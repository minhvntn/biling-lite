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
using Server.Admin.App.Windows;

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
    internal readonly HttpClient _httpClient = new();
    internal readonly DispatcherTimer _healthTimer = new();
    private readonly DispatcherTimer _machinesTimer = new();
    private readonly DispatcherTimer _systemLogsTimer = new();
    private readonly DispatcherTimer _membersAutoRefreshTimer = new();
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
    private readonly ObservableCollection<ServiceCategoryViewModel> _serviceCategoryRows = new();
    private readonly ObservableCollection<ServiceTopItemViewModel> _servicesTabTopServiceRows = new();
    private readonly ObservableCollection<ServiceCategoryRevenueViewModel> _servicesTabCategoryRevenueRows = new();
    private string _currentSelectedServiceCategory = "Tất cả dịch vụ";
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
    private bool _isRefreshingMembersAuto;
    private bool _fontSizeInitialized;
    private bool _fontFamilyInitialized;
    private bool _mainMenuTabIconSizeInitialized;
    private bool _machineTableFontSizeInitialized;
    private bool _machineTableIconSizeInitialized;
    private bool _machineTableItemSpacingInitialized;
    private bool _machineContextMenuPaddingInitialized;
    private bool _machineContextMenuFontSizeInitialized;
    private bool _loyaltySettingsInitialized;
    private bool _isLoadingLoyaltySettings;
    private bool _readyShutdownSettingsInitialized;
    private bool _isLoadingReadyShutdownSettings;
    private int _readyAutoShutdownMinutes = 3;
    private int _autoCollapseIntervalSeconds = 0;
    private string _gameLauncherPath = string.Empty;
    private bool _vpnEnabled = true;
    private string _vpnLocalInstallerPath = @"D:\Games\Tools\Cloudflare_WARP_Release-x64.msi";
    private string _vpnDownloadUrl = "https://1111-releases.cloudflareclient.com/windows/Cloudflare_WARP_Release-x64.msi";
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
    
    private bool _topupPromoSettingsInitialized;
    private bool _isLoadingTopupPromoSettings;
    private bool _topupPromoEnabled = false;
    private readonly ObservableCollection<TopupPromoTierRow> _topupPromoTierRows = new();

    private bool _backupSettingsInitialized;
    private bool _isLoadingBackupSettings;
    private bool _transactionReportInitialized;
    private TaskCompletionSource<decimal?>? _topupModalTcs;
    private readonly Stack<decimal> _topupModalHistory = new();
    private decimal _topupModalAmount;
    private bool _topupModalIsDeduct;
    private bool _topupModalAllowDeduct = true;
    private bool _topupModalIsGift;
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
        _httpClient.DefaultRequestHeaders.Add("X-API-KEY", "SmbSecureLANKey2026");
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        PreviewMouseLeftButtonDown += MainWindow_PreviewMouseLeftButtonDown;
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
            ApplyUiFontFamily(_settings.UiFontFamily);
            ApplyMainMenuTabIconSize(_settings.MainMenuTabIconSize);
            ApplyMachineTableFontSize(_settings.MachineTableFontSize);
            ApplyMachineTableIconSize(_settings.MachineTableIconSize);
            ApplyMachineTableItemSpacing(_settings.MachineTableItemSpacing);
            ApplyMachineContextMenuPadding(_settings.MachineContextMenuItemPadding);
            ApplyMachineContextMenuFontSize(_settings.MachineContextMenuFontSize);

            MachinesDataGrid.ItemsSource = _machineRows;
            MembersDataGrid.ItemsSource = _memberRows;
            // MemberTransactionsDataGrid.ItemsSource = _memberTransactionRows;
            SystemLogsDataGrid.ItemsSource = _systemLogRows;
            MachineTimelineListBox.ItemsSource = _machineTimelineRows;
            SessionLogsDataGrid.ItemsSource = _sessionLogRows;
            WebsiteLogsDataGrid.ItemsSource = _websiteLogRows;
            GroupSummaryDataGrid.ItemsSource = _groupSummaryRows;
            GroupMachinesDataGrid.ItemsSource = _groupMachineRows;
            ServiceItemsDataGrid.ItemsSource = _serviceItemRows;
            ServiceCategoryListBox.ItemsSource = _serviceCategoryRows;
            TopServiceSellingItemsControl.ItemsSource = _servicesTabTopServiceRows;
            ServiceCategoryRevenueItemsControl.ItemsSource = _servicesTabCategoryRevenueRows;
            WebFilterDomainsDataGrid.ItemsSource = _webFilterDomainRows;
            MiniGameSpinSettingsDataGrid.ItemsSource = _loyaltySpinSettingRows;
            TopupPromoTiersDataGrid.ItemsSource = _topupPromoTierRows;
            FontSizeSlider.Value = _settings.UiFontSize;
            FontSizeValueTextBlock.Text = _settings.UiFontSize.ToString("0");
            if (MainMenuTabIconSizeSlider is not null)
            {
                MainMenuTabIconSizeSlider.Value = _settings.MainMenuTabIconSize;
            }
            if (MainMenuTabIconSizeValueTextBlock is not null)
            {
                MainMenuTabIconSizeValueTextBlock.Text = _settings.MainMenuTabIconSize.ToString("0");
            }
            MachineTableFontSizeSlider.Value = _settings.MachineTableFontSize;
            MachineTableFontSizeValueTextBlock.Text = _settings.MachineTableFontSize.ToString("0");
            if (MachineTableIconSizeSlider is not null)
            {
                MachineTableIconSizeSlider.Value = _settings.MachineTableIconSize;
            }
            if (MachineTableIconSizeValueTextBlock is not null)
            {
                MachineTableIconSizeValueTextBlock.Text = _settings.MachineTableIconSize.ToString("0");
            }
            if (MachineTableItemSpacingSlider is not null)
            {
                MachineTableItemSpacingSlider.Value = _settings.MachineTableItemSpacing;
            }
            if (MachineTableItemSpacingValueTextBlock is not null)
            {
                MachineTableItemSpacingValueTextBlock.Text = _settings.MachineTableItemSpacing.ToString("0");
            }
            MachineContextMenuPaddingSlider.Value = _settings.MachineContextMenuItemPadding;
            MachineContextMenuPaddingValueTextBlock.Text = _settings.MachineContextMenuItemPadding.ToString("0");
            MachineContextMenuFontSizeSlider.Value = _settings.MachineContextMenuFontSize;
            MachineContextMenuFontSizeValueTextBlock.Text = _settings.MachineContextMenuFontSize.ToString("0");
            if (HideDisabledContextMenuItemsCheckBox is not null)
            {
                HideDisabledContextMenuItemsCheckBox.IsChecked = _settings.HideDisabledContextMenuItems;
            }
            if (AutoBackupOnExitCheckBox is not null)
            {
                AutoBackupOnExitCheckBox.IsChecked = _settings.AutoBackupOnExit;
            }
            _showOfflineMachines = _settings.ShowOfflineMachines;
            if (ShowOfflineMachinesIcon is not null)
            {
                ShowOfflineMachinesIcon.Text = _showOfflineMachines ? "✔" : "";
            }
            if (ShowOfflineMachinesMenuItem is not null)
            {
                ShowOfflineMachinesMenuItem.FontWeight = _showOfflineMachines ? FontWeights.Bold : FontWeights.Normal;
            }
            if (ShowOfflineMachinesIcon2 is not null)
            {
                ShowOfflineMachinesIcon2.Text = _showOfflineMachines ? "✔" : "";
            }
            if (ShowOfflineMachinesMenuItem2 is not null)
            {
                ShowOfflineMachinesMenuItem2.FontWeight = _showOfflineMachines ? FontWeights.Bold : FontWeights.Normal;
            }
            ApplyMachineColumnVisibilities();
            RevenueAnchorDatePicker.SelectedDate = DateTime.Today;
            WebsiteLogFromDatePicker.SelectedDate = DateTime.Today.AddDays(-1);
            WebsiteLogToDatePicker.SelectedDate = DateTime.Today;
            RefreshWebsiteLogMachineFilterOptions();
            InitializeSystemLogMachineFilter();
            _fontSizeInitialized = true;
            if (AppFontFamilyComboBox is not null)
            {
                foreach (ComboBoxItem item in AppFontFamilyComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), _settings.UiFontFamily, StringComparison.OrdinalIgnoreCase))
                    {
                        AppFontFamilyComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            _fontFamilyInitialized = true;
            _mainMenuTabIconSizeInitialized = true;
            _machineTableFontSizeInitialized = true;
            _machineTableIconSizeInitialized = true;
            _machineTableItemSpacingInitialized = true;
            _machineContextMenuPaddingInitialized = true;
            _machineContextMenuFontSizeInitialized = true;
            _transactionReportInitialized = true;
            InitializeWakeLanProfilesUi();
            InitializeTaxReportUi();
            InitializeChatbot();
            if (!_settings.IsLiteMode)
            {
                EnsureProTabsInitialized();
            }

            _memberModalAutoCloseTimer.Interval = TimeSpan.FromMinutes(2);
            _memberModalAutoCloseTimer.Tick += MemberModalAutoCloseTimer_Tick;

            ApplyUserRoleRestrictions();
            UpdateLoginStatusUi();
            if (AdminSession.IsLoggedIn)
            {
                _settings.IsLiteMode = false;
            }
            UpdateAppModeUi();

            if (AdminSession.IsLoggedIn && !_settings.IsLiteMode)
            {
                _ = LoadServerUsersAsync();
                _ = LoadDatabaseStorageStatsAsync();
                _ = LoadClientManagementSettingsAsync();
            }

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

            _membersAutoRefreshTimer.Interval = TimeSpan.FromMinutes(1);
            _membersAutoRefreshTimer.Tick += MembersAutoRefreshTimer_Tick;
            _membersAutoRefreshTimer.Start();

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
            if (startupSuccess)
            {
                TriggerStartupLoginDialog();
            }
        }
    }

    private async void TriggerStartupLoginDialog()
    {
        // Cho app chạy lên và hiển thị mượt mà trước, delay 1 giây rồi mới hiển thị form đăng nhập
        await Task.Delay(1000);

        if (!IsLoaded || !IsVisible || AdminSession.IsLoggedIn)
        {
            return;
        }

        if (Application.Current.Windows.OfType<LoginWindow>().Any())
        {
            return;
        }

        var loginWindow = new LoginWindow { Owner = this };
        loginWindow.ShowDialog();
        if (AdminSession.IsLoggedIn)
        {
            HandleSuccessfulLogin();
        }
    }

    private void HandleSuccessfulLogin()
    {
        if (!AdminSession.IsLoggedIn)
        {
            return;
        }

        _settings.IsLiteMode = false;
        SaveSettings();
        UpdateLoginStatusUi();
        UpdateAppModeUi();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] 🖥️ Đăng nhập thành công ({AdminSession.CurrentUserFullName}). Đã tự động chuyển sang Bản Pro: Kích hoạt đầy đủ 12 tab tính năng quản trị.");
        _ = RefreshAllDataAsync();
        _ = LoadServerUsersAsync();
        _ = LoadDatabaseStorageStatsAsync();
        _ = LoadClientManagementSettingsAsync();
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
            
            // Hide sensitive financial info
            StatTotalTopupCardBorder.Visibility = Visibility.Collapsed;
            MemberTotalTopupColumn.Visibility = Visibility.Collapsed;
            MemberDetailsTotalTopupBorder.Visibility = Visibility.Collapsed;

            // Optionally select the first allowed tab if a hidden one was selected by default
            if (MainTabControl.SelectedItem is TabItem selectedTab && selectedTab.Visibility == Visibility.Collapsed)
            {
                MainTabControl.SelectedItem = WorkstationsTab;
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

    public static void TrimProcessMemory()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                using var proc = Process.GetCurrentProcess();
                SetProcessWorkingSetSize(proc.Handle, -1, -1);
            }
        }
        catch
        {
            // Keep app responsive
        }
    }

    private void ToggleAppModeButton_Click(object sender, RoutedEventArgs e)
    {
        // Khi chưa đăng nhập: Không cho phép switch, chỉ được dùng Bản Lite, yêu cầu đăng nhập
        if (!AdminSession.IsLoggedIn)
        {
            var msgResult = MessageBox.Show(
                "Bạn cần đăng nhập tài khoản quản trị để chuyển sang Bản Pro (đầy đủ các tab tính năng).\nBạn có muốn đăng nhập ngay bây giờ không?",
                "Yêu cầu đăng nhập",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (msgResult == MessageBoxResult.Yes)
            {
                var loginWindow = new LoginWindow { Owner = this };
                loginWindow.ShowDialog();
                if (AdminSession.IsLoggedIn)
                {
                    HandleSuccessfulLogin();
                }
            }
            return;
        }

        // Đã đăng nhập: Cho phép switch qua lại giữa Bản Lite và Bản Pro tự do
        _settings.IsLiteMode = !_settings.IsLiteMode;
        SaveSettings();
        UpdateAppModeUi();

        if (_settings.IsLiteMode)
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] 🍃 Đã chuyển sang Bản Lite: Đã tối ưu giải phóng RAM, giảm tải CPU & mạng, chỉ giữ 3 tab chính (Máy trạm, Hội viên, Nhật ký).");
        }
        else
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] 🖥️ Đã chuyển sang Bản Pro: Kích hoạt đầy đủ 12 tab tính năng quản trị.");
            _ = RefreshAllDataAsync();
        }
    }

    private bool _proTabsInitialized;
    private void EnsureProTabsInitialized()
    {
        if (_proTabsInitialized) return;
        _proTabsInitialized = true;

        InitializeLoyaltyRanksTab();
        InitializeMiniGameTab();
        InitializeAppBlockTab();
        InitializeVirtualPcsTab();
    }

    private void UpdateAppModeUi()
    {
        // Khi chưa đăng nhập: Luôn ép sử dụng Bản Lite
        if (!AdminSession.IsLoggedIn)
        {
            _settings.IsLiteMode = true;
        }

        bool isLite = _settings.IsLiteMode;

        if (isLite)
        {
            // 1. Tối ưu timer nền: giảm tần suất các chu kỳ polling để tiết kiệm tối đa CPU & mạng
            _healthTimer.Interval = TimeSpan.FromSeconds(30);
            _machinesTimer.Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.MachineRefreshSeconds));
            _systemLogsTimer.Interval = TimeSpan.FromSeconds(15);
            _membersAutoRefreshTimer.Interval = TimeSpan.FromMinutes(2);

            // 2. Chỉ hiển thị 3 tab chính: Máy trạm, Hội viên, Nhật ký
            if (WorkstationsTab != null) WorkstationsTab.Visibility = Visibility.Visible;
            if (MembersTab != null) MembersTab.Visibility = Visibility.Visible;
            if (LogsTabItem != null) LogsTabItem.Visibility = Visibility.Visible;

            if (GroupsTab != null) GroupsTab.Visibility = Visibility.Collapsed;
            if (ServicesTab != null) ServicesTab.Visibility = Visibility.Collapsed;
            if (BlockTab != null) BlockTab.Visibility = Visibility.Collapsed;
            if (VipTab != null) VipTab.Visibility = Visibility.Collapsed;
            if (ComboTab != null) ComboTab.Visibility = Visibility.Collapsed;
            if (StatisticsTabItem != null) StatisticsTabItem.Visibility = Visibility.Collapsed;
            if (MiniGameTabItem != null) MiniGameTabItem.Visibility = Visibility.Collapsed;
            if (SettingsTab != null) SettingsTab.Visibility = Visibility.Collapsed;
            if (AdminTab != null) AdminTab.Visibility = Visibility.Collapsed;

            // Bản Lite: Ẩn 3 cột Ngày, IP, Nhóm bên tab Máy trạm
            ApplyMachineColumnVisibilities();

            // Bản Lite: Ẩn 3 block thống kê (Tổng số dư, Tổng giờ chơi, Tổng nạp) bên tab Hội viên
            if (StatTotalBalanceCardBorder != null) StatTotalBalanceCardBorder.Visibility = Visibility.Collapsed;
            if (StatTotalHoursCardBorder != null) StatTotalHoursCardBorder.Visibility = Visibility.Collapsed;
            if (StatTotalTopupCardBorder != null) StatTotalTopupCardBorder.Visibility = Visibility.Collapsed;

            // Bản Lite: Ẩn nút "Nhập từ GCafe" bên tab Hội viên
            if (ImportGcafeMemberButton != null) ImportGcafeMemberButton.Visibility = Visibility.Collapsed;

            // Bản Lite: Tắt và ẩn hoàn toàn widget Chatbot AI (Ami)
            UpdateChatbotWidgetVisibility();

            // Nút hiển thị mời chuyển sang "Bản Pro"
            if (ToggleAppModeButton != null)
            {
                ToggleAppModeButton.ToolTip = AdminSession.IsLoggedIn
                    ? "Đang ở Bản Lite (chế độ siêu nhẹ). Nhấn để chuyển sang Bản Pro (đầy đủ các tab tính năng)"
                    : "Đang ở Bản Lite. Đăng nhập tài khoản quản trị để chuyển sang Bản Pro";
            }
            if (ModeIconTextBlock != null)
            {
                ModeIconTextBlock.Text = "🖥️";
            }
            if (ModeTextLabel != null)
            {
                ModeTextLabel.Text = "Bản Pro";
                ModeTextLabel.Foreground = Brushes.White;
            }

            // Nếu tab hiện tại đang bị ẩn thì chuyển sang tab Máy trạm
            if (MainTabControl != null && MainTabControl.SelectedItem is TabItem selected && selected.Visibility == Visibility.Collapsed)
            {
                MainTabControl.SelectedItem = WorkstationsTab;
            }

            // 3. Tối ưu bộ nhớ RAM: ép dọn rác và thu gọn Working Set ngay lập tức
            TrimProcessMemory();
        }
        else
        {
            EnsureProTabsInitialized();

            // 1. Khôi phục timer Bản Pro
            _healthTimer.Interval = TimeSpan.FromSeconds(5);
            _machinesTimer.Interval = TimeSpan.FromSeconds(1);
            _systemLogsTimer.Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.MachineRefreshSeconds));
            _membersAutoRefreshTimer.Interval = TimeSpan.FromMinutes(1);

            // 2. Hiển thị đầy đủ tất cả các tab
            if (WorkstationsTab != null) WorkstationsTab.Visibility = Visibility.Visible;
            if (MembersTab != null) MembersTab.Visibility = Visibility.Visible;
            if (LogsTabItem != null) LogsTabItem.Visibility = Visibility.Visible;
            if (GroupsTab != null) GroupsTab.Visibility = Visibility.Visible;
            if (ServicesTab != null) ServicesTab.Visibility = Visibility.Visible;
            if (BlockTab != null) BlockTab.Visibility = Visibility.Visible;
            if (VipTab != null) VipTab.Visibility = Visibility.Visible;
            if (ComboTab != null) ComboTab.Visibility = Visibility.Visible;
            if (StatisticsTabItem != null) StatisticsTabItem.Visibility = Visibility.Visible;
            if (MiniGameTabItem != null) MiniGameTabItem.Visibility = Visibility.Visible;
            if (SettingsTab != null) SettingsTab.Visibility = Visibility.Visible;
            if (AdminTab != null) AdminTab.Visibility = Visibility.Visible;

            // Bản Pro: Hiển thị lại các cột theo thiết lập người dùng
            ApplyMachineColumnVisibilities();

            // Bản Pro: Hiển thị lại các block thống kê bên tab Hội viên
            if (StatTotalBalanceCardBorder != null) StatTotalBalanceCardBorder.Visibility = Visibility.Visible;
            if (StatTotalHoursCardBorder != null) StatTotalHoursCardBorder.Visibility = Visibility.Visible;
            if (StatTotalTopupCardBorder != null)
            {
                StatTotalTopupCardBorder.Visibility = (AdminSession.CurrentUserRole == "STAFF") ? Visibility.Collapsed : Visibility.Visible;
            }

            // Bản Pro: Hiển thị lại nút "Nhập từ GCafe" bên tab Hội viên
            if (ImportGcafeMemberButton != null) ImportGcafeMemberButton.Visibility = Visibility.Visible;

            // Bản Pro: Khôi phục hiển thị widget Chatbot AI nếu đang được bật
            UpdateChatbotWidgetVisibility();

            // Áp dụng giới hạn vai trò nhân viên nếu có
            ApplyUserRoleRestrictions();

            // Nút hiển thị mời chuyển sang "Bản Lite"
            if (ToggleAppModeButton != null)
            {
                ToggleAppModeButton.ToolTip = "Đang ở Bản Pro. Nhấn để chuyển sang Bản Lite (chỉ Máy trạm, Hội viên, Nhật ký)";
            }
            if (ModeIconTextBlock != null)
            {
                ModeIconTextBlock.Text = "🍂";
            }
            if (ModeTextLabel != null)
            {
                ModeTextLabel.Text = "Bản Lite";
                ModeTextLabel.Foreground = Brushes.White;
            }
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            TrimProcessMemory();
        }
        else if (WindowState == WindowState.Normal || WindowState == WindowState.Maximized)
        {
            _ = RefreshMachinesAsync();
        }
    }

    private bool _isClosingWithBackup;

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosingWithBackup)
        {
            return;
        }

        bool backupOnExit = _settings.AutoBackupOnExit;
        if (AutoBackupOnExitCheckBox is not null && AutoBackupOnExitCheckBox.IsChecked.HasValue)
        {
            backupOnExit = AutoBackupOnExitCheckBox.IsChecked.Value;
        }

        if (!backupOnExit)
        {
            return;
        }

        // Tạm hoãn đóng cửa sổ để gọi API backup ngầm trước khi app tắt
        e.Cancel = true;
        _isClosingWithBackup = true;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            Title = $"{Title} - [Đang tự động sao lưu dữ liệu trước khi thoát...]";
            await PerformExitBackupAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExitBackup] Exception on close: {ex.Message}");
        }
        finally
        {
            Mouse.OverrideCursor = null;
            Close();
        }
    }

    private async Task PerformExitBackupAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var payload = new
            {
                requestedBy = "admin.desktop.exit",
                trigger = "exit"
            };

            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl("/settings/backup/run"),
                payload,
                cts.Token);

            if (response.IsSuccessStatusCode)
            {
                Debug.WriteLine("[ExitBackup] Tự động sao lưu khi tắt ứng dụng thành công.");
            }
            else
            {
                Debug.WriteLine($"[ExitBackup] Tự động sao lưu thất bại với mã: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExitBackup] Lỗi hoặc timeout sao lưu khi tắt app: {ex.Message}");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _healthTimer.Stop();
        _machinesTimer.Stop();
        _systemLogsTimer.Stop();
        _membersAutoRefreshTimer.Stop();
        _machineSearchDebounceTimer.Stop();
        _memberSearchDebounceTimer.Stop();
        ShutdownGuestLoginNotifications();
        ShutdownRealtimeMachineRefreshBridge();
        _httpClient.Dispose();
    }

    private async void MachinesTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsLoaded || WindowState == WindowState.Minimized)
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
            else if (StatsTabControl.SelectedItem == HardwareTabItem)
            {
                await RefreshAllHardwareAsync();
            }
            else if (StatsTabControl.SelectedItem == PcAnalyticsTabItem)
            {
                await RefreshPcAnalyticsAsync();
            }
            else if (StatsTabControl.SelectedItem == TaxReportTabItem)
            {
                await RefreshTaxReportAsync();
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
                await LoadWebsiteLogSettingsAsync();
                await RefreshWebsiteLogsAsync();
            }
        }
    }

    private async Task RefreshAllDataAsync()
    {
        await RefreshMachinesAsync();
        await RefreshMembersAsync(forceRefresh: true);
        await RefreshSystemLogsAsync();
        await RefreshTransactionLogsAsync();

        if (_settings.IsLiteMode)
        {
            // Trong Bản Lite: Dừng hoàn toàn việc tải ngầm 9 tab phụ để app cực kỳ nhẹ và phản hồi tức thì!
            TrimProcessMemory();
            return;
        }

        await RefreshGroupsAsync(forceReloadPricing: true);
        await RefreshServiceItemsAsync();
        await LoadLoyaltySettingsAsync();
        await LoadClientRuntimeSettingsAsync();
        await LoadGuestLoginSettingsAsync();
        await LoadTopupPromoSettingsAsync();
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

    private async void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await ReloadAllSystemDataAsync(isUserInitiated: true);
        }
    }

    public bool IsRealtimeSocketConnected => _billingSocket?.Connected == true;

    private async void ReloadAllData_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAllSystemDataAsync(isUserInitiated: true);
    }

    private void CheckSystemAndDatabaseHealth_Click(object sender, RoutedEventArgs e)
    {
        var diagWindow = new SystemHealthCheckWindow(this, autoStartRepair: false)
        {
            Owner = this
        };
        diagWindow.ShowDialog();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new Server.Admin.App.Windows.AboutWindow
        {
            Owner = this
        };
        aboutWindow.ShowDialog();
    }

    private void QuickAutoRepair_Click(object sender, RoutedEventArgs e)
    {
        var diagWindow = new SystemHealthCheckWindow(this, autoStartRepair: true)
        {
            Owner = this
        };
        diagWindow.ShowDialog();
    }

    private bool _isReloadingAllData;

    public async Task ReloadAllSystemDataAsync(bool isUserInitiated = true)
    {
        if (_isReloadingAllData) return;
        _isReloadingAllData = true;

        var originalText = ReloadAllDataButtonText?.Text ?? "Tải lại (F5)";
        if (ReloadAllDataButtonText != null) ReloadAllDataButtonText.Text = "Đang tải...";
        if (ReloadAllDataButton != null) ReloadAllDataButton.IsEnabled = false;

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] 🔄 Đang tải lại toàn bộ dữ liệu và kết nối lại máy chủ...");

        try
        {
            // 1. Kiểm tra lại kết nối Backend
            await CheckBackendHealthAsync();

            // 2. Kết nối lại Realtime Socket
            await ConnectRealtimeMachineRefreshAsync();

            // 3. Tải lại toàn bộ dữ liệu nghiệp vụ
            await RefreshAllDataAsync();

            // 4. Đánh dấu đã khởi tạo các cài đặt
            _loyaltySettingsInitialized = true;
            _readyShutdownSettingsInitialized = true;
            _webFilterSettingsInitialized = true;
            _websiteLogSettingsInitialized = true;
            _websiteLogFiltersInitialized = true;
            _loyaltySpinSettingsInitialized = true;
            _backupSettingsInitialized = true;

            // 5. Tải thêm người dùng server, storage stats, yêu cầu rút/nạp tiền
            _ = LoadServerUsersAsync();
            _ = LoadDatabaseStorageStatsAsync();
            _ = LoadPendingMemberWithdrawRequestsAsync();
            _ = LoadPendingMemberTopupRequestsAsync();

            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] ✅ Tải lại toàn bộ dữ liệu từ server thành công.");

            if (isUserInitiated && HealthTextBlock != null && (HealthTextBlock.Text.Contains(I18n.BackendOffline) || HealthTextBlock.Text.Contains("offline", StringComparison.OrdinalIgnoreCase)))
            {
                ConnectionErrorDialog.Show(
                    this,
                    "Chưa kết nối được máy chủ",
                    new Exception("Hiện vẫn chưa kết nối được tới Backend / Database (localhost:39090)."),
                    async () => await ReloadAllSystemDataAsync(isUserInitiated: true));
            }
        }
        catch (Exception ex)
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] ❌ Tải lại dữ liệu lỗi: {ex.Message}");
            if (isUserInitiated)
            {
                ConnectionErrorDialog.Show(
                    this,
                    "Tải lại dữ liệu thất bại",
                    ex,
                    async () => await ReloadAllSystemDataAsync(isUserInitiated: true));
            }
        }
        finally
        {
            if (ReloadAllDataButtonText != null) ReloadAllDataButtonText.Text = originalText;
            if (ReloadAllDataButton != null) ReloadAllDataButton.IsEnabled = true;
            _isReloadingAllData = false;
        }
    }

    private bool IsMembersTabActive()
    {
        return IsLoaded && MainTabControl.SelectedIndex == MembersTabIndex;
    }

    private bool IsSystemLogsTabActive()
    {
        return IsLoaded && MainTabControl.SelectedItem == LogsTabItem && LogsTabControl.SelectedItem == SystemLogsTabItem;
    }

    private bool IsTransactionLogsTabActive()
    {
        return IsLoaded && MainTabControl.SelectedItem == LogsTabItem && LogsTabControl.SelectedItem == TransactionLogsTabItem;
    }

    private bool IsWebsiteLogsTabActive()
    {
        return IsLoaded && MainTabControl.SelectedItem == LogsTabItem && LogsTabControl.SelectedItem == WebsiteLogsTabItem;
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

    private void UpdateLoginStatusUi()
    {
        if (AdminSession.IsLoggedIn)
        {
            if (LogoutIconTextBlock != null) LogoutIconTextBlock.Text = "🚪";
            if (LogoutTextLabel != null)
            {
                var name = !string.IsNullOrWhiteSpace(AdminSession.CurrentUserFullName)
                    ? AdminSession.CurrentUserFullName
                    : (!string.IsNullOrWhiteSpace(AdminSession.CurrentUsername) ? AdminSession.CurrentUsername : "Admin");
                LogoutTextLabel.Text = $"{name} (Đăng xuất)";
            }
            if (LogoutButton != null)
            {
                LogoutButton.ToolTip = $"Đang đăng nhập: {AdminSession.CurrentUserFullName} ({AdminSession.CurrentUserRole}). Nhấn để đăng xuất.";
            }
        }
        else
        {
            if (LogoutIconTextBlock != null) LogoutIconTextBlock.Text = "🔑";
            if (LogoutTextLabel != null) LogoutTextLabel.Text = "Đăng nhập";
            if (LogoutButton != null)
            {
                LogoutButton.ToolTip = "Đăng nhập tài khoản quản trị để mở Bản Pro";
            }
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AdminSession.IsLoggedIn)
        {
            // Mở popup đăng nhập
            var loginWindow = new LoginWindow { Owner = this };
            loginWindow.ShowDialog();
            if (AdminSession.IsLoggedIn)
            {
                HandleSuccessfulLogin();
            }
            return;
        }

        // Đang đăng nhập -> Xác nhận đăng xuất
        var displayName = !string.IsNullOrWhiteSpace(AdminSession.CurrentUserFullName)
            ? AdminSession.CurrentUserFullName
            : (!string.IsNullOrWhiteSpace(AdminSession.CurrentUsername) ? AdminSession.CurrentUsername : "Admin");

        var result = MessageBox.Show(
            $"Bạn đang đăng nhập: {displayName} ({AdminSession.CurrentUserRole}).\nBạn có chắc chắn muốn đăng xuất không?",
            "Xác nhận đăng xuất",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            // Clear session info
            AdminSession.Clear();

            // Khi chưa đăng nhập, tự động chuyển về Bản Lite
            _settings.IsLiteMode = true;
            SaveSettings();

            UpdateLoginStatusUi();
            UpdateAppModeUi();

            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] 🚪 Đã đăng xuất. Ứng dụng đã chuyển về Bản Lite.");
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

    private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var hit = e.OriginalSource as DependencyObject;
        if (hit == null) return;

        // If clicking inside the member detail sidebar, do NOT clear selection or auto close
        if (MemberDetailSidebarBorder != null && IsVisualChildOf(hit, MemberDetailSidebarBorder))
        {
            return;
        }

        var dataGrid = FindVisualParent<DataGrid>(hit);
        if (dataGrid != null)
        {
            // If clicking inside a DataGrid, check if it hit a row
            var row = FindVisualParent<DataGridRow>(hit);
            if (row == null)
            {
                // Clicking blank area of the DataGrid -> Unselect it (except MembersDataGrid)
                // (MembersDataGrid selection drives the member detail sidebar which must not auto-close)
                if (dataGrid != MembersDataGrid)
                {
                    dataGrid.SelectedItem = null;
                }
            }
        }
        else
        {
            // Clicking completely outside DataGrid
            // Check if we are clicking on action controls. If so, preserve selection so actions can proceed.
            if (!IsActionControl(hit))
            {
                if (MachinesDataGrid != null) MachinesDataGrid.SelectedItem = null;
                // Note: Do NOT clear MembersDataGrid.SelectedItem so the member sidebar does not auto-close
                if (ServiceItemsDataGrid != null) ServiceItemsDataGrid.SelectedItem = null;
            }
        }
    }

    private static bool IsVisualChildOf(DependencyObject? child, DependencyObject parent)
    {
        while (child != null)
        {
            if (ReferenceEquals(child, parent)) return true;
            var visualParent = (child is Visual || child is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(child) : null;
            child = visualParent ?? LogicalTreeHelper.GetParent(child);
        }
        return false;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }

    private static bool IsActionControl(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is Button || 
                current is MenuItem || 
                current is ComboBox || 
                current is TextBox || 
                current is CheckBox || 
                current is TabItem || 
                current is ContextMenu ||
                current is ScrollBar ||
                current is DatePicker ||
                current is System.Windows.Controls.Calendar ||
                current is System.Windows.Controls.Primitives.ToggleButton)
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }



    private void ContextViewAppUsageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedItem = MachinesDataGrid.SelectedItem;
        if (selectedItem != null)
        {
            // The item is a dynamic type or PcDto, let's use dynamic
            dynamic pc = selectedItem;
            string pcId = pc.Id;
            string pcName = pc.Name;

            var window = new AppUsageWindow(this, pcId, pcName)
            {
                Owner = this
            };
            window.ShowDialog();
        }
    }

    public AdminChatWindow OpenOrActivateAdminChatWindow(string pcId, string pcName, bool isUserAction = true)
    {
        if (string.IsNullOrWhiteSpace(pcId)) return null!;

        // 1. Kiểm tra xem đã có cửa sổ AdminChatWindow nào đang mở cho máy này chưa
        foreach (Window w in Application.Current.Windows)
        {
            if (w is AdminChatWindow existing && string.Equals(existing.PcId, pcId, StringComparison.OrdinalIgnoreCase))
            {
                if (existing.WindowState == WindowState.Minimized)
                {
                    existing.WindowState = WindowState.Normal;
                }
                if (isUserAction)
                {
                    existing.Activate();
                    existing.Focus();
                }
                _ = existing.ReloadMessagesAsync();
                return existing;
            }
        }

        // 2. Chưa có thì mới tạo 1 cửa sổ mới duy nhất
        var window = new AdminChatWindow(this, pcId, pcName)
        {
            Owner = this
        };
        window.Show();
        if (isUserAction)
        {
            window.Activate();
            window.Focus();
        }
        return window;
    }

    private void ContextChatMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedItem = MachinesDataGrid.SelectedItem;
        if (selectedItem != null)
        {
            dynamic pc = selectedItem;
            string pcId = pc.Id;
            string pcName = pc.Name;

            OpenOrActivateAdminChatWindow(pcId, pcName, isUserAction: true);
        }
    }

    internal string? _lastChatNotificationPcId;
    internal string? _lastChatNotificationPcName;

    private void ChatNotificationPopupBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        ChatNotificationPopupBorder.Visibility = Visibility.Collapsed;
        
        if (!string.IsNullOrEmpty(_lastChatNotificationPcId))
        {
            OpenOrActivateAdminChatWindow(_lastChatNotificationPcId, _lastChatNotificationPcName ?? "PC", isUserAction: true);
        }
    }
}

