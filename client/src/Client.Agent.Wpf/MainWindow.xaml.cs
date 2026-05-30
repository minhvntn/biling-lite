using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using Client.Agent.Wpf.Localization;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Client.Agent.Wpf;

public partial class MainWindow : Window
{
    public static decimal PricingStep { get; set; } = 1000m;
    public static decimal MinimumCharge { get; set; } = 1000m;
    private const int DefaultTotalSessionMinutes = 60_000; // 1000 giờ
    public event EventHandler? TopupRequestRequested;

    public event EventHandler? TimeExpired;

    private const decimal DefaultHourlyRate = 12_000m;

    private readonly DispatcherTimer _usageTimer = new();

    private bool _allowClose;
    private int _totalSessionMinutes = DefaultTotalSessionMinutes;
    private decimal _hourlyRate = DefaultHourlyRate;
    private decimal _serviceCost;
    private int _serviceOrderCount;
    private int _phaseStartedElapsedSeconds;
    private int _lastSyncElapsedSeconds;
    private decimal _lastSyncMemberBalance;
    private bool _isMemberSession;
    private bool _isVipSession;
    private bool _isAdminSession;
    private bool _isPostpaidSession;
    private decimal _memberBalance;
    private int _playSeconds;
    private bool _withdrawActionEnabledSetting = true;
    private bool _topupActionEnabledSetting = true;
    private TimeSpan _usedDuration = TimeSpan.Zero;
    private DateTime? _runningStartedAtUtc;
    private int _fastSyncTickCount = 0;

    public MainWindow()
    {
        InitializeComponent();
        ApplyI18nTexts();
        SetLogoutActionVisible(false);

        _usageTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _usageTimer.Tick += UsageTimer_Tick;
        _usageTimer.Start();

        UpdateUsageUi();

        Loaded += MainWindow_Loaded;
        LocationChanged += MainWindow_LocationChanged;

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

        FooterTitleTextBlock.Text = ClientI18n.Get("main.footer.title", "Loyalty Program");
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

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = !_isCollapsed;
        var visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        
        ActionButtonsGrid.Visibility = visibility;
        PromotionsContainer.Visibility = visibility;
        FooterContainer.Visibility = visibility;
        
        if (_isCollapsed)
        {
            CollapseButtonText.Text = "Mở rộng";
            CollapseButtonIcon.Text = "\uE76B"; // ChevronRight
            LeftColumnGrid.Margin = new Thickness(0);
            LeftColumnGrid.HorizontalAlignment = HorizontalAlignment.Center;
            LeftColumnGrid.SetValue(System.Windows.Controls.Grid.ColumnSpanProperty, 2);
            
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
            LeftColumnGrid.Margin = new Thickness(0, 0, 15, 0);
            LeftColumnGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            LeftColumnGrid.SetValue(System.Windows.Controls.Grid.ColumnSpanProperty, 1);
            
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

    public void ConfigureBilling(int totalSessionMinutes, decimal hourlyRate, bool resetUsage = false, decimal? memberBalance = null, bool isPostpaid = false, int playSeconds = 0)
    {
        var elapsedSeconds = Math.Max(0, (int)GetCurrentUsedDuration().TotalSeconds);

        if (resetUsage)
        {
            _usedDuration = TimeSpan.Zero;
            _runningStartedAtUtc = null;
            _serviceCost = 0;
            _serviceOrderCount = 0;
            _phaseStartedElapsedSeconds = 0;
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

        UpdateUsageUi();
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

    public void SetLoyaltyProgress(int points, double progressMinutes, int minutesPerPoint)
    {
        MemberPointsTextBlock.Text = $"Điểm: {points:N0} | {progressMinutes:0.##}/{minutesPerPoint} phút";
        MemberPointsTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8"));

        LoyaltyProgressContainer.Visibility = Visibility.Visible;
        FooterTitleTextBlock.Text = "Loyalty Program";

        if (minutesPerPoint > 0)
        {
            double pct = Math.Clamp(progressMinutes / minutesPerPoint, 0.0, 1.0);
            LoyaltyProgressFilled.Width = new GridLength(pct, GridUnitType.Star);
            LoyaltyProgressEmpty.Width = new GridLength(1.0 - pct, GridUnitType.Star);
        }
    }

    public void SetMemberInfo(string? username, string? rank, string? memberType = null)
    {
        _isMemberSession = !string.IsNullOrWhiteSpace(username);
        _isAdminSession = string.Equals(username, "Admin", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(rank, "ADMIN", StringComparison.OrdinalIgnoreCase);
        _isVipSession = string.Equals(memberType, "VIP", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rank, "VIP", StringComparison.OrdinalIgnoreCase);
        UpdateExternalInfoVisibility();
        SetLogoutActionVisible((_isMemberSession || _isAdminSession) && !_isVipSession);
        SetTransferActionVisible(_isMemberSession && !_isAdminSession);
        SetWithdrawActionVisible(_withdrawActionEnabledSetting);
        SetTopupRequestActionVisible(_topupActionEnabledSetting);
        LoyaltyActionButton.Visibility = _isMemberSession ? Visibility.Visible : Visibility.Collapsed;
        PasswordActionButton.Visibility = _isMemberSession ? Visibility.Visible : Visibility.Collapsed;
        FooterContainer.Visibility = _isMemberSession ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButtonsLayout();
        if (string.IsNullOrWhiteSpace(username))
        {
            UserInfoPanel.Visibility = Visibility.Collapsed;
            MemberRankContainer.Visibility = Visibility.Collapsed;
            if (VipIconImage is not null)
            {
                VipIconImage.Visibility = Visibility.Collapsed;
            }
            ApplyRankPulseAnimation(false);
            UpdateUsageUi();
            return;
        }

        UserInfoPanel.Visibility = Visibility.Visible;
        MemberRankContainer.Visibility = Visibility.Visible;
        MemberUsernameTextBlock.Text = username;
        
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
        string fgColor = "#475569";
        string rankIcon = "🔰";
        string? rankIconAsset = null;
        var shouldPulse = false;

        if (rankNormalized.Contains("SAT") || rankUpper.Contains("IRON"))
        {
            rankIconAsset = "sat.png";
        }
        else if (rankNormalized.Contains("DONG") || rankUpper.Contains("BRONZE"))
        {
            bgColor = "#FEF3C7";
            fgColor = "#B45309";
            rankIconAsset = "dong.png";
            rankIcon = "🥉";
        }
        else if (rankNormalized.Contains("BAC") || rankUpper.Contains("SILVER"))
        {
            bgColor = "#F3F4F6";
            fgColor = "#4B5563";
            rankIconAsset = "bac.png";
            rankIcon = "🥈";
        }
        else if (rankNormalized.Contains("VANG") || rankUpper.Contains("GOLD"))
        {
            bgColor = "#FEF08A";
            fgColor = "#A16207";
            rankIconAsset = "vang.png";
            rankIcon = "🥇";
        }
        else if (rankNormalized.Contains("BACH KIM") || rankUpper.Contains("PLATINUM"))
        {
            bgColor = "#CFFAFE";
            fgColor = "#0E7490";
            rankIconAsset = "back-kim.png";
            rankIcon = "💠";
        }
        else if (rankNormalized.Contains("TINH ANH") ||
                 rankNormalized.Contains("LUC BAO") ||
                 rankUpper.Contains("EMERALD"))
        {
            bgColor = "#D1FAE5";
            fgColor = "#047857";
            rankIconAsset = "tinh-anh.png";
            rankIcon = "💚";
        }
        else if (rankNormalized.Contains("KIM CUONG") || rankUpper.Contains("DIAMOND"))
        {
            bgColor = "#E0E7FF";
            fgColor = "#4338CA";
            rankIconAsset = "kim-cuong.png";
            rankIcon = "💎";
            shouldPulse = true;
        }
        else if (rankNormalized.Contains("DAI CAO THU") || rankUpper.Contains("GRANDMASTER"))
        {
            bgColor = "#FEE2E2";
            fgColor = "#B91C1C";
            rankIconAsset = "dai-cao-thu.png";
            rankIcon = "🛡️";
            shouldPulse = true;
        }
        else if (rankNormalized.Contains("CAO THU") || rankUpper.Contains("MASTER"))
        {
            bgColor = "#FAE8FF";
            fgColor = "#A21CAF";
            rankIconAsset = "cao-thu.png";
            rankIcon = "👑";
            shouldPulse = true;
        }
        else if (rankNormalized.Contains("THACH DAU") ||
                 rankCompact.Contains("THACHDAU") ||
                 rankUpper.Contains("CHALLENGER"))
        {
            bgColor = "#DBEAFE";
            fgColor = "#1D4ED8";
            rankIconAsset = "thach-dau.png";
            rankIcon = "🏆";
            shouldPulse = true;
        }
        else if (rankUpper.Contains("VIP"))
        {
            bgColor = "#FCE7F3";
            fgColor = "#BE185D";
            rankIcon = "✨";
        }

        var bc = new BrushConverter();
        var rankAccentBrush = (Brush)bc.ConvertFromString(fgColor)!;
        MemberRankBorder.Background = (Brush)bc.ConvertFromString("#60FFFFFF")!;
        MemberRankBorder.BorderBrush = rankAccentBrush;
        MemberRankTextBlock.Foreground = rankAccentBrush;
        MemberRankIconTextBlock.Foreground = rankAccentBrush;
        MemberRankIconBadgeBorder.Background = (Brush)bc.ConvertFromString(bgColor)!;
        MemberRankIconBadgeBorder.BorderBrush = (Brush)bc.ConvertFromString(bgColor)!;
        ApplyRankIcon(rankIconAsset, rankIcon);
        // Shadow effect removed to fix text blur
        ApplyRankPulseAnimation(shouldPulse);
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

            var fileUri = new Uri(iconPath, UriKind.Absolute);
            return new BitmapImage(fileUri);
        }
        catch
        {
            return null;
        }
    }

    private void ApplyRankPulseAnimation(bool shouldPulse)
    {
        // Pulse animation removed due to shadow effect removal
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

        LogoutActionButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButtonsLayout();
    }

    public void SetTransferActionVisible(bool visible)
    {
        if (TransferActionButton is null)
        {
            return;
        }

        TransferActionButton.Visibility = (visible && !_isVipSession) ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButtonsLayout();
    }

    public void SetWithdrawActionVisible(bool visible)
    {
        _withdrawActionEnabledSetting = visible;
        if (WithdrawActionButton is null)
        {
            return;
        }

        WithdrawActionButton.Visibility = (visible && _isMemberSession && !_isVipSession) ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButtonsLayout();
    }

    public void SetTopupRequestActionVisible(bool visible)
    {
        _topupActionEnabledSetting = visible;
        if (TopupRequestActionButton is null)
        {
            return;
        }

        TopupRequestActionButton.Visibility = (visible && _isMemberSession && !_isVipSession) ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButtonsLayout();
    }

    private void UpdateActionButtonsLayout()
    {
        if (ActionButtonsGrid is null) return;
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
        if (_isMemberSession && !_isVipSession)
        {
            var elapsedNow = Math.Max(0, (int)GetCurrentUsedDuration().TotalSeconds);
            var remainingRawSeconds = ComputeMemberRemainingSecondsRealtime(elapsedNow);
            return remainingRawSeconds <= 0
                ? 0
                : Math.Max(1, (int)Math.Ceiling(remainingRawSeconds / 60.0));
        }

        var total = TimeSpan.FromMinutes(Math.Max(1, _totalSessionMinutes));
        var used = GetCurrentUsedDuration();
        var totalMins = (int)total.TotalMinutes;
        var elapsedSeconds = Math.Max(0, (int)used.TotalSeconds);
        var hasSessionUsage = _runningStartedAtUtc is not null || _usedDuration > TimeSpan.Zero;
        var usedMins = hasSessionUsage
            ? Math.Max(0, (int)Math.Floor(elapsedSeconds / 60.0))
            : 0;

        return Math.Max(0, totalMins - usedMins);
    }

    private int ComputeRoundedMemberRemainingMinutesFromAccount()
    {
        var pricePerMinute = _hourlyRate > 0 ? (_hourlyRate / 60m) : 0m;
        var balanceSeconds = pricePerMinute > 0
            ? (_memberBalance / pricePerMinute) * 60m
            : 0m;
        var totalRemainingSeconds = Math.Max(0m, balanceSeconds) + Math.Max(0, _playSeconds);
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
        var remainingSecondsAtSync = Math.Max(0m, balanceSecondsAtSync) + Math.Max(0, _playSeconds);
        var elapsedSinceSync = Math.Max(0, elapsedSecondsNow - _lastSyncElapsedSeconds);
        var remainingRawSeconds = remainingSecondsAtSync - elapsedSinceSync;
        return Math.Max(0, (int)Math.Floor((double)remainingRawSeconds));
    }

    private void UpdateUsageUi()
    {
        var total = TimeSpan.FromMinutes(Math.Max(1, _totalSessionMinutes));
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
            
            if (_isVipSession)
            {
                totalMins = 60000; // 1000 hours
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
            if (_isMemberSession && !_isVipSession)
            {
                totalMins = Math.Max(1, _totalSessionMinutes);
                totalSecs = totalMins * 60;
                remainingMins = ComputeRoundedMemberRemainingMinutesFromAccount();
                remainingSecs = remainingMins * 60;
                remainingRawSecsForProgress = remainingSecs;
            }
            else if (_isVipSession)
            {
                totalMins = 60000;
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
        var gameCost = billableMins <= 0
            ? 0m
            : (_isMemberSession ? roundedCost : Math.Max(MinimumCharge > 0 ? MinimumCharge : 1000m, roundedCost));

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
        RemainingTimeValueTextBlock.Text = FormatSeconds(displayRemainingSecs);

        bool showTotalTime = (_isMemberSession && !_isVipSession && !_isAdminSession) || 
                             (!_isMemberSession && !_isPostpaidSession && !_isAdminSession);
        var totalTimeVisibility = showTotalTime ? Visibility.Visible : Visibility.Collapsed;
        TotalTimeContainer.Visibility = totalTimeVisibility;

        double percentage = 1.0;
        if (_isMemberSession && !_isVipSession)
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
            if (_isVipSession)
            {
                GameCostValueTextBlock.Text = gameCost.ToString("N0", CultureInfo.InvariantCulture);
            }
            else
            {
                GameCostValueTextBlock.Text = "-";
            }
            ServiceCostValueTextBlock.Text = _serviceCost > 0 ? _serviceCost.ToString("N0", CultureInfo.InvariantCulture) : "-";
            return;
        }

        GameCostValueTextBlock.Text = gameCost.ToString("N0", CultureInfo.InvariantCulture);
        ServiceCostValueTextBlock.Text = _serviceCost.ToString("N0", CultureInfo.InvariantCulture);
    }

    private void UpdateProgressArc(double percentage)
    {
        if (ProgressArcPath == null) return;
        
        percentage = Math.Max(0, Math.Min(1, percentage));
        
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

    public void UpdatePromotion(string? promotionName, decimal discountPercent)
    {
        if (string.IsNullOrWhiteSpace(promotionName))
        {
            PromotionBannerBorder.Visibility = Visibility.Collapsed;
            GuestPromotionBannerBorder.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (!_isMemberSession)
            {
                PromotionBannerBorder.Visibility = Visibility.Collapsed;
                GuestPromotionBannerBorder.Visibility = Visibility.Visible;
            }
            else
            {
                PromotionNameTextBlock.Text = promotionName;
                PromotionDiscountTextBlock.Text = $"Giảm {discountPercent:0.#}% tiền giờ chơi";
                PromotionBannerBorder.Visibility = Visibility.Visible;
                GuestPromotionBannerBorder.Visibility = Visibility.Collapsed;
            }
        }
        UpdatePromotionsLayout();
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

    private void UpdatePromotionsLayout()
    {
        int visibleCount = 0;
        if (PromotionBannerBorder.Visibility == Visibility.Visible) visibleCount++;
        if (LoyaltyPromotionBannerBorder.Visibility == Visibility.Visible) visibleCount++;
        if (GuestPromotionBannerBorder.Visibility == Visibility.Visible) visibleCount++;
        
        PromotionsContainer.Columns = visibleCount >= 2 ? 2 : 1;
        
        if (visibleCount >= 2)
        {
            PromotionBannerBorder.Margin = new Thickness(0, 0, 4, 8);
            GuestPromotionBannerBorder.Margin = new Thickness(0, 0, 4, 8);
            LoyaltyPromotionBannerBorder.Margin = new Thickness(4, 0, 0, 8);
        }
        else
        {
            PromotionBannerBorder.Margin = new Thickness(0, 0, 0, 8);
            GuestPromotionBannerBorder.Margin = new Thickness(0, 0, 0, 8);
            LoyaltyPromotionBannerBorder.Margin = new Thickness(0, 0, 0, 8);
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


    private void MessagesButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Hi\u1ec7n ch\u01b0a c\u00f3 tin nh\u1eafn m\u1edbi t\u1eeb qu\u1ea3n tr\u1ecb.",
            "Tin nh\u1eafn",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
            "\u1ee8ng d\u1ee5ng ch\u01b0a s\u1eb5n s\u00e0ng \u0111\u1ec3 ki\u1ec3m tra \u0111i\u1ec3m t\u00edch l\u0169y.",
            "\u0110i\u1ec3m t\u00edch l\u0169y",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TransferBalanceButton_Click(object sender, RoutedEventArgs e)
    {
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

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
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
        if (Application.Current is App app)
        {
            app.RequestManualLockFromClientUi();
            return;
        }
    }
}
