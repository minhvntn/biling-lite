using System.Collections.ObjectModel;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Server.Admin.App;

public sealed class StartupProgressEventArgs : EventArgs
{
    public StartupProgressEventArgs(string message, int progressPercent)
    {
        Message = message;
        ProgressPercent = Math.Clamp(progressPercent, 0, 100);
    }

    public string Message { get; }
    public int ProgressPercent { get; }
}

public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient = new();
    private readonly DispatcherTimer _healthTimer = new();
    private readonly DispatcherTimer _machinesTimer = new();
    private readonly DispatcherTimer _systemLogsTimer = new();
    private readonly DispatcherTimer _machineSearchDebounceTimer = new();
    private readonly DispatcherTimer _memberSearchDebounceTimer = new();
    private readonly DispatcherTimer _memberModalAutoCloseTimer = new();

    private readonly ObservableCollection<MachineRow> _machineRows = new();
    private readonly ObservableCollection<MemberRow> _memberRows = new();
    private readonly ObservableCollection<MemberTransactionRow> _memberTransactionRows = new();
    private readonly ObservableCollection<SystemLogRow> _systemLogRows = new();
    private readonly ObservableCollection<MachineTimelineRow> _machineTimelineRows = new();
    private readonly ObservableCollection<SessionLogRow> _sessionLogRows = new();
    private readonly ObservableCollection<WebsiteLogRow> _websiteLogRows = new();
    private readonly ObservableCollection<GroupSummaryRow> _groupSummaryRows = new();
    private readonly ObservableCollection<GroupMachineRow> _groupMachineRows = new();
    private readonly ObservableCollection<ServiceItemRow> _serviceItemRows = new();
    private readonly ObservableCollection<string> _webFilterDomainRows = new();
    private readonly ObservableCollection<LoyaltySpinSettingRow> _loyaltySpinSettingRows = new();

    private readonly List<MachineRow> _allMachineRows = new();
    private PricingSettingsResponse? _pricingSettings;

    private AdminShellSettings _settings = new();
    private string _searchKeyword = string.Empty;
    private string _statusFilter = I18n.StatusAll;
    private string? _selectedMachineId;
    private readonly HashSet<string> _selectedMachineIds = new(StringComparer.OrdinalIgnoreCase);
    private string _memberSearchKeyword = string.Empty;
    private string? _selectedMemberId;
    private string? _selectedGroupSummaryId;
    private string? _selectedGroupMachinePcId;
    private string? _selectedServiceItemId;
    private bool _isRefreshingMachines;
    private bool _isRefreshingSystemLogs;
    private bool _fontSizeInitialized;
    private bool _machineTableFontSizeInitialized;
    private bool _machineContextMenuPaddingInitialized;
    private bool _machineContextMenuFontSizeInitialized;
    private bool _loyaltySettingsInitialized;
    private bool _isLoadingLoyaltySettings;
    private bool _readyShutdownSettingsInitialized;
    private bool _isLoadingReadyShutdownSettings;
    private int _readyAutoShutdownMinutes = 3;
    private string _lockScreenBackgroundMode = "none";
    private string _lockScreenBackgroundUrl = string.Empty;
    private bool _webFilterSettingsInitialized;
    private bool _isLoadingWebFilterSettings;
    private bool _websiteLogSettingsInitialized;
    private bool _isLoadingWebsiteLogSettings;
    private bool _loyaltySpinSettingsInitialized;
    private bool _isLoadingLoyaltySpinSettings;
    private bool _websiteLogFiltersInitialized;
    private bool _isUpdatingWebsiteLogMachineFilters;
    private bool _guestLoginSettingsInitialized;
    private bool _isLoadingGuestLoginSettings;
    private bool _backupSettingsInitialized;
    private bool _isLoadingBackupSettings;
    private bool _transactionReportInitialized;
    private TaskCompletionSource<decimal?>? _topupModalTcs;
    private readonly Stack<decimal> _topupModalHistory = new();
    private decimal _topupModalAmount;
    private bool _topupModalIsDeduct;
    private bool _topupModalAllowDeduct = true;
    private Point _groupMachineDragStartPoint;
    private GroupMachineRow? _draggingGroupMachine;
    private readonly HashSet<string> _memberTransferNotifiedEventIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _memberTransferNotificationsInitialized;
    private const int MachinesTabIndex = 0;
    private const int MembersTabIndex = 1;
    private const int SystemLogsTabIndex = 2;
    private const int MembersCacheTtlSeconds = 4;
    private const int SystemLogsCacheTtlSeconds = 3;
    private const int WebsiteLogsCacheTtlSeconds = 4;
    private string _membersCacheKey = string.Empty;
    private MemberListResponse? _membersCacheResponse;
    private DateTime _membersCacheAtUtc = DateTime.MinValue;
    private int _systemLogsCacheLimit = -1;
    private SystemEventsResponse? _systemLogsCacheResponse;
    private DateTime _systemLogsCacheAtUtc = DateTime.MinValue;
    private List<SystemEventItem> _latestSystemEventItems = new();
    private bool _isUpdatingSystemLogMachineFilter;
    private string _websiteLogsCacheKey = string.Empty;
    private WebsiteLogsResponse? _websiteLogsCacheResponse;
    private DateTime _websiteLogsCacheAtUtc = DateTime.MinValue;
    private bool _startupInitialized;

    public event EventHandler<StartupProgressEventArgs>? StartupProgressChanged;
    public event EventHandler<bool>? StartupInitializationCompleted;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupInitialized)
        {
            return;
        }

        _startupInitialized = true;
        var startupSuccess = true;

        try
        {
            ReportStartupProgress("Đang khởi tạo giao diện...", 10);

            _settings = LoadSettings();
            ApplyColorSettings();
            UpdateServerIpDisplay();
            ApplyUiFontSize(_settings.UiFontSize);
            ApplyMachineTableFontSize(_settings.MachineTableFontSize);
            ApplyMachineContextMenuPadding(_settings.MachineContextMenuItemPadding);
            ApplyMachineContextMenuFontSize(_settings.MachineContextMenuFontSize);

            MachinesDataGrid.ItemsSource = _machineRows;
            MembersDataGrid.ItemsSource = _memberRows;
            MemberTransactionsDataGrid.ItemsSource = _memberTransactionRows;
            SystemLogsDataGrid.ItemsSource = _systemLogRows;
            MachineTimelineListBox.ItemsSource = _machineTimelineRows;
            SessionLogsDataGrid.ItemsSource = _sessionLogRows;
            WebsiteLogsDataGrid.ItemsSource = _websiteLogRows;
            GroupSummaryDataGrid.ItemsSource = _groupSummaryRows;
            GroupMachinesDataGrid.ItemsSource = _groupMachineRows;
            ServiceItemsDataGrid.ItemsSource = _serviceItemRows;
            WebFilterDomainsListBox.ItemsSource = _webFilterDomainRows;
            MiniGameSpinSettingsDataGrid.ItemsSource = _loyaltySpinSettingRows;
            FontSizeSlider.Value = _settings.UiFontSize;
            FontSizeValueTextBlock.Text = _settings.UiFontSize.ToString("0");
            MachineTableFontSizeSlider.Value = _settings.MachineTableFontSize;
            MachineTableFontSizeValueTextBlock.Text = _settings.MachineTableFontSize.ToString("0");
            MachineContextMenuPaddingSlider.Value = _settings.MachineContextMenuItemPadding;
            MachineContextMenuPaddingValueTextBlock.Text = _settings.MachineContextMenuItemPadding.ToString("0");
            MachineContextMenuFontSizeSlider.Value = _settings.MachineContextMenuFontSize;
            MachineContextMenuFontSizeValueTextBlock.Text = _settings.MachineContextMenuFontSize.ToString("0");
            RevenueAnchorDatePicker.SelectedDate = DateTime.Today;
            WebsiteLogFromDatePicker.SelectedDate = DateTime.Today.AddDays(-1);
            WebsiteLogToDatePicker.SelectedDate = DateTime.Today;
            RefreshWebsiteLogMachineFilterOptions();
            InitializeSystemLogMachineFilter();
            _fontSizeInitialized = true;
            _machineTableFontSizeInitialized = true;
            _machineContextMenuPaddingInitialized = true;
            _machineContextMenuFontSizeInitialized = true;
            _transactionReportInitialized = true;
            InitializeLoyaltyRanksTab();
            InitializeMiniGameTab();
            InitializeAppBlockTab();
            InitializeWakeLanProfilesUi();

            _memberModalAutoCloseTimer.Interval = TimeSpan.FromMinutes(2);
            _memberModalAutoCloseTimer.Tick += MemberModalAutoCloseTimer_Tick;

            ApplyUserRoleRestrictions();

            _ = LoadServerUsersAsync();
            _ = LoadDatabaseStorageStatsAsync();

            _healthTimer.Interval = TimeSpan.FromSeconds(5);
            _healthTimer.Tick += HealthTimer_Tick;
            _healthTimer.Start();

            _machineSearchDebounceTimer.Interval = TimeSpan.FromMilliseconds(250);
            _machineSearchDebounceTimer.Tick += MachineSearchDebounceTimer_Tick;

            _memberSearchDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
            _memberSearchDebounceTimer.Tick += MemberSearchDebounceTimer_Tick;

            _machinesTimer.Interval = TimeSpan.FromSeconds(1);
            _machinesTimer.Tick += MachinesTimer_Tick;
            _machinesTimer.Start();

            _systemLogsTimer.Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.MachineRefreshSeconds));
            _systemLogsTimer.Tick += SystemLogsTimer_Tick;
            _systemLogsTimer.Start();

            ReportStartupProgress("Đang kết nối realtime...", 40);
            InitializeGuestLoginNotifications();
            InitializeRealtimeMachineRefreshBridge();
            _ = ConnectRealtimeMachineRefreshAsync();

            ReportStartupProgress("Đang kiểm tra backend...", 60);
            await CheckBackendHealthAsync();

            ReportStartupProgress("Đang tải dữ liệu nền...", 80);
            _ = ContinueStartupBackgroundLoadAsync();
            ReportStartupProgress("Đã sẵn sàng. Dữ liệu sẽ tiếp tục cập nhật.", 100);
        }
        catch (Exception ex)
        {
            startupSuccess = false;
            MessageBox.Show(
                $"Khởi động ứng dụng gặp lỗi: {ex.Message}",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            StartupInitializationCompleted?.Invoke(this, startupSuccess);
        }
    }

    private void ReportStartupProgress(string message, int progressPercent)
    {
        StartupProgressChanged?.Invoke(this, new StartupProgressEventArgs(message, progressPercent));
    }

    private async Task ContinueStartupBackgroundLoadAsync()
    {
        try
        {
            await RefreshAllDataAsync();
            _loyaltySettingsInitialized = true;
            _readyShutdownSettingsInitialized = true;
            _webFilterSettingsInitialized = true;
            _websiteLogSettingsInitialized = true;
            _websiteLogFiltersInitialized = true;
            _loyaltySpinSettingsInitialized = true;
            _backupSettingsInitialized = true;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Tải dữ liệu nền hoàn tất.");
        }
        catch (Exception ex)
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Tải dữ liệu nền lỗi: {ex.Message}");
        }
    }

    private void ApplyUserRoleRestrictions()
    {
        if (AdminSession.CurrentUserRole == "STAFF")
        {
            // Hide tabs not allowed for Staff
            VipTab.Visibility = Visibility.Collapsed;
            StatisticsTabItem.Visibility = Visibility.Collapsed;
            MiniGameTabItem.Visibility = Visibility.Collapsed;
            SettingsTab.Visibility = Visibility.Collapsed;
            AdminTab.Visibility = Visibility.Collapsed;
            
            // Optionally select the first allowed tab if a hidden one was selected by default
            if (MainTabControl.SelectedItem is TabItem selectedTab && selectedTab.Visibility == Visibility.Collapsed)
            {
                MainTabControl.SelectedItem = WorkstationsTab;
            }
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _healthTimer.Stop();
        _machinesTimer.Stop();
        _systemLogsTimer.Stop();
        _machineSearchDebounceTimer.Stop();
        _memberSearchDebounceTimer.Stop();
        ShutdownGuestLoginNotifications();
        ShutdownRealtimeMachineRefreshBridge();
        _httpClient.Dispose();
    }

    private async void MachinesTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await RefreshMachinesAsync();
    }

    private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (e.Source == MainTabControl)
        {
            if (MainTabControl.SelectedItem is TabItem tabItem)
            {
                if (tabItem == StatisticsTabItem)
                {
                    await RefreshStatisticsAsync();
                }
                else if (tabItem == LogsTabItem)
                {
                    await RefreshActiveLogTabAsync();
                }
                else if (tabItem == MiniGameTabItem)
                {
                    await RefreshMiniGameSpinSettingsAsync();
                }
                else if (tabItem == AdminTab)
                {
                    _ = LoadServerUsersAsync();
                    _ = LoadDatabaseStorageStatsAsync();
                }
                else
                {
                    var header = tabItem.Header?.ToString();
                    if (header is null || header == "System.Windows.Controls.StackPanel")
                    {
                        if (tabItem.Header is StackPanel sp)
                        {
                            var textBlock = sp.Children.OfType<TextBlock>().LastOrDefault();
                            header = textBlock?.Text;
                        }
                    }

                    switch (header)
                    {
                        case "Máy trạm":
                            await RefreshMachinesAsync();
                            break;
                        case "Tài khoản":
                            await RefreshMembersAsync();
                            break;
                        case "Nhóm máy":
                            await RefreshGroupsAsync();
                            break;
                        case "Dịch vụ":
                            await RefreshServiceItemsAsync();
                            break;
                    }
                }
            }
        }
        else if (e.Source == LogsTabControl)
        {
            await RefreshActiveLogTabAsync();
        }
        else if (e.Source == StatsTabControl)
        {
            if (StatsTabControl.SelectedItem == RevenueStatsTabItem)
            {
                await RefreshStatisticsAsync();
            }
            else if (StatsTabControl.SelectedItem == PcMachineStatsTabItem)
            {
                await RefreshPcRevenueStatsAsync();
            }
        }
    }

    private async Task RefreshActiveLogTabAsync()
    {
        if (!IsLoaded) return;

        if (LogsTabControl.SelectedItem is TabItem subTab)
        {
            if (subTab == SystemLogsTabItem)
            {
                await RefreshSystemLogsAsync();
            }
            else if (subTab == TransactionLogsTabItem)
            {
                await RefreshTransactionLogsAsync();
            }
            else if (subTab == WebsiteLogsTabItem)
            {
                await RefreshWebsiteLogsAsync();
            }
        }
    }

    private async Task RefreshAllDataAsync()
    {
        await RefreshMachinesAsync();
        await RefreshMembersAsync();
        await RefreshSystemLogsAsync();
        await RefreshTransactionLogsAsync();
        await RefreshGroupsAsync(forceReloadPricing: true);
        await RefreshServiceItemsAsync();
        await LoadLoyaltySettingsAsync();
        await LoadClientRuntimeSettingsAsync();
        await LoadGuestLoginSettingsAsync();
        await LoadBackupSettingsAsync();
        await RefreshWebFilterSettingsAsync();
        await LoadWebsiteLogSettingsAsync();
        await RefreshWebsiteLogsAsync();
        await RefreshLoyaltyRanksAsync();
        await RefreshMiniGameSpinSettingsAsync();
        await RefreshStatisticsAsync();
        await RefreshAppBlockSettingsAsync();
        _appBlockSettingsInitialized = true;
    }

    private bool IsMembersTabActive()
    {
        return IsLoaded && MainTabControl.SelectedIndex == MembersTabIndex;
    }

    private bool IsSystemLogsTabActive()
    {
        return IsLoaded && MainTabControl.SelectedItem == LogsTabItem && LogsTabControl.SelectedItem == SystemLogsTabItem;
    }

    private static bool IsCacheValid(DateTime cachedAtUtc, int ttlSeconds)
    {
        return cachedAtUtc != DateTime.MinValue &&
               (DateTime.UtcNow - cachedAtUtc) <= TimeSpan.FromSeconds(ttlSeconds);
    }

    private void InvalidateMembersCache()
    {
        _membersCacheAtUtc = DateTime.MinValue;
        _membersCacheKey = string.Empty;
        _membersCacheResponse = null;
    }

    private void InvalidateSystemLogsCache()
    {
        _systemLogsCacheAtUtc = DateTime.MinValue;
        _systemLogsCacheLimit = -1;
        _systemLogsCacheResponse = null;
    }

    private void InvalidateWebsiteLogsCache()
    {
        _websiteLogsCacheAtUtc = DateTime.MinValue;
        _websiteLogsCacheKey = string.Empty;
        _websiteLogsCacheResponse = null;
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Bạn có chắc chắn muốn đăng xuất không?", "Xác nhận đăng xuất", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            // Clear session info
            AdminSession.CurrentUserRole = null;
            AdminSession.CurrentUserFullName = null;

            // Show login window
            var loginWindow = new LoginWindow();
            loginWindow.Show();

            // Close current window
            this.Close();
        }
    }

    private void MemberModalAutoCloseTimer_Tick(object? sender, EventArgs e)
    {
        _memberModalAutoCloseTimer.Stop();
        HideAddMemberModal();
        if (_topupModalTcs != null)
        {
            CloseTopupModal(null);
        }
    }
}

