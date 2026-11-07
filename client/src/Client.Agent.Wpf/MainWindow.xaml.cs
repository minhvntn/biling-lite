using System.Windows.Input;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Client.Agent.Wpf.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Client.Agent.Wpf.Models;
using Client.Agent.Wpf.Services;

namespace Client.Agent.Wpf;

public partial class MainWindow : Window
{
    public string? CurrentMemberId { get; set; }
public static decimal PricingStep { get; set; } = 1000m;
    public static decimal MinimumCharge { get; set; } = 1000m;
    private const int DefaultTotalSessionMinutes = 60_000; // 1000 giờ
    private const int VipOrPostpaidTotalSessionMinutes = 6_000; // 100 giờ

    public event EventHandler? TimeExpired;

    private const decimal DefaultHourlyRate = 12_000m;

    private readonly DispatcherTimer _usageTimer = new();

    private bool _allowClose;
    private int _totalSessionMinutes = DefaultTotalSessionMinutes;
    private decimal _hourlyRate = DefaultHourlyRate;
    private decimal _serviceCost;
    private int _serviceOrderCount;
    private int _phaseStartedElapsedSeconds;
    private const string DefaultGameLauncherExecutableName = "GameLauncher.Client.exe";

    private InGameOverlayWindow? _overlayWindow;
    private const int HOTKEY_ID = 9000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);
    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDWININICHANGE = 0x02;

    private int _lastSyncElapsedSeconds;
    private decimal _lastSyncMemberBalance;
    private bool _isMemberSession;
    private bool _isVipSession;
    private bool _isAdminSession;
    private bool _isPostpaidSession;
    private decimal _memberBalance;
    private int _playSeconds;
    private bool _usePromoPlayTime = true;
    private bool IsUsingPromoPlayTimeNow => _isMemberSession && !_isVipSession && _usePromoPlayTime && _playSeconds > 0;
    private bool _withdrawActionEnabledSetting = true;
    private bool _topupActionEnabledSetting = true;
    private TimeSpan _usedDuration = TimeSpan.Zero;
    private DateTime? _runningStartedAtUtc;
    private decimal? _serverEstimatedCost;

    private readonly WarpVpnService _warpVpnService;
    private readonly VpnSettings _vpnSettings = new();
    private readonly DispatcherTimer _vpnStatusTimer;

    public MainWindow()
    {
        InitializeComponent();
        ApplyI18nTexts();
        SetLogoutActionVisible(false);

        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ServerManagerBilling", "logs");
        _warpVpnService = new WarpVpnService(new FileLogger(Path.Combine(logDir, "client-agent.log")));
        _warpVpnService.StatusChanged += OnVpnStatusChanged;

        _vpnStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _vpnStatusTimer.Tick += async (s, e) =>
        {
            if (_warpVpnService.CurrentStatus != VpnStatus.Installing && _warpVpnService.CurrentStatus != VpnStatus.Connecting)
            {
                await _warpVpnService.RefreshStatusAsync();
            }
        };
        _vpnStatusTimer.Start();
        _ = _warpVpnService.RefreshStatusAsync();

        _usageTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _usageTimer.Tick += UsageTimer_Tick;
        _usageTimer.Start();

        UpdateUsageUi();

        Loaded += MainWindow_Loaded;
        LocationChanged += MainWindow_LocationChanged;
        StateChanged += MainWindow_StateChanged;

        PreviewMouseMove += (s, e) => ResetClientActivity();
        PreviewMouseDown += (s, e) => ResetClientActivity();
        PreviewKeyDown += (s, e) => ResetClientActivity();
    }

    private void ApplyI18nTexts()
    {
        ConnectionStatusTextBlock.Text = ClientI18n.Get(
            "main.connection.connecting",
            "\u0110ang k\u1ebft n\u1ed1i...");

        StatusIconTextBlock.Text = ClientI18n.Get("main.status.icon", "⚡");
        StatusTitleTextBlock.Text = ClientI18n.Get("main.status.title", "Trạng thái máy");

        UsedTimeLabelTextBlock.Text = ClientI18n.Get("main.metrics.used", "Đã dùng") + ":";
        GameCostLabelTextBlock.Text = ClientI18n.Get("main.metrics.game_cost", "Tiền giờ chơi");
        ServiceCostLabelTextBlock.Text = ClientI18n.Get("main.metrics.service_cost", "Tiền dịch vụ");

        LastCommandTextBlock.Text = ClientI18n.Get(
            "main.last_command.initial",
            "Lệnh gần nhất: Khởi động hệ thống");

        MessagesIconTextBlock.Text = ClientI18n.Get("main.actions.messages.icon", "💬");
        MessagesLabelTextBlock.Text = ClientI18n.Get("main.actions.messages.label", "Tin nhắn");
        ServicesIconTextBlock.Text = ClientI18n.Get("main.actions.services.icon", "🛒");
        ServicesLabelTextBlock.Text = ClientI18n.Get("main.actions.services.label", "Dịch vụ");
        LoyaltyIconTextBlock.Text = ClientI18n.Get("main.actions.loyalty.icon", "🎡");
        LoyaltyLabelTextBlock.Text = ClientI18n.Get("main.actions.loyalty.label", "\u0110\u1ed5i \u0111i\u1ec3m");
        TransferIconTextBlock.Text = ClientI18n.Get("main.actions.transfer.icon", "💸");
        TransferLabelTextBlock.Text = ClientI18n.Get("main.actions.transfer.label", "Chuy\u1ec3n ti\u1ec1n");
        WithdrawIconTextBlock.Text = ClientI18n.Get("main.actions.withdraw.icon", "💵");
        WithdrawLabelTextBlock.Text = ClientI18n.Get("main.actions.withdraw.label", "R\u00fat ti\u1ec1n");
        TopupRequestIconTextBlock.Text = ClientI18n.Get("main.actions.topup_request.icon", "💳");
        TopupRequestLabelTextBlock.Text = ClientI18n.Get("main.actions.topup_request.label", "N\u1ea1p ti\u1ec1n");
        PasswordIconTextBlock.Text = ClientI18n.Get("main.actions.password.icon", "🔑");
        PasswordLabelTextBlock.Text = ClientI18n.Get("main.actions.password.label", "\u0110\u1ed5i m\u1eadt m\u00e3");
        LogoutIconTextBlock.Text = ClientI18n.Get("main.actions.logout.icon", "🚪");
        LogoutLabelTextBlock.Text = ClientI18n.Get("main.actions.logout.label", "\u0110\u0103ng xu\u1ea5t");
        LockIconTextBlock.Text = ClientI18n.Get("main.actions.lock.icon", "🔒");
        LockLabelTextBlock.Text = ClientI18n.Get("main.actions.lock.label", "Kh\u00f3a m\u00e1y");

        FooterTitleTextBlock.Text = ClientI18n.Get("main.footer.title", "Hội viên thân thiết");
        FooterDescriptionTextBlock.Text = ClientI18n.Get(
            "main.footer.description",
            "T\u00edch l\u0169y \u0111i\u1ec3m khi ch\u01a1i \u0111\u1ec3 \u0111\u1ed5i gi\u1edd ho\u1eb7c quay th\u01b0\u1edfng h\u1ea5p d\u1eabn!");
        FooterIconTextBlock.Text = ClientI18n.Get("main.footer.icon", "💎");
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SnapToTopRight();
    }

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        SnapToTopRight();
    }

    private void SnapToTopRight()
    {
        // If window is minimized, do not snap it
        if (WindowState == WindowState.Minimized)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var targetLeft = workArea.Right - Width;
        var targetTop = workArea.Top;

        // Prevent infinite event loop
        if (Math.Abs(Left - targetLeft) > 1 || Math.Abs(Top - targetTop) > 1)
        {
            Left = targetLeft;
            Top = targetTop;
        }
    }

    private int _autoCollapseIntervalSeconds = 0;
    private DateTime _lastClientInteractionTime = DateTime.UtcNow;
    private bool _isCollapsed = false;
    private bool _hasTopupPromotion = false;
    private string? _currentUsername;
    private string? _currentRank;
    private string? _currentMemberType;
    private string? _currentAvatarId;
    private string? _currentComboExpiresAt;


    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = !_isCollapsed;
        var visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        
        ActionButtonsGrid.Visibility = visibility;
        PromotionsContainer.Visibility = _isAdminSession ? Visibility.Collapsed : visibility;
        FooterContainer.Visibility = _isAdminSession ? Visibility.Collapsed : visibility;
        
        if (_hasTopupPromotion && !_isAdminSession)
        {
            TopupPromotionBannerBorder.Visibility = visibility;
        }
        else
        {
            TopupPromotionBannerBorder.Visibility = Visibility.Collapsed;
        }
        
        if (_isCollapsed)
        {
            if (MoreActionsOverlay is not null) MoreActionsOverlay.Visibility = Visibility.Collapsed;
            SuspendAllAnimations();
            CollapseButtonText.Text = "Mở rộng";
            CollapseButtonIcon.Text = "\uE76B"; // ChevronRight
            LeftColumnGrid.Margin = new Thickness(0);
            LeftColumnGrid.HorizontalAlignment = HorizontalAlignment.Center;
            LeftColumnGrid.SetValue(System.Windows.Controls.Grid.ColumnSpanProperty, 2);
            
            // Hide rank banner when collapsed
            MemberRankContainer.Visibility = Visibility.Collapsed;

            if (RightColumnRankPanel.Children.Contains(MemberRankContainer))
            {
                RightColumnRankPanel.Children.Remove(MemberRankContainer);
                LeftColumnRankPanel.Children.Add(MemberRankContainer);
                MemberRankContainer.Width = 280;
            }
            if (HeaderGrid.Children.Contains(UserInfoPanel))
            {
                HeaderGrid.Children.Remove(UserInfoPanel);
                LeftColumnRankPanel.Children.Insert(0, UserInfoPanel);
                UserInfoPanel.Margin = new Thickness(0, 0, 0, 15);
                UserInfoPanel.HorizontalAlignment = HorizontalAlignment.Center;
            }
        }
        else
        {
            CollapseButtonText.Text = "Thu gọn";
            CollapseButtonIcon.Text = "\uE76C"; // ChevronLeft
            LeftColumnGrid.Margin = new Thickness(0, 0, 10, 0);
            LeftColumnGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            LeftColumnGrid.SetValue(System.Windows.Controls.Grid.ColumnSpanProperty, 1);
            
            // Show rank banner when expanded (only if it is a member session)
            MemberRankContainer.Visibility = _isMemberSession ? Visibility.Visible : Visibility.Collapsed;

            if (LeftColumnRankPanel.Children.Contains(MemberRankContainer))
            {
                LeftColumnRankPanel.Children.Remove(MemberRankContainer);
                RightColumnRankPanel.Children.Add(MemberRankContainer);
                MemberRankContainer.Width = double.NaN;
            }
            if (LeftColumnRankPanel.Children.Contains(UserInfoPanel))
            {
                LeftColumnRankPanel.Children.Remove(UserInfoPanel);
                HeaderGrid.Children.Add(UserInfoPanel);
                System.Windows.Controls.Grid.SetColumn(UserInfoPanel, 1);
                UserInfoPanel.Margin = new Thickness(0, 0, 20, 0);
                UserInfoPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }
        
        // Reapply member info to trigger animation suspension or resumption
        SetMemberInfo(_currentUsername, _currentRank, _currentMemberType, _currentAvatarId, _currentComboExpiresAt);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            SuspendAllAnimations();
        }
        else if (!_isCollapsed)
        {
            SetMemberInfo(_currentUsername, _currentRank, _currentMemberType, _currentAvatarId, _currentComboExpiresAt);
        }
    }

    public void SuspendAllAnimations()
    {
        // 1. Dừng triệt để toàn bộ animation của Sparkles (hạt lấp lánh)
        if (SparklesContainer != null)
        {
            SparklesContainer.Visibility = Visibility.Collapsed;
            Sparkle1?.BeginAnimation(UIElement.OpacityProperty, null);
            Sparkle2?.BeginAnimation(UIElement.OpacityProperty, null);
            Sparkle3?.BeginAnimation(UIElement.OpacityProperty, null);
            Sparkle4?.BeginAnimation(UIElement.OpacityProperty, null);
            Sparkle5?.BeginAnimation(UIElement.OpacityProperty, null);
            Sparkle6?.BeginAnimation(UIElement.OpacityProperty, null);
            Sparkle7?.BeginAnimation(UIElement.OpacityProperty, null);
            Sparkle8?.BeginAnimation(UIElement.OpacityProperty, null);

            Sparkle1Transform?.BeginAnimation(TranslateTransform.YProperty, null);
            Sparkle2Transform?.BeginAnimation(TranslateTransform.YProperty, null);
            Sparkle3Transform?.BeginAnimation(TranslateTransform.YProperty, null);
            Sparkle4Transform?.BeginAnimation(TranslateTransform.YProperty, null);
            Sparkle5Transform?.BeginAnimation(TranslateTransform.YProperty, null);
            Sparkle6Transform?.BeginAnimation(TranslateTransform.YProperty, null);
            Sparkle7Transform?.BeginAnimation(TranslateTransform.YProperty, null);
            Sparkle8Transform?.BeginAnimation(TranslateTransform.YProperty, null);
        }

        // 2. Dừng glow border & glow effect
        if (MemberRankGlowBorder != null)
        {
            MemberRankGlowBorder.Opacity = 0;
        }
        if (MemberRankGlowEffect != null)
        {
            MemberRankGlowEffect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            MemberRankGlowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        }

        // 3. Dừng animation sóng gradient trên MemberRankBorder
        if (MemberRankBorder?.Background is LinearGradientBrush lgb)
        {
            foreach (var stop in lgb.GradientStops)
            {
                stop.BeginAnimation(GradientStop.OffsetProperty, null);
            }
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        
        var workArea = SystemParameters.WorkArea;
        // Add 20px margin from top and right edges
        this.Left = workArea.Right - this.ActualWidth - 20;
        this.Top = workArea.Top + 20;
    }

    private bool _isExternalInfoVisible = true;
    public void SetExternalInfoVisibility(bool visible)
    {
        _isExternalInfoVisible = visible;
        UpdateExternalInfoVisibility();
    }

    private void UpdateExternalInfoVisibility()
    {
        ExternalInfoPanel.Visibility = (_isExternalInfoVisible || _isVipSession) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    public void ConfigureBilling(int totalSessionMinutes, decimal hourlyRate, bool resetUsage = false, decimal? memberBalance = null, bool isPostpaid = false, int playSeconds = 0, bool usePromoPlayTime = true)
    {
        var elapsedSeconds = Math.Max(0, (int)GetCurrentUsedDuration().TotalSeconds);

        if (resetUsage)
        {
            _usedDuration = TimeSpan.Zero;
            _runningStartedAtUtc = null;
            _serviceCost = 0;
            _serviceOrderCount = 0;
            _phaseStartedElapsedSeconds = 0;
            _serverEstimatedCost = null;
            UpdateServiceBadgeUi();
        }
        else if (_isVipSession && memberBalance.HasValue && memberBalance.Value > _memberBalance + 1000m)
        {
            _phaseStartedElapsedSeconds = elapsedSeconds;
        }

        _lastSyncElapsedSeconds = elapsedSeconds;

        if (memberBalance.HasValue)
        {
            _lastSyncMemberBalance = memberBalance.Value;
            _memberBalance = memberBalance.Value;
            _playSeconds = playSeconds;
        }

        _totalSessionMinutes = Math.Max(1, totalSessionMinutes);
        _hourlyRate = hourlyRate < 0 ? 0 : hourlyRate;

        _usageTimer.Interval = TimeSpan.FromSeconds(1);

        _isPostpaidSession = isPostpaid;
        SetExternalInfoVisibility(isPostpaid || _isVipSession);

        UpdatePromoPlayTimeUi(playSeconds, usePromoPlayTime);
        UpdateUsageUi();
    }

    public void UpdatePromoPlayTimeUi(int playSeconds, bool usePromoPlayTime)
    {
        _playSeconds = Math.Max(0, playSeconds);
        _usePromoPlayTime = usePromoPlayTime;

        if (_isMemberSession)
        {
            PromoPlayTimeCard.Visibility = Visibility.Visible;
            var hours = _playSeconds / 3600;
            var mins = (_playSeconds % 3600) / 60;
            PromoPlayTimeText.Text = hours > 0 ? $"{hours} giờ {mins:D2} phút" : $"{mins} phút";

            if (TogglePromoTimeButton.IsEnabled)
            {
                TogglePromoTimeBadge.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TogglePromoTimeBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                TogglePromoTimeText.Text = "🎁 Đổi giờ KM";
            }
        }
        else
        {
            PromoPlayTimeCard.Visibility = Visibility.Collapsed;
        }
    }

    public void SynchronizeUsedDuration(int elapsedSeconds)
    {
        if (elapsedSeconds >= 0)
        {
            _usedDuration = TimeSpan.FromSeconds(elapsedSeconds);
            if (_runningStartedAtUtc is not null)
            {
                _runningStartedAtUtc = DateTime.UtcNow;
            }
            UpdateUsageUi();
        }
    }

    public void SynchronizeSessionBilling(int elapsedSeconds, decimal? estimatedCost)
    {
        if (elapsedSeconds >= 0)
        {
            _usedDuration = TimeSpan.FromSeconds(elapsedSeconds);
            if (_runningStartedAtUtc is not null)
            {
                _runningStartedAtUtc = DateTime.UtcNow;
            }
        }
        if (estimatedCost.HasValue && estimatedCost.Value >= 0)
        {
            _serverEstimatedCost = estimatedCost.Value;
        }
        UpdateUsageUi();
    }

    public void SetUpfrontUsedDuration()
    {
        _usedDuration = TimeSpan.Zero;
        _runningStartedAtUtc = DateTime.UtcNow;
        UpdateUsageUi();
    }

    public void UpdateHourlyRate(decimal hourlyRate)
    {
        _hourlyRate = hourlyRate < 0 ? 0 : hourlyRate;
        UpdateUsageUi();
    }

    public void SetServiceCost(decimal amount)
    {
        _serviceCost = amount < 0 ? 0 : amount;
        UpdateUsageUi();
    }

    public void SetServiceOrderCount(int count)
    {
        _serviceOrderCount = Math.Max(0, count);
        UpdateServiceBadgeUi();
    }

    public void SetAgentId(string agentId)
    {
        AgentIdTextBlock.Text = string.IsNullOrWhiteSpace(agentId) ? Environment.MachineName : agentId;
    }

    public void SetMachineGroup(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            MachineGroupBadgeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var trimmed = groupName.Trim();
        MachineGroupBadgeBorder.Visibility = Visibility.Visible;
        MachineGroupNameTextBlock.Text = trimmed;

        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("vip") || lower.Contains("pro"))
        {
            // Gold / Luxury Amber
            MachineGroupBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
            MachineGroupBadgeBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
            MachineGroupBadgeBorder.BorderThickness = new Thickness(1);
            MachineGroupIconTextBlock.Text = "👑";
            MachineGroupNameTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B45309"));
        }
        else if (lower.Contains("thi đấu") || lower.Contains("arena") || lower.Contains("esport"))
        {
            // Fiery Red / Esports
            MachineGroupBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));
            MachineGroupBadgeBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
            MachineGroupBadgeBorder.BorderThickness = new Thickness(1);
            MachineGroupIconTextBlock.Text = "⚡";
            MachineGroupNameTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
        }
        else
        {
            // Clean Blue / Normal / Default
            MachineGroupBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
            MachineGroupBadgeBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BFDBFE"));
            MachineGroupBadgeBorder.BorderThickness = new Thickness(1);
            MachineGroupIconTextBlock.Text = "🖥️";
            MachineGroupNameTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8"));
        }
    }

    public void SetConnectionStatus(string status)
    {
        var normalized = status?.Trim() ?? string.Empty;

        if (normalized.StartsWith("Connected", StringComparison.OrdinalIgnoreCase))
        {
            ConnectionStatusTextBlock.Text = "\u0110\u00e3 k\u1ebft n\u1ed1i";
            ConnectionIndicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
            return;
        }

        if (normalized.StartsWith("Reconnecting", StringComparison.OrdinalIgnoreCase))
        {
            ConnectionStatusTextBlock.Text = normalized;
            ConnectionIndicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            return;
        }

        ConnectionStatusTextBlock.Text = "M\u1ea5t k\u1ebft n\u1ed1i";
        ConnectionIndicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
    }

    public void SetMachineState(string state)
    {
        var code = (state ?? string.Empty).Trim().ToUpperInvariant();
        var now = DateTime.Now.ToString("HH:mm:ss");

        if (code == "LOCKED")
        {
            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                _overlayWindow.Hide();
            }
            if (_vpnSettings.AutoDisconnectOnSessionEnd && _warpVpnService != null)
            {
                _ = _warpVpnService.DisconnectAsync();
            }
        }

        switch (code)
        {
            case "IN_USE":
                ResumeSession();
                MachineStateTextBlock.Text = "\u0110ang s\u1eed d\u1ee5ng";
                MachineStateTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                LastCommandTextBlock.Text = $"L\u1ec7nh g\u1ea7n nh\u1ea5t: M\u1edf m\u00e1y ({now})";
                break;

            case "PAUSED":
                PauseSession();
                MachineStateTextBlock.Text = "T\u1ea1m ngh\u1ec9";
                MachineStateTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
                LastCommandTextBlock.Text = $"L\u1ec7nh g\u1ea7n nh\u1ea5t: T\u1ea1m ngh\u1ec9 ({now})";
                break;

            case "ONLINE":
                EndSession(resetUsage: true);
                MachineStateTextBlock.Text = "S\u1eb5n s\u00e0ng";
                MachineStateTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8"));
                LastCommandTextBlock.Text = $"L\u1ec7nh g\u1ea7n nh\u1ea5t: S\u1eb5n s\u00e0ng ({now})";
                break;

            case "LOCKED":
                EndSession(resetUsage: true);
                MachineStateTextBlock.Text = "\u0110\u00e3 kh\u00f3a";
                MachineStateTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                LastCommandTextBlock.Text = $"L\u1ec7nh g\u1ea7n nh\u1ea5t: Kh\u00f3a m\u00e1y ({now})";
                break;

            default:
                EndSession(resetUsage: false);
                MachineStateTextBlock.Text = "Ngo\u1ea1i tuy\u1ebfn";
                MachineStateTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                LastCommandTextBlock.Text = $"L\u1ec7nh g\u1ea7n nh\u1ea5t: {code} ({now})";
                break;
        }

        UpdateUsageUi();
    }

    public void SetLastCommand(string command)
    {
        var value = string.IsNullOrWhiteSpace(command) ? "-" : command;
        LastCommandTextBlock.Text = $"Lệnh gần nhất: {value}";
    }

    public void SetLoyaltyProgress(int points, double progressMinutes, double minutesPerPoint)
    {
        var progressSecs = (int)Math.Floor(progressMinutes * 60.0);
        var progressStr = $"{progressSecs / 60:00}:{progressSecs % 60:00}";

        var totalSecs = (int)Math.Floor(minutesPerPoint * 60.0);
        var totalStr = totalSecs % 60 == 0 
            ? $"{totalSecs / 60} phút" 
            : $"{totalSecs / 60} phút {totalSecs % 60} giây";

        MemberPointsTextBlock.Text = $"Điểm: {points:N0} | {progressStr} / {totalStr}";
        MemberPointsTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8"));

        LoyaltyProgressContainer.Visibility = Visibility.Visible;
        FooterTitleTextBlock.Text = ClientI18n.Get("main.footer.title", "Hội viên thân thiết");

        if (minutesPerPoint > 0)
        {
            double pct = Math.Clamp(progressMinutes / minutesPerPoint, 0.0, 1.0);
            LoyaltyProgressFilled.Width = new GridLength(pct, GridUnitType.Star);
            LoyaltyProgressEmpty.Width = new GridLength(1.0 - pct, GridUnitType.Star);
        }
    }

    public void SetMemberInfo(string? username, string? rank, string? memberType = null, string? avatarId = null, string? comboExpiresAt = null)
    {
        _currentUsername = username?.ToUpperInvariant();
        _currentRank = rank;
        _currentMemberType = memberType;
        _currentAvatarId = avatarId;
        _currentComboExpiresAt = comboExpiresAt;

        _isAdminSession = string.Equals(username, "Admin", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(username, "Administrator", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(rank, "ADMIN", StringComparison.OrdinalIgnoreCase);
        _isMemberSession = !string.IsNullOrWhiteSpace(username) && !_isAdminSession;
        _isVipSession = !_isAdminSession && (string.Equals(memberType, "VIP", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rank, "VIP", StringComparison.OrdinalIgnoreCase));
        UpdateExternalInfoVisibility();

        if (_isAdminSession)
        {
            MessagesActionButton.Visibility = Visibility.Collapsed;
            ServicesActionButton.Visibility = Visibility.Collapsed;
            TopupRequestActionButton.Visibility = Visibility.Collapsed;
            GameLauncherActionButton.Visibility = Visibility.Collapsed;
            ComboPackagesActionButton.Visibility = Visibility.Collapsed;
            LoyaltyActionButton.Visibility = Visibility.Collapsed;
            LockActionButton.Visibility = Visibility.Collapsed;
            if (TransferActionButton != null) TransferActionButton.Visibility = Visibility.Collapsed;
            if (WithdrawActionButton != null) WithdrawActionButton.Visibility = Visibility.Collapsed;
            if (PasswordActionButton != null) PasswordActionButton.Visibility = Visibility.Collapsed;
            if (ChangeWallpaperButton != null) ChangeWallpaperButton.Visibility = Visibility.Collapsed;
            if (MoreActionButton != null) MoreActionButton.Visibility = Visibility.Collapsed;
            if (MoreActionsOverlay != null) MoreActionsOverlay.Visibility = Visibility.Collapsed;

            LogoutActionButton.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;

            FooterContainer.Visibility = Visibility.Collapsed;
            PromotionsContainer.Visibility = Visibility.Collapsed;
            TopupPromotionBannerBorder.Visibility = Visibility.Collapsed;
            MemberRankContainer.Visibility = Visibility.Collapsed;

            UpdateActionButtonsLayout();
        }
        else if (string.Equals(_currentMemberType, "COMBO", StringComparison.OrdinalIgnoreCase))
        {
            MessagesActionButton.Visibility = Visibility.Collapsed;
            ServicesActionButton.Visibility = Visibility.Collapsed;
            LoyaltyActionButton.Visibility = Visibility.Collapsed;
            TransferActionButton.Visibility = Visibility.Collapsed;
            WithdrawActionButton.Visibility = Visibility.Collapsed;
            TopupRequestActionButton.Visibility = Visibility.Collapsed;
            PasswordActionButton.Visibility = Visibility.Collapsed;
            if (MoreActionButton is not null) MoreActionButton.Visibility = Visibility.Collapsed;
            if (MoreActionsOverlay is not null) MoreActionsOverlay.Visibility = Visibility.Collapsed;
            FooterContainer.Visibility = Visibility.Collapsed;
            PromotionsContainer.Visibility = Visibility.Collapsed;
            MemberRankContainer.Visibility = Visibility.Collapsed;

            GameLauncherActionButton.Visibility = Visibility.Visible;
            ComboPackagesActionButton.Visibility = Visibility.Visible;
            LockActionButton.Visibility = Visibility.Visible;
            SetLogoutActionVisible(false);
            UpdateActionButtonsLayout();
        }
        else
        {
            MessagesActionButton.Visibility = Visibility.Visible;
            ServicesActionButton.Visibility = Visibility.Visible;
            GameLauncherActionButton.Visibility = Visibility.Visible;
            ComboPackagesActionButton.Visibility = Visibility.Visible;
            LockActionButton.Visibility = Visibility.Visible;
            SetLogoutActionVisible(_isMemberSession && !_isVipSession);
            SetTransferActionVisible(_isMemberSession);
            SetWithdrawActionVisible(_withdrawActionEnabledSetting);
            SetTopupRequestActionVisible(_topupActionEnabledSetting);
            LoyaltyActionButton.Visibility = (_isMemberSession && !_isCollapsed) ? Visibility.Visible : Visibility.Collapsed;
            PasswordActionButton.Visibility = (_isMemberSession && !_isCollapsed) ? Visibility.Visible : Visibility.Collapsed;
            ChangeWallpaperButton.Visibility = Visibility.Collapsed;
            FooterContainer.Visibility = (_isMemberSession && !_isCollapsed) ? Visibility.Visible : Visibility.Collapsed;
            if (MoreActionButton is not null) MoreActionButton.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            PromotionsContainer.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            MemberRankContainer.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            UpdateActionButtonsLayout();
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            UserInfoPanel.Visibility = Visibility.Collapsed;
            MemberRankContainer.Visibility = Visibility.Collapsed;
            if (VipIconImage is not null)
            {
                VipIconImage.Visibility = Visibility.Collapsed;
            }
            ApplyRankPulseAnimation(false, null, false);
            UpdateUsageUi();
            return;
        }

        UserInfoPanel.Visibility = Visibility.Visible;
        if (!_isAdminSession && !string.Equals(_currentMemberType, "COMBO", StringComparison.OrdinalIgnoreCase))
        {
            MemberRankContainer.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            MemberRankContainer.Visibility = Visibility.Collapsed;
        }
        MemberUsernameTextBlock.Text = username?.ToUpperInvariant() ?? string.Empty;
        
        var avatarToLoad = string.IsNullOrWhiteSpace(avatarId) ? "avatar_1.png" : avatarId;
        var avatarSource = avatarToLoad.StartsWith("data:image/") 
            ? AvatarSelectionWindow.ResolveIconSource(avatarToLoad) 
            : ResolveRankIconSource("Avatars/" + avatarToLoad);
        if (avatarSource != null)
        {
            AvatarButton.Background = new ImageBrush(avatarSource) { Stretch = Stretch.UniformToFill };
        }
        if (MemberRankVipLabel is not null)
        {
            MemberRankVipLabel.Visibility = _isVipSession ? Visibility.Visible : Visibility.Collapsed;
        }

        if (VipIconImage is not null)
        {
            VipIconImage.Visibility = _isVipSession ? Visibility.Visible : Visibility.Collapsed;
            if (_isVipSession && VipIconImage.Source is null)
            {
                VipIconImage.Source = ResolveRankIconSource("vip.png");
            }
        }

        var rankStr = string.IsNullOrWhiteSpace(rank) ? "SAT" : rank;
        var rankUpper = rankStr.ToUpperInvariant();
        var rankNormalized = NormalizeRankKey(rankUpper);
        var rankCompact = rankNormalized.Replace(" ", string.Empty);
        MemberRankTextBlock.Text = rankUpper;

        string bgColor = "#F1F5F9";
        string bgColor2 = "#E2E8F0";
        string? bgColor3 = null;
        string? bgColor4 = null;
        string fgColor = "#475569";
        string rankIcon = "🔰";
        string? rankIconAsset = null;
        var shouldPulse = false;
        var showSparkles = false;
        double waveOpacity = 0.6;
        double waveSpeed = 3.0;

        if (rankNormalized.Contains("SAT") || rankUpper.Contains("IRON"))
        {
            rankIconAsset = "sat.png";
            bgColor = "#F1F5F9";
            bgColor2 = "#CBD5E1";
        }
        else if (rankNormalized.Contains("DONG") || rankUpper.Contains("BRONZE"))
        {
            bgColor = "#FEF3C7";
            bgColor2 = "#FDE047";
            fgColor = "#B45309";
            rankIconAsset = "dong.png";
            rankIcon = "🥉";
        }
        else if (rankNormalized.Contains("BAC") || rankUpper.Contains("SILVER"))
        {
            bgColor = "#F3F4F6";
            bgColor2 = "#D1D5DB";
            fgColor = "#4B5563";
            rankIconAsset = "bac.png";
            rankIcon = "🥈";
        }
        else if (rankNormalized.Contains("VANG") || rankUpper.Contains("GOLD"))
        {
            bgColor = "#FEF08A";
            bgColor2 = "#F59E0B";
            fgColor = "#A16207";
            rankIconAsset = "vang.png";
            rankIcon = "🥇";
        }
        else if (rankNormalized.Contains("BACH KIM") || rankUpper.Contains("PLATINUM"))
        {
            bgColor = "#CFFAFE";
            bgColor2 = "#2DD4BF";
            fgColor = "#0E7490";
            rankIconAsset = "back-kim.png";
            rankIcon = "💠";
        }
        else if (rankNormalized.Contains("TINH ANH") ||
                 rankNormalized.Contains("LUC BAO") ||
                 rankUpper.Contains("EMERALD"))
        {
            bgColor = "#D1FAE5";
            bgColor2 = "#34D399";
            bgColor3 = "#059669";
            fgColor = "#047857";
            rankIconAsset = "tinh-anh.png";
            rankIcon = "💚";
            waveOpacity = 0.65;
            waveSpeed = 2.8;
        }
        else if (rankNormalized.Contains("KIM CUONG") || rankUpper.Contains("DIAMOND"))
        {
            bgColor = "#E0E7FF";
            bgColor2 = "#818CF8";
            bgColor3 = "#67E8F9"; // Cyan streak
            fgColor = "#4338CA";
            rankIconAsset = "kim-cuong.png";
            rankIcon = "💎";
            shouldPulse = true;
            waveOpacity = 0.7;
            waveSpeed = 2.5;
        }
        else if (rankNormalized.Contains("DAI CAO THU") || rankUpper.Contains("GRANDMASTER"))
        {
            bgColor = "#FEE2E2";
            bgColor2 = "#F87171";
            bgColor3 = "#FDE047"; // Yellow streak
            fgColor = "#B91C1C";
            rankIconAsset = "dai-cao-thu.png";
            rankIcon = "🛡️";
            shouldPulse = true;
            waveOpacity = 0.75;
            waveSpeed = 2.0;
        }
        else if (rankNormalized.Contains("CAO THU") || rankUpper.Contains("MASTER"))
        {
            bgColor = "#FAE8FF";
            bgColor2 = "#D946EF";
            bgColor3 = "#A78BFA"; // Violet streak
            fgColor = "#A21CAF";
            rankIconAsset = "cao-thu.png";
            rankIcon = "👑";
            shouldPulse = true;
            waveOpacity = 0.7;
            waveSpeed = 2.5;
        }
        else if (rankNormalized.Contains("THACH DAU") ||
                 rankCompact.Contains("THACHDAU") ||
                 rankUpper.Contains("CHALLENGER"))
        {
            bgColor = "#BFDBFE";
            bgColor2 = "#FDE047"; // Gold streak
            bgColor3 = "#D946EF"; // Strong purple streak
            bgColor4 = "#E9D5FF"; // Light purple streak
            fgColor = "#1D4ED8";
            rankIconAsset = "thach-dau.png";
            rankIcon = "🏆";
            shouldPulse = true;
            showSparkles = true;
            waveOpacity = 0.85;
            waveSpeed = 1.5; // Very fast shimmering
        }
        else if (rankUpper.Contains("VIP"))
        {
            bgColor = "#FCE7F3";
            bgColor2 = "#F472B6";
            bgColor3 = "#FDE047"; // Gold streak
            fgColor = "#BE185D";
            rankIcon = "✨";
            waveOpacity = 0.75;
            waveSpeed = 2.0;
        }

        var bc = new BrushConverter();
        var rankAccentBrush = (Brush)bc.ConvertFromString(fgColor)!;
        
        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            Opacity = waveOpacity
        };
        
        if (bgColor4 != null)
        {
            var stop1 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor), -0.5);
            var stop2 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor2), 0.0);
            var stop3 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor3), 0.5);
            var stop4 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor4), 1.0);
            var stop5 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor), 1.5);

            gradientBrush.GradientStops.Add(stop1);
            gradientBrush.GradientStops.Add(stop2);
            gradientBrush.GradientStops.Add(stop3);
            gradientBrush.GradientStops.Add(stop4);
            gradientBrush.GradientStops.Add(stop5);

            if (!_isCollapsed)
            {
                var anim1 = new DoubleAnimation(-0.5, 1.5, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var anim2 = new DoubleAnimation(0.0, 2.0, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var anim3 = new DoubleAnimation(0.5, 2.5, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var anim4 = new DoubleAnimation(1.0, 3.0, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var anim5 = new DoubleAnimation(1.5, 3.5, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };

                stop1.BeginAnimation(GradientStop.OffsetProperty, anim1);
                stop2.BeginAnimation(GradientStop.OffsetProperty, anim2);
                stop3.BeginAnimation(GradientStop.OffsetProperty, anim3);
                stop4.BeginAnimation(GradientStop.OffsetProperty, anim4);
                stop5.BeginAnimation(GradientStop.OffsetProperty, anim5);
            }
        }
        else if (bgColor3 != null)
        {
            var stop1 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor), -0.5);
            var stop2 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor2), 0.0);
            var stop3 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor3), 0.5);
            var stop4 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor), 1.0);

            gradientBrush.GradientStops.Add(stop1);
            gradientBrush.GradientStops.Add(stop2);
            gradientBrush.GradientStops.Add(stop3);
            gradientBrush.GradientStops.Add(stop4);

            if (!_isCollapsed)
            {
                var anim1 = new DoubleAnimation(-0.5, 1.5, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var anim2 = new DoubleAnimation(0.0, 2.0, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var anim3 = new DoubleAnimation(0.5, 2.5, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var anim4 = new DoubleAnimation(1.0, 3.0, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };

                stop1.BeginAnimation(GradientStop.OffsetProperty, anim1);
                stop2.BeginAnimation(GradientStop.OffsetProperty, anim2);
                stop3.BeginAnimation(GradientStop.OffsetProperty, anim3);
                stop4.BeginAnimation(GradientStop.OffsetProperty, anim4);
            }
        }
        else
        {
            var stop1 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor), -0.5);
            var stop2 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor2), 0.0);
            var stop3 = new GradientStop((Color)ColorConverter.ConvertFromString(bgColor), 0.5);

            gradientBrush.GradientStops.Add(stop1);
            gradientBrush.GradientStops.Add(stop2);
            gradientBrush.GradientStops.Add(stop3);

            if (!_isCollapsed)
            {
                var anim1 = new DoubleAnimation(-0.5, 1.5, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var anim2 = new DoubleAnimation(0.0, 2.0, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                var anim3 = new DoubleAnimation(0.5, 2.5, new Duration(TimeSpan.FromSeconds(waveSpeed))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };

                stop1.BeginAnimation(GradientStop.OffsetProperty, anim1);
                stop2.BeginAnimation(GradientStop.OffsetProperty, anim2);
                stop3.BeginAnimation(GradientStop.OffsetProperty, anim3);
            }
        }

        MemberRankBorder.Background = gradientBrush;
        MemberRankBorder.BorderBrush = rankAccentBrush;
        MemberRankTextBlock.Foreground = rankAccentBrush;
        MemberRankIconTextBlock.Foreground = rankAccentBrush;
        MemberRankIconBadgeBorder.Background = (Brush)bc.ConvertFromString(bgColor)!;
        MemberRankIconBadgeBorder.BorderBrush = (Brush)bc.ConvertFromString(bgColor)!;
        ApplyRankIcon(rankIconAsset, rankIcon);
        ApplyRankPulseAnimation(!_isCollapsed && shouldPulse, rankAccentBrush, !_isCollapsed && showSparkles);
        UpdateUsageUi();
    }

    private void ApplyRankIcon(string? iconAssetName, string fallbackIcon)
    {
        MemberRankIconTextBlock.Text = fallbackIcon;
        MemberRankIconTextBlock.Visibility = Visibility.Visible;

        if (MemberRankIconImage is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(iconAssetName))
        {
            MemberRankIconImage.Source = null;
            MemberRankIconImage.Visibility = Visibility.Collapsed;
            return;
        }

        var iconSource = ResolveRankIconSource(iconAssetName);
        if (iconSource is null)
        {
            MemberRankIconImage.Source = null;
            MemberRankIconImage.Visibility = Visibility.Collapsed;
            MemberRankIconTextBlock.Visibility = Visibility.Visible;
            return;
        }

        MemberRankIconImage.Source = iconSource;
        MemberRankIconImage.Visibility = Visibility.Visible;
        MemberRankIconTextBlock.Visibility = Visibility.Collapsed;
    }

    private static ImageSource? ResolveRankIconSource(string iconAssetName)
    {
        try
        {
            var packUri = new Uri($"pack://application:,,,/Assets/{iconAssetName}", UriKind.Absolute);
            return new BitmapImage(packUri);
        }
        catch
        {
            // fall through to file-based loading
        }

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", iconAssetName);
            if (!File.Exists(iconPath))
            {
                return null;
            }

            var fileUri = new System.Uri(iconPath, System.UriKind.Absolute);
            return new BitmapImage(fileUri);
        }
        catch
        {
            return null;
        }
    }

    private void ApplyRankPulseAnimation(bool shouldPulse, Brush? accentBrush, bool showSparkles)
    {
        if (shouldPulse && accentBrush is SolidColorBrush solidBrush)
        {
            MemberRankGlowBorder.Opacity = 1;
            MemberRankGlowEffect.Color = solidBrush.Color;

            var blurAnim = new DoubleAnimation(20, 30, new Duration(TimeSpan.FromMilliseconds(1000))) 
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            
            var opacAnim = new DoubleAnimation(0.6, 0.9, new Duration(TimeSpan.FromMilliseconds(1200))) 
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };

            MemberRankGlowEffect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blurAnim);
            MemberRankGlowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, opacAnim);
        }
        else
        {
            MemberRankGlowBorder.Opacity = 0;
            MemberRankGlowEffect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            MemberRankGlowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        }

        if (SparklesContainer != null)
        {
            if (showSparkles)
            {
                SparklesContainer.Visibility = Visibility.Visible;
                Sparkle1.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1.0, new Duration(TimeSpan.FromMilliseconds(800))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                Sparkle2.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.1, 0.9, new Duration(TimeSpan.FromMilliseconds(1100))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                Sparkle3.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 1.0, new Duration(TimeSpan.FromMilliseconds(600))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                Sparkle4.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 0.8, new Duration(TimeSpan.FromMilliseconds(1300))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                Sparkle5.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.4, 1.0, new Duration(TimeSpan.FromMilliseconds(900))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                Sparkle6?.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1.0, new Duration(TimeSpan.FromMilliseconds(1000))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                Sparkle7?.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.1, 0.9, new Duration(TimeSpan.FromMilliseconds(700))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                Sparkle8?.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 0.8, new Duration(TimeSpan.FromMilliseconds(1200))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

                Sparkle1Transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-20, 150, new Duration(TimeSpan.FromMilliseconds(4000))) { RepeatBehavior = RepeatBehavior.Forever });
                Sparkle2Transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 160, new Duration(TimeSpan.FromMilliseconds(5500))) { RepeatBehavior = RepeatBehavior.Forever });
                Sparkle3Transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-30, 140, new Duration(TimeSpan.FromMilliseconds(4500))) { RepeatBehavior = RepeatBehavior.Forever });
                Sparkle4Transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-15, 170, new Duration(TimeSpan.FromMilliseconds(6000))) { RepeatBehavior = RepeatBehavior.Forever });
                Sparkle5Transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-5, 145, new Duration(TimeSpan.FromMilliseconds(5000))) { RepeatBehavior = RepeatBehavior.Forever });
                Sparkle6Transform?.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-25, 155, new Duration(TimeSpan.FromMilliseconds(4800))) { RepeatBehavior = RepeatBehavior.Forever });
                Sparkle7Transform?.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 165, new Duration(TimeSpan.FromMilliseconds(6500))) { RepeatBehavior = RepeatBehavior.Forever });
                Sparkle8Transform?.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-35, 150, new Duration(TimeSpan.FromMilliseconds(5200))) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                SparklesContainer.Visibility = Visibility.Collapsed;
                Sparkle1.BeginAnimation(UIElement.OpacityProperty, null);
                Sparkle2.BeginAnimation(UIElement.OpacityProperty, null);
                Sparkle3.BeginAnimation(UIElement.OpacityProperty, null);
                Sparkle4.BeginAnimation(UIElement.OpacityProperty, null);
                Sparkle5.BeginAnimation(UIElement.OpacityProperty, null);
                Sparkle6?.BeginAnimation(UIElement.OpacityProperty, null);
                Sparkle7?.BeginAnimation(UIElement.OpacityProperty, null);
                Sparkle8?.BeginAnimation(UIElement.OpacityProperty, null);

                Sparkle1Transform.BeginAnimation(TranslateTransform.YProperty, null);
                Sparkle2Transform.BeginAnimation(TranslateTransform.YProperty, null);
                Sparkle3Transform.BeginAnimation(TranslateTransform.YProperty, null);
                Sparkle4Transform.BeginAnimation(TranslateTransform.YProperty, null);
                Sparkle5Transform.BeginAnimation(TranslateTransform.YProperty, null);
                Sparkle6Transform?.BeginAnimation(TranslateTransform.YProperty, null);
                Sparkle7Transform?.BeginAnimation(TranslateTransform.YProperty, null);
                Sparkle8Transform?.BeginAnimation(TranslateTransform.YProperty, null);
            }
        }
    }

    private static string NormalizeRankKey(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        var folded = sb.ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('\u0110', 'D')
            .Replace('\u0111', 'd');
        return folded.ToUpperInvariant();
    }

    private void SetLogoutActionVisible(bool visible)
    {
        if (LogoutActionButton is null)
        {
            return;
        }

        if (_isAdminSession)
        {
            LogoutActionButton.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            UpdateActionButtonsLayout();
            return;
        }

        bool isCombo = string.Equals(_currentMemberType, "COMBO", StringComparison.OrdinalIgnoreCase);
        LogoutActionButton.Visibility = (visible && !isCombo) ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButtonsLayout();
    }

    public void SetTransferActionVisible(bool visible)
    {
        if (TransferActionButton is null)
        {
            return;
        }

        bool isCombo = string.Equals(_currentMemberType, "COMBO", StringComparison.OrdinalIgnoreCase);
        TransferActionButton.Visibility = (visible && !_isVipSession && !isCombo) ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButtonsLayout();
    }

    public void SetWithdrawActionVisible(bool visible)
    {
        _withdrawActionEnabledSetting = visible;
        if (WithdrawActionButton is null)
        {
            return;
        }

        bool isCombo = string.Equals(_currentMemberType, "COMBO", StringComparison.OrdinalIgnoreCase);
        WithdrawActionButton.Visibility = (visible && _isMemberSession && !_isVipSession && !isCombo) ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButtonsLayout();
    }

    public void SetTopupRequestActionVisible(bool visible)
    {
        _topupActionEnabledSetting = visible;
        if (TopupRequestActionButton is null)
        {
            return;
        }

        bool isCombo = string.Equals(_currentMemberType, "COMBO", StringComparison.OrdinalIgnoreCase);
        TopupRequestActionButton.Visibility = (visible && _isMemberSession && !_isVipSession && !isCombo) ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButtonsLayout();
    }

    private void UpdateActionButtonsLayout()
    {
        if (ActionButtonsGrid is null) return;
        if (_isAdminSession)
        {
            ActionButtonsGrid.Columns = 1;
            return;
        }
        int visibleCount = 0;
        foreach (System.Windows.UIElement child in ActionButtonsGrid.Children)
        {
            if (child.Visibility == System.Windows.Visibility.Visible)
            {
                visibleCount++;
            }
        }
        ActionButtonsGrid.Columns = visibleCount < 4 ? 1 : 3;
    }

    public void AllowShutdown()
    {
        _allowClose = true;
        _usageTimer.Stop();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        // Basic anti-close behavior for Phase 5: hide instead of closing.
        e.Cancel = true;
        Hide();
    }

    private void UsageTimer_Tick(object? sender, EventArgs e)
    {
        UpdateUsageUi();
        CheckAutoCollapse();
    }

    private void CheckAutoCollapse()
    {
        if (_autoCollapseIntervalSeconds > 0 && !_isCollapsed)
        {
            var idleSeconds = (DateTime.UtcNow - _lastClientInteractionTime).TotalSeconds;
            if (idleSeconds >= _autoCollapseIntervalSeconds)
            {
                CollapseButton_Click(this, new RoutedEventArgs());
            }
        }
    }

    public void UpdateAutoCollapseInterval(int seconds)
    {
        _autoCollapseIntervalSeconds = seconds;
        ResetClientActivity();
    }

    public void ResetClientActivity()
    {
        _lastClientInteractionTime = DateTime.UtcNow;
    }

    private void ResumeSession()
    {
        if (_runningStartedAtUtc is null)
        {
            _runningStartedAtUtc = DateTime.UtcNow;
        }
    }

    private void PauseSession()
    {
        if (_runningStartedAtUtc is null)
        {
            return;
        }

        _usedDuration += DateTime.UtcNow - _runningStartedAtUtc.Value;
        _runningStartedAtUtc = null;
    }

    private void EndSession(bool resetUsage)
    {
        PauseSession();

        if (!resetUsage)
        {
            return;
        }

        _usedDuration = TimeSpan.Zero;
        _serviceCost = 0;
        _serviceOrderCount = 0;
        _serverEstimatedCost = null;
        UpdateServiceBadgeUi();
    }

    private TimeSpan GetCurrentUsedDuration()
    {
        if (_runningStartedAtUtc is null)
        {
            return _usedDuration;
        }

        return _usedDuration + (DateTime.UtcNow - _runningStartedAtUtc.Value);
    }

    public int GetUsedSeconds()
    {
        return Math.Max(0, (int)GetCurrentUsedDuration().TotalSeconds);
    }

    public int GetRemainingMinutes()
    {
        if (_isMemberSession && (!_isVipSession || IsUsingPromoPlayTimeNow))
        {
            var elapsedNow = Math.Max(0, (int)GetCurrentUsedDuration().TotalSeconds);
            var remainingRawSeconds = ComputeMemberRemainingSecondsRealtime(elapsedNow);
            return remainingRawSeconds <= 0
                ? 0
                : Math.Max(1, (int)Math.Ceiling(remainingRawSeconds / 60.0));
        }

        var total = TimeSpan.FromMinutes(GetEffectiveTotalSessionMinutes());
        var used = GetCurrentUsedDuration();
        var totalMins = (int)total.TotalMinutes;
        var elapsedSeconds = Math.Max(0, (int)used.TotalSeconds);
        var hasSessionUsage = _runningStartedAtUtc is not null || _usedDuration > TimeSpan.Zero;
        var usedMins = hasSessionUsage
            ? Math.Max(0, (int)Math.Floor(elapsedSeconds / 60.0))
            : 0;

        return Math.Max(0, totalMins - usedMins);
    }

    private int GetEffectiveTotalSessionMinutes()
    {
        if ((_isVipSession && !IsUsingPromoPlayTimeNow) || _isPostpaidSession)
        {
            return VipOrPostpaidTotalSessionMinutes;
        }

        return Math.Max(1, _totalSessionMinutes);
    }

    private int ComputeRoundedMemberRemainingMinutesFromAccount()
    {
        var pricePerMinute = _hourlyRate > 0 ? (_hourlyRate / 60m) : 0m;
        var balanceSeconds = pricePerMinute > 0
            ? (_memberBalance / pricePerMinute) * 60m
            : 0m;
        var effectivePlaySeconds = _usePromoPlayTime ? Math.Max(0, _playSeconds) : 0;
        var totalRemainingSeconds = Math.Max(0m, balanceSeconds) + effectivePlaySeconds;
        if (totalRemainingSeconds <= 0m)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling((double)(totalRemainingSeconds / 60m)));
    }

    private int ComputeMemberRemainingSecondsRealtime(int elapsedSecondsNow)
    {
        var pricePerMinute = _hourlyRate > 0 ? (_hourlyRate / 60m) : 0m;
        var balanceSecondsAtSync = pricePerMinute > 0
            ? (_lastSyncMemberBalance / pricePerMinute) * 60m
            : 0m;
        var effectivePlaySeconds = _usePromoPlayTime ? Math.Max(0, _playSeconds) : 0;
        var remainingSecondsAtSync = Math.Max(0m, balanceSecondsAtSync) + effectivePlaySeconds;
        var elapsedSinceSync = Math.Max(0, elapsedSecondsNow - _lastSyncElapsedSeconds);
        var remainingRawSeconds = remainingSecondsAtSync - elapsedSinceSync;
        return Math.Max(0, (int)Math.Floor((double)remainingRawSeconds));
    }

    private void UpdateUsageUi()
    {
        var total = TimeSpan.FromMinutes(GetEffectiveTotalSessionMinutes());
        var used = GetCurrentUsedDuration();

        // Keep client display aligned with server/admin:
        // elapsed seconds are rounded up to billable minutes and minimum is 1 minute.
        var totalMins = (int)total.TotalMinutes;
        var elapsedSeconds = Math.Max(0, (int)used.TotalSeconds);
        var hasSessionUsage = _runningStartedAtUtc is not null || _usedDuration > TimeSpan.Zero;
        var totalSecs = 0;
        var remainingSecs = 0;
        var remainingRawSecsForProgress = 0;
        var usedSecs = 0;
        var usedMins = 0;
        var remainingMins = 0;
        
        if (hasSessionUsage)
        {
            usedSecs = elapsedSeconds;
            usedMins = (int)Math.Floor(elapsedSeconds / 60.0);
            
            if (_isVipSession && !IsUsingPromoPlayTimeNow)
            {
                totalMins = VipOrPostpaidTotalSessionMinutes;
                totalSecs = totalMins * 60;
                remainingMins = Math.Max(0, totalMins - usedMins);
                remainingSecs = Math.Max(0, totalSecs - usedSecs);
            }
            else if (_isMemberSession)
            {
                totalMins = Math.Max(1, _totalSessionMinutes);
                totalSecs = totalMins * 60;
                remainingRawSecsForProgress = ComputeMemberRemainingSecondsRealtime(elapsedSeconds);
                remainingMins = remainingRawSecsForProgress <= 0
                    ? 0
                    : Math.Max(1, (int)Math.Ceiling(remainingRawSecsForProgress / 60.0));
                remainingSecs = remainingMins * 60;
            }
            else
            {
                totalSecs = totalMins * 60;
                remainingSecs = Math.Max(0, totalSecs - usedSecs);
                remainingMins = Math.Max(0, totalMins - usedMins);
            }
        }
        else
        {
            if (_isMemberSession && (!_isVipSession || IsUsingPromoPlayTimeNow))
            {
                totalMins = Math.Max(1, _totalSessionMinutes);
                totalSecs = totalMins * 60;
                remainingMins = ComputeRoundedMemberRemainingMinutesFromAccount();
                remainingSecs = remainingMins * 60;
                remainingRawSecsForProgress = remainingSecs;
            }
            else if (_isVipSession && !IsUsingPromoPlayTimeNow)
            {
                totalMins = VipOrPostpaidTotalSessionMinutes;
                totalSecs = totalMins * 60;
                remainingMins = totalMins;
                remainingSecs = totalSecs;
            }
            else
            {
                totalSecs = totalMins * 60;
                remainingMins = totalMins;
                remainingSecs = totalSecs;
            }
        }

        var billableMins = hasSessionUsage
            ? Math.Max(1, (int)Math.Ceiling(elapsedSeconds / 60.0))
            : 0;
        var rawCost = billableMins * (_hourlyRate / 60m);
        var step = PricingStep > 0 ? PricingStep : 1000m;
        var roundedCost = Math.Ceiling(rawCost / step) * step;
        var isPostpaidUser = !_isMemberSession || _isPostpaidSession || (_isVipSession && !IsUsingPromoPlayTimeNow);
        var gameCost = isPostpaidUser 
            ? Math.Max(MinimumCharge > 0 ? MinimumCharge : 1000m, roundedCost) 
            : (billableMins <= 0 ? 0m : roundedCost);

        if (_serverEstimatedCost.HasValue && _serverEstimatedCost.Value >= 0 && isPostpaidUser)
        {
            gameCost = _serverEstimatedCost.Value;
        }

        TotalTimeValueTextBlock.Text = FormatSeconds(totalSecs);

        if (remainingSecs == 0 && _totalSessionMinutes < DefaultTotalSessionMinutes)
        {
            TimeExpired?.Invoke(this, EventArgs.Empty);
        }

        var displayRemainingSecs = remainingSecs;
        var displayUsedSecs = (usedSecs / 60) * 60;
        var displayTotalSecs = _isMemberSession
            ? displayRemainingSecs + displayUsedSecs
            : totalMins * 60;

        TotalTimeValueTextBlock.Text = FormatSeconds(displayTotalSecs);
        UsedTimeValueTextBlock.Text = FormatSeconds(displayUsedSecs);
        var isUnlimitedCombo = string.Equals(_currentMemberType, "COMBO", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_currentComboExpiresAt) && displayRemainingSecs > 100000 * 3600;

        if (string.Equals(_currentMemberType, "COMBO", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_currentComboExpiresAt))
        {
            if (isUnlimitedCombo) // Unlimited combo
            {
                if (DateTime.TryParse(_currentComboExpiresAt, out var parsedExpiresAt))
                {
                    var startTime = DateTime.Now.AddSeconds(-displayUsedSecs).ToString("HH:mm");
                    var endTime = parsedExpiresAt.ToLocalTime().ToString("HH:mm");
                    RemainingTimeValueTextBlock.Text = $"{startTime} - {endTime}";
                }
                else
                {
                    RemainingTimeValueTextBlock.Text = _currentComboExpiresAt;
                }
            }
            else // Limited combo
            {
                RemainingTimeValueTextBlock.Text = FormatSeconds(displayRemainingSecs);
            }
        }
        else
        {
            RemainingTimeValueTextBlock.Text = FormatSeconds(displayRemainingSecs);
        }

        bool showTotalTime = (_isMemberSession && (!_isVipSession || IsUsingPromoPlayTimeNow) && !_isAdminSession && !isUnlimitedCombo) || 
                             (!_isMemberSession && !_isPostpaidSession && !_isAdminSession);
        var totalTimeVisibility = showTotalTime ? Visibility.Visible : Visibility.Collapsed;
        TotalTimeContainer.Visibility = totalTimeVisibility;

        double percentage = 1.0;
        if (_isMemberSession && (!_isVipSession || IsUsingPromoPlayTimeNow))
        {
            var totalSecondsForProgress = Math.Max(1.0, usedSecs + remainingRawSecsForProgress);
            percentage = Math.Max(0, remainingRawSecsForProgress) / totalSecondsForProgress;
        }
        else if (totalMins > 0)
        {
            double totalSeconds = totalMins * 60.0;
            double remainingSeconds = Math.Max(0, totalSeconds - elapsedSeconds);
            percentage = remainingSeconds / totalSeconds;
        }
        UpdateProgressArc(percentage);

        PercentageValueTextBlock.Text = $"{(int)Math.Round(percentage * 100)}%";
        PercentageBadge.Visibility = totalTimeVisibility;

        if (_isMemberSession)
        {
            if ((_isVipSession && !IsUsingPromoPlayTimeNow) || _isPostpaidSession)
            {
                GameCostValueTextBlock.Text = gameCost.ToString("N0", CultureInfo.InvariantCulture);
            }
            else
            {
                GameCostValueTextBlock.Text = "-";
            }
            ServiceCostValueTextBlock.Text = _serviceCost > 0 ? _serviceCost.ToString("N0", CultureInfo.InvariantCulture) : "-";
            
            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                _overlayWindow.UpdateData(MemberUsernameTextBlock.Text, GameCostValueTextBlock.Text, RemainingTimeValueTextBlock.Text, isPostpaidUser);
            }
            return;
        }

        GameCostValueTextBlock.Text = gameCost.ToString("N0", CultureInfo.InvariantCulture);
        ServiceCostValueTextBlock.Text = _serviceCost.ToString("N0", CultureInfo.InvariantCulture);
        
        if (_overlayWindow != null && _overlayWindow.IsVisible)
        {
            _overlayWindow.UpdateData(MemberUsernameTextBlock.Text, GameCostValueTextBlock.Text, RemainingTimeValueTextBlock.Text, isPostpaidUser);
        }
    }

    private double _lastProgressPercentage = -1;

    private void UpdateProgressArc(double percentage)
    {
        if (ProgressArcPath == null) return;
        if (WindowState == WindowState.Minimized) return;
        
        percentage = Math.Max(0, Math.Min(1, percentage));

        if (Math.Abs(_lastProgressPercentage - percentage) < 0.0005 && ProgressArcPath.Data != null)
        {
            return;
        }
        _lastProgressPercentage = percentage;
        
        double radius = 132.5;
        double centerX = 140;
        double centerY = 140;
        
        double startAngle = -Math.PI / 2;
        double endAngle = startAngle + (percentage * 2 * Math.PI);
        
        if (percentage >= 0.999)
        {
            endAngle = startAngle + (0.999 * 2 * Math.PI);
        }
        else if (percentage <= 0.001)
        {
             ProgressArcPath.Data = null;
             return;
        }

        double startX = centerX + radius * Math.Cos(startAngle);
        double startY = centerY + radius * Math.Sin(startAngle);
        
        double endX = centerX + radius * Math.Cos(endAngle);
        double endY = centerY + radius * Math.Sin(endAngle);
        
        bool isLargeArc = percentage > 0.5;
        
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(startX, startY),
            IsClosed = false
        };
        figure.Segments.Add(new ArcSegment(
            new Point(endX, endY), 
            new Size(radius, radius), 
            0, 
            isLargeArc, 
            SweepDirection.Clockwise, 
            true));
            
        geometry.Figures.Add(figure);
        ProgressArcPath.Data = geometry;
    }

    public void UpdatePromotions(List<(string Name, decimal Discount)> promotions)
    {
        if (_isAdminSession)
        {
            PromotionsContainer.Visibility = Visibility.Collapsed;
            return;
        }
        var toRemove = new List<UIElement>();
        foreach (UIElement child in PromotionsContainer.Children)
        {
            if (child != LoyaltyPromotionBannerBorder && child != TopupPromotionBannerBorder)
            {
                toRemove.Add(child);
            }
        }
        foreach (var child in toRemove)
        {
            PromotionsContainer.Children.Remove(child);
        }

        if (promotions != null && promotions.Count > 0)
        {
            if (!_isMemberSession)
            {
                var border = CreateGuestPromotionBanner();
                PromotionsContainer.Children.Insert(0, border);
            }
            else
            {
                var highestPromo = promotions.OrderByDescending(p => p.Discount).First();
                var border = CreatePromotionBanner(highestPromo.Name, highestPromo.Discount);
                PromotionsContainer.Children.Insert(0, border);
            }
        }

        UpdatePromotionsLayout();
    }

    private Border CreatePromotionBanner(string name, decimal discountPercent)
    {
        var border = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xCD, 0xD3)),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconContainer = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xF2)),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var viewBox = new Viewbox
        {
            Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var path = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48)),
            Data = Geometry.Parse("M12,2 L15,9 L22,9 L16,14 L18,21 L12,17 L6,21 L8,14 L2,9 L9,9 Z")
        };
        viewBox.Child = path;
        iconContainer.Child = viewBox;
        Grid.SetColumn(iconContainer, 0);
        grid.Children.Add(iconContainer);

        var stackPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = name
        };
        var discountText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48)),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Text = $"Giảm {discountPercent:0.#}% tiền giờ chơi",
            Margin = new Thickness(0, 2, 0, 0)
        };
        stackPanel.Children.Add(nameText);
        stackPanel.Children.Add(discountText);
        Grid.SetColumn(stackPanel, 1);
        grid.Children.Add(stackPanel);

        border.Child = grid;
        return border;
    }

    private Border CreateGuestPromotionBanner()
    {
        var border = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xD7, 0xAA)),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconContainer = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF7, 0xED)),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var viewBox = new Viewbox
        {
            Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var path = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
            Stroke = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
            StrokeThickness = 2,
            Data = Geometry.Parse("M12,2 L22,20 L2,20 Z M12,8 L12,14 M12,17 L12,17.5")
        };
        viewBox.Child = path;
        iconContainer.Child = viewBox;
        Grid.SetColumn(iconContainer, 0);
        grid.Children.Add(iconContainer);

        var stackPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = "Thông báo khuyến mãi"
        };
        var discountText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Text = "Chỉ dành cho Hội Viên",
            Margin = new Thickness(0, 2, 0, 0)
        };
        stackPanel.Children.Add(nameText);
        stackPanel.Children.Add(discountText);
        Grid.SetColumn(stackPanel, 1);
        grid.Children.Add(stackPanel);

        border.Child = grid;
        return border;
    }

    public void UpdateLoyaltyMultiplier(double multiplier)
    {
        if (multiplier > 1.0 && _isMemberSession)
        {
            LoyaltyPromotionDiscountTextBlock.Text = $"Hệ số nhân điểm: x{multiplier:0.##}";
            LoyaltyPromotionBannerBorder.Visibility = Visibility.Visible;
        }
        else
        {
            LoyaltyPromotionBannerBorder.Visibility = Visibility.Collapsed;
        }
        UpdatePromotionsLayout();
    }

    public void UpdateTopupPromotion(bool enabled, List<(decimal MinAmount, decimal BonusRate)> tiers)
    {
        try { System.IO.File.AppendAllText(@"C:\ProgramData\ServerManagerBilling\logs\client-agent.log", $"[{DateTime.Now:O}] [INFO] UpdateTopupPromotion called: enabled={enabled}, tiers={tiers?.Count}\n"); } catch {}
        
        _hasTopupPromotion = enabled && tiers != null && tiers.Count > 0;
        
        if (_hasTopupPromotion)
        {
            if (_isAdminSession)
            {
                TopupPromotionBannerBorder.Visibility = Visibility.Collapsed;
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine("Đang có chương trình khuyến mãi nạp tiền:");
            TopupPromotionTiersPanel.Children.Clear();

            foreach (var tier in tiers!.OrderBy(t => t.MinAmount))
            {
                var formattedAmount = (tier.MinAmount / 1000m).ToString("0.##") + "k";
                sb.AppendLine($"- Nạp từ {formattedAmount} khuyến mãi {tier.BonusRate:0.##}%");

                var chipBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xFD, 0xF5)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xA7, 0xF3, 0xD0)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 8, 3)
                };

                var chipStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                chipStack.Children.Add(new TextBlock
                {
                    Text = $"Nạp ≥ {formattedAmount}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var rateBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                rateBadge.Child = new TextBlock
                {
                    Text = $"+{tier.BonusRate:0.##}%",
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                };

                chipStack.Children.Add(rateBadge);
                chipBorder.Child = chipStack;
                TopupPromotionTiersPanel.Children.Add(chipBorder);
            }
            TopupPromotionDiscountTextBlock.Text = sb.ToString().TrimEnd();
            TopupPromotionBannerBorder.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            TopupPromotionBannerBorder.Visibility = Visibility.Collapsed;
        }
        UpdatePromotionsLayout();
    }

    private void UpdatePromotionsLayout()
    {
        int visibleCount = 0;
        foreach (UIElement child in PromotionsContainer.Children)
        {
            if (child is Border border && border.Visibility == Visibility.Visible)
            {
                visibleCount++;
            }
        }
        
        PromotionsContainer.Columns = visibleCount >= 2 ? 2 : 1;
        
        int currentIndex = 0;
        foreach (UIElement child in PromotionsContainer.Children)
        {
            if (child is Border border && border.Visibility == Visibility.Visible)
            {
                if (visibleCount >= 2)
                {
                    if (currentIndex % 2 == 0)
                        border.Margin = new Thickness(0, 0, 4, 8);
                    else
                        border.Margin = new Thickness(4, 0, 0, 8);
                }
                else
                {
                    border.Margin = new Thickness(0, 0, 0, 8);
                }
                currentIndex++;
            }
        }
    }

    private static string FormatMinutes(int totalMinutes)
    {
        var hours = totalMinutes / 60;
        var mins = totalMinutes % 60;
        return $"{hours:00}:{mins:00}";
    }

    private static string FormatSeconds(int totalSeconds)
    {
        if (totalSeconds >= 359940)
        {
            return "99:99";
        }
        var hours = totalSeconds / 3600;
        var mins = (totalSeconds % 3600) / 60;
        return $"{hours:00}:{mins:00}";
    }

    private void UpdateServiceBadgeUi()
    {
        if (ServicesBadgeBorder is null || ServicesBadgeTextBlock is null)
        {
            return;
        }

        if (_serviceOrderCount <= 0)
        {
            ServicesBadgeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        ServicesBadgeBorder.Visibility = Visibility.Visible;
        ServicesBadgeTextBlock.Text = _serviceOrderCount > 99
            ? "99+"
            : _serviceOrderCount.ToString(CultureInfo.InvariantCulture);
    }


    private void ComboPackagesButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ComboPackagesWindow();
        window.ShowDialog();
    }

    private void MessagesButton_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        var socketService = app.GetSocketService();
        if (socketService == null)
        {
            MessageBox.Show("Không thể kết nối với máy chủ.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var existingChat = Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault();
        if (existingChat != null)
        {
            if (existingChat.WindowState == WindowState.Minimized)
            {
                existingChat.WindowState = WindowState.Normal;
            }
            existingChat.Activate();
            return;
        }

        var dialog = new ChatWindow(app, socketService)
        {
            Owner = this
        };
        dialog.Show(); // Use Show() instead of ShowDialog() to be consistent with App.xaml.cs
    }

    private void ServicesButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.OpenServicesPanelFromClientUi();
            return;
        }

        MessageBox.Show(
            "\u1ee8ng d\u1ee5ng ch\u01b0a s\u1eb5n s\u00e0ng \u0111\u1ec3 g\u1ecdi d\u1ecbch v\u1ee5.",
            "D\u1ecbch v\u1ee5",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void LoyaltyButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.OpenLoyaltyPanelFromClientUi();
            return;
        }

        MessageBox.Show(
            "Ứng dụng chưa sẵn sàng để kiểm tra điểm tích lũy.",
            "Điểm tích lũy",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void LeaderboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsOverlay is not null) MoreActionsOverlay.Visibility = Visibility.Collapsed;
        if (Application.Current is App app)
        {
            app.OpenLeaderboardPanelFromClientUi();
            return;
        }

        MessageBox.Show(
            "Ứng dụng chưa sẵn sàng để xem bảng xếp hạng.",
            "Bảng xếp hạng",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void DailyMissionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsOverlay is not null) MoreActionsOverlay.Visibility = Visibility.Collapsed;
        if (Application.Current is App app)
        {
            app.OpenDailyMissionsPanelFromClientUi();
            return;
        }

        MessageBox.Show(
            "Ứng dụng chưa sẵn sàng để làm nhiệm vụ.",
            "Nhiệm vụ hàng ngày",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TransferBalanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsOverlay is not null) MoreActionsOverlay.Visibility = Visibility.Collapsed;
        if (Application.Current is App app)
        {
            app.OpenTransferBalancePanelFromClientUi();
            return;
        }

        MessageBox.Show(
            "\u1ee8ng d\u1ee5ng ch\u01b0a s\u1eb5n s\u00e0ng \u0111\u1ec3 chuy\u1ec3n ti\u1ec1n h\u1ed9i vi\u00ean.",
            "Chuy\u1ec3n ti\u1ec1n",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void WithdrawBalanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsOverlay is not null) MoreActionsOverlay.Visibility = Visibility.Collapsed;
        if (Application.Current is App app)
        {
            app.OpenWithdrawBalancePanelFromClientUi();
            return;
        }

        MessageBox.Show(
            "\u1ee8ng d\u1ee5ng ch\u01b0a s\u1eb5n s\u00e0ng \u0111\u1ec3 r\u00fat ti\u1ec1n h\u1ed9i vi\u00ean.",
            "R\u00fat ti\u1ec1n",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TopupRequestButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.OpenTopupRequestPanelFromClientUi();
            return;
        }

        MessageBox.Show(
            "\u1ee8ng d\u1ee5ng ch\u01b0a s\u1eb5n s\u00e0ng \u0111\u1ec3 g\u1eedi y\u00eau c\u1ea7u n\u1ea1p ti\u1ec1n.",
            "N\u1ea1p ti\u1ec1n",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnVpnStatusChanged(VpnStatus status)
    {
        Dispatcher.Invoke(() => UpdateVpnUi(status));
    }

    private void UpdateVpnUi(VpnStatus status)
    {
        if (VpnStatusBadgeBorder == null || VpnSubLabelTextBlock == null) return;

        switch (status)
        {
            case VpnStatus.Connected:
                VpnStatusBadgeBorder.Visibility = Visibility.Visible;
                VpnStatusBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                VpnStatusBadgeText.Text = "ON";
                VpnSubLabelTextBlock.Text = "Đang bật";
                VpnSubLabelTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                if (VpnIconBrush != null && VpnIconBrush.GradientStops.Count >= 2)
                {
                    VpnIconBrush.GradientStops[0].Color = (Color)ColorConverter.ConvertFromString("#10B981");
                    VpnIconBrush.GradientStops[1].Color = (Color)ColorConverter.ConvertFromString("#34D399");
                }
                if (VpnIconGlow != null)
                {
                    VpnIconGlow.Color = (Color)ColorConverter.ConvertFromString("#10B981");
                    VpnIconGlow.Opacity = 0.8;
                }
                break;

            case VpnStatus.Connecting:
                VpnStatusBadgeBorder.Visibility = Visibility.Visible;
                VpnStatusBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                VpnStatusBadgeText.Text = "...";
                VpnSubLabelTextBlock.Text = "Đang kết nối...";
                VpnSubLabelTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                if (VpnIconBrush != null && VpnIconBrush.GradientStops.Count >= 2)
                {
                    VpnIconBrush.GradientStops[0].Color = (Color)ColorConverter.ConvertFromString("#F59E0B");
                    VpnIconBrush.GradientStops[1].Color = (Color)ColorConverter.ConvertFromString("#FBBF24");
                }
                if (VpnIconGlow != null)
                {
                    VpnIconGlow.Color = (Color)ColorConverter.ConvertFromString("#F59E0B");
                    VpnIconGlow.Opacity = 0.6;
                }
                break;

            case VpnStatus.Installing:
                VpnStatusBadgeBorder.Visibility = Visibility.Visible;
                VpnStatusBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                VpnStatusBadgeText.Text = "SETUP";
                VpnSubLabelTextBlock.Text = "Đang cài đặt...";
                VpnSubLabelTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                if (VpnIconBrush != null && VpnIconBrush.GradientStops.Count >= 2)
                {
                    VpnIconBrush.GradientStops[0].Color = (Color)ColorConverter.ConvertFromString("#F59E0B");
                    VpnIconBrush.GradientStops[1].Color = (Color)ColorConverter.ConvertFromString("#FBBF24");
                }
                break;

            case VpnStatus.NotInstalled:
                VpnStatusBadgeBorder.Visibility = Visibility.Collapsed;
                VpnSubLabelTextBlock.Text = "Chưa cài đặt";
                VpnSubLabelTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                if (VpnIconBrush != null && VpnIconBrush.GradientStops.Count >= 2)
                {
                    VpnIconBrush.GradientStops[0].Color = (Color)ColorConverter.ConvertFromString("#64748B");
                    VpnIconBrush.GradientStops[1].Color = (Color)ColorConverter.ConvertFromString("#94A3B8");
                }
                if (VpnIconGlow != null)
                {
                    VpnIconGlow.Opacity = 0.2;
                }
                break;

            default: // Disconnected, Error
                VpnStatusBadgeBorder.Visibility = Visibility.Collapsed;
                VpnSubLabelTextBlock.Text = "Đang tắt";
                VpnSubLabelTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                if (VpnIconBrush != null && VpnIconBrush.GradientStops.Count >= 2)
                {
                    VpnIconBrush.GradientStops[0].Color = (Color)ColorConverter.ConvertFromString("#0284C7");
                    VpnIconBrush.GradientStops[1].Color = (Color)ColorConverter.ConvertFromString("#38BDF8");
                }
                if (VpnIconGlow != null)
                {
                    VpnIconGlow.Color = (Color)ColorConverter.ConvertFromString("#0284C7");
                    VpnIconGlow.Opacity = 0.3;
                }
                break;
        }
    }

    private async void VpnActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_warpVpnService.IsWarpInstalled())
        {
            var result = MessageBox.Show(
                "Máy trạm chưa có Cloudflare WARP. Bạn có muốn tự động tải và cài đặt ngay không?",
                "Cài đặt VPN",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _ = Task.Run(async () =>
                {
                    await _warpVpnService.TriggerInstallAsync(_vpnSettings);
                });
            }
            return;
        }

        if (_warpVpnService.CurrentStatus == VpnStatus.Connected)
        {
            await _warpVpnService.DisconnectAsync();
        }
        else if (_warpVpnService.CurrentStatus == VpnStatus.Disconnected || _warpVpnService.CurrentStatus == VpnStatus.Error || _warpVpnService.CurrentStatus == VpnStatus.NotInstalled)
        {
            await _warpVpnService.ConnectAsync();
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vpnSettings.AutoDisconnectOnSessionEnd && _warpVpnService != null)
        {
            _ = _warpVpnService.DisconnectAsync();
        }

        if (_isVipSession)
        {
            MessageBox.Show("Hội viên VIP không được sử dụng chức năng đăng xuất.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Application.Current is App app)
        {
            app.RequestLockFromClientUi("Đăng xuất máy");
            return;
        }

        SetMachineState("LOCKED");
    }

    private void PasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsOverlay is not null) MoreActionsOverlay.Visibility = Visibility.Collapsed;
        if (Application.Current is App app)
        {
            app.OpenChangePasswordPanelFromClientUi();
            return;
        }

        MessageBox.Show(
            "Ứng dụng chưa sẵn sàng để đổi mật khẩu.",
            "Mật khẩu",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vpnSettings.AutoDisconnectOnSessionEnd && _warpVpnService != null)
        {
            _ = _warpVpnService.DisconnectAsync();
        }

        if (Application.Current is App app)
        {
            app.RequestManualLockFromClientUi();
            return;
        }
    }

    private void AvatarButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isMemberSession || string.IsNullOrWhiteSpace(MemberUsernameTextBlock.Text))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(CurrentMemberId)) return;

        var window = new AvatarSelectionWindow(CurrentMemberId);
        window.Owner = this;
        window.ShowDialog();
    }

    private void ChangeWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsOverlay is not null) MoreActionsOverlay.Visibility = Visibility.Collapsed;
        if (!_isVipSession || string.IsNullOrWhiteSpace(CurrentMemberId)) return;

        var window = new WallpaperSelectionWindow(CurrentMemberId);
        window.Owner = this;
        window.ShowDialog();
        
        // Apply wallpaper after dialog closes just in case it changed
        _ = ApplyWallpaperAsync(CurrentMemberId);
    }

    private void TogglePromoTimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isMemberSession || string.IsNullOrWhiteSpace(CurrentMemberId) || !TogglePromoTimeButton.IsEnabled) return;

        int firstCardSeconds = _isVipSession
            ? (int)_usedDuration.TotalSeconds
            : ComputeMemberRemainingSecondsRealtime((int)_usedDuration.TotalSeconds);

        var dlg = new PromoTimeRedeemWindow(CurrentMemberId, _isVipSession, firstCardSeconds, _playSeconds)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true && _isVipSession)
        {
            int redeemedSeconds = dlg.RedeemedSeconds;
            if (redeemedSeconds > 0)
            {
                int newUsedSeconds = Math.Max(0, (int)_usedDuration.TotalSeconds - redeemedSeconds);
                SynchronizeUsedDuration(newUsedSeconds);
            }
        }
    }

    public async System.Threading.Tasks.Task ApplyWallpaperAsync(string memberId)
    {
        try
        {
            if (Application.Current is not App app) return;
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("X-API-KEY", "SmbSecureLANKey2026");
            var apiUrl = app.BuildApiUrl($"/members/{memberId}/wallpapers");
            var response = await client.GetAsync(apiUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var data = System.Text.Json.JsonSerializer.Deserialize<WallpaperResponseDto>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (data != null && !string.IsNullOrWhiteSpace(data.CurrentWallpaperId))
                {
                    var wallpaperPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Wallpapers", data.CurrentWallpaperId);
                    if (System.IO.File.Exists(wallpaperPath))
                    {
                        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaperPath, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply wallpaper: {ex.Message}");
        }
    }
    
    private class WallpaperResponseDto
    {
        public string? CurrentWallpaperId { get; set; }
    }

    private string _gameLauncherPath = string.Empty;

    public void SetGameLauncherPath(string? path)
    {
        _gameLauncherPath = path ?? string.Empty;
        var hasValidPath = !string.IsNullOrWhiteSpace(_gameLauncherPath);
        if (GameLauncherActionButton is not null)
        {
            GameLauncherActionButton.Visibility = _isAdminSession ? Visibility.Collapsed : Visibility.Visible;
        }
        UpdateActionButtonsLayout();
    }

    public void UpdateVpnSettings(bool enabled, string? localInstallerPath, string? downloadUrl)
    {
        _vpnSettings.Enabled = enabled;
        if (!string.IsNullOrWhiteSpace(localInstallerPath))
        {
            _vpnSettings.LocalInstallerPath = localInstallerPath.Trim();
        }
        if (!string.IsNullOrWhiteSpace(downloadUrl))
        {
            _vpnSettings.DownloadUrl = downloadUrl.Trim();
        }

        if (VpnActionButton is not null)
        {
            VpnActionButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateActionButtonsLayout();
    }

    private void GameLauncherButton_Click(object sender, RoutedEventArgs e)
    {
        _ = TryLaunchConfiguredGameLauncher(showDialogs: true, out _);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var helper = new WindowInteropHelper(this);
        HwndSource.FromHwnd(helper.Handle)?.AddHook(HwndHook);
        
        // Register Shift (0x0004) + F1 (0x70)
        RegisterHotKey(helper.Handle, HOTKEY_ID, 0x0004, 0x70);
    }
    
    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            ToggleInGameOverlay();
            handled = true;
        }
        return IntPtr.Zero;
    }
    
    private void ToggleInGameOverlay()
    {
        if (MachineStateTextBlock.Text == "LOCKED" || (!_isMemberSession && !_isPostpaidSession))
            return;
            
        if (_overlayWindow == null)
        {
            _overlayWindow = new InGameOverlayWindow(this);
        }
        
        if (_overlayWindow.IsVisible)
        {
            _overlayWindow.Hide();
        }
        else
        {
            _overlayWindow.Show();
            UpdateUsageUi();
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        UnregisterHotKey(helper.Handle, HOTKEY_ID);
        base.OnClosed(e);
    }

    public bool TryLaunchConfiguredGameLauncher(bool showDialogs, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(_gameLauncherPath))
        {
            errorMessage = "Chưa cấu hình đường dẫn Menu Game.";
            if (showDialogs)
            {
                MessageBox.Show(errorMessage, "Menu Game", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        if (!TryResolveGameLauncherExecutablePath(_gameLauncherPath, out var executablePath, out errorMessage))
        {
            if (showDialogs)
            {
                MessageBox.Show(errorMessage, "Menu Game", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(executablePath);
            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            };
            if (!string.IsNullOrWhiteSpace(directory))
            {
                processStartInfo.WorkingDirectory = directory;
            }
            Process.Start(processStartInfo);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Không thể mở Menu Game: {ex.Message}";
            if (showDialogs)
            {
                MessageBox.Show(errorMessage, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }
    }

    private static bool TryResolveGameLauncherExecutablePath(
        string? configuredPath,
        out string executablePath,
        out string errorMessage)
    {
        executablePath = string.Empty;
        errorMessage = "Chưa cấu hình đường dẫn Menu Game.";

        var candidate = (configuredPath ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            if (File.Exists(candidate))
            {
                var fullPath = Path.GetFullPath(candidate);
                if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = $"Đường dẫn Menu Game phải trỏ tới file .exe, hiện tại là: {fullPath}";
                    return false;
                }

                executablePath = fullPath;
                return true;
            }

            if (Directory.Exists(candidate))
            {
                var directExecutable = Path.Combine(candidate, DefaultGameLauncherExecutableName);
                if (File.Exists(directExecutable))
                {
                    executablePath = Path.GetFullPath(directExecutable);
                    return true;
                }

                var exeFiles = Directory.GetFiles(candidate, "*.exe", SearchOption.TopDirectoryOnly);
                if (exeFiles.Length > 0)
                {
                    executablePath = Path.GetFullPath(exeFiles[0]);
                    return true;
                }

                errorMessage =
                    $"Thư mục Menu Game chưa có file thực thi (.exe) nào: {Path.GetFullPath(candidate)}";
                return false;
            }

            errorMessage = $"Không tìm thấy Menu Game tại: {candidate}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Đường dẫn Menu Game không hợp lệ: {ex.Message}";
            return false;
        }
    }



    #region More Actions Modal Handlers
    private void MoreActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsOverlay is null) return;
        MoreActionsOverlay.Visibility = Visibility.Visible;
    }

    private void CloseMoreActionsModal_Click(object sender, RoutedEventArgs e)
    {
        if (MoreActionsOverlay is null) return;
        MoreActionsOverlay.Visibility = Visibility.Collapsed;
    }

    private void MoreActionsOverlay_Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (MoreActionsOverlay is null) return;
        MoreActionsOverlay.Visibility = Visibility.Collapsed;
    }

    private void MoreActionsCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && MoreActionsOverlay is not null && MoreActionsOverlay.Visibility == Visibility.Visible)
        {
            MoreActionsOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }
    #endregion
}
