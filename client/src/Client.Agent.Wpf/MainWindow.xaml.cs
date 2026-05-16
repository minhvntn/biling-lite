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
    private const decimal DefaultHourlyRate = 12_000m;

    private readonly DispatcherTimer _usageTimer = new();

    private bool _allowClose;
    private int _totalSessionMinutes = DefaultTotalSessionMinutes;
    private decimal _hourlyRate = DefaultHourlyRate;
    private decimal _serviceCost;
    private int _serviceOrderCount;
    private bool _isMemberSession;
    private TimeSpan _usedDuration = TimeSpan.Zero;
    private DateTime? _runningStartedAtUtc;

    public MainWindow()
    {
        InitializeComponent();
        ApplyI18nTexts();
        SetLogoutActionVisible(false);

        _usageTimer.Interval = TimeSpan.FromSeconds(1);
        _usageTimer.Tick += UsageTimer_Tick;
        _usageTimer.Start();

        UpdateUsageUi();

        Loaded += MainWindow_Loaded;
        LocationChanged += MainWindow_LocationChanged;
    }

    private void ApplyI18nTexts()
    {
        ConnectionStatusTextBlock.Text = ClientI18n.Get(
            "main.connection.connecting",
            "\u0110ang k\u1ebft n\u1ed1i...");

        StatusIconTextBlock.Text = ClientI18n.Get("main.status.icon", "⚡");
        StatusTitleTextBlock.Text = ClientI18n.Get("main.status.title", "Trạng thái máy");

        TotalTimeLabelTextBlock.Text = ClientI18n.Get("main.metrics.total", "Tổng thời gian");
        UsedTimeLabelTextBlock.Text = ClientI18n.Get("main.metrics.used", "Đã sử dụng");
        RemainingTimeLabelTextBlock.Text = ClientI18n.Get("main.metrics.remaining", "Còn lại");
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

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    public void ConfigureBilling(int totalSessionMinutes, decimal hourlyRate, bool resetUsage = false)
    {
        if (resetUsage)
        {
            _usedDuration = TimeSpan.Zero;
            _runningStartedAtUtc = null;
            _serviceCost = 0;
            _serviceOrderCount = 0;
            UpdateServiceBadgeUi();
        }

        _totalSessionMinutes = Math.Max(1, totalSessionMinutes);
        _hourlyRate = hourlyRate < 0 ? 0 : hourlyRate;
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
        _usedDuration = TimeSpan.FromSeconds(60);
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
        LastCommandTextBlock.Text = $"L\u1ec7nh g\u1ea7n nh\u1ea5t: {value}";
    }

    public void SetMemberInfo(string? username, string? rank)
    {
        _isMemberSession = !string.IsNullOrWhiteSpace(username);
        var isAdminSession = string.Equals(username, "Admin", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(rank, "ADMIN", StringComparison.OrdinalIgnoreCase);
        SetLogoutActionVisible(_isMemberSession || isAdminSession);
        if (string.IsNullOrWhiteSpace(username))
        {
            UserInfoPanel.Visibility = Visibility.Collapsed;
            MemberRankBorder.Visibility = Visibility.Collapsed;
            ApplyRankPulseAnimation(false);
            UpdateUsageUi();
            return;
        }

        UserInfoPanel.Visibility = Visibility.Visible;
        MemberRankBorder.Visibility = Visibility.Visible;
        MemberUsernameTextBlock.Text = username;

        var rankStr = string.IsNullOrWhiteSpace(rank) ? "SAT" : rank;
        var rankUpper = rankStr.ToUpperInvariant();
        var rankNormalized = NormalizeRankKey(rankUpper);
        var rankCompact = rankNormalized.Replace(" ", string.Empty);
        MemberRankTextBlock.Text = rankUpper;

        string bgColor = "#374151";
        string fgColor = "#9CA3AF";
        string rankIcon = "🔰";
        string? rankIconAsset = null;
        var shouldPulse = false;

        if (rankNormalized.Contains("SAT") || rankUpper.Contains("IRON"))
        {
            rankIconAsset = "sat.png";
        }
        else if (rankNormalized.Contains("DONG") || rankUpper.Contains("BRONZE"))
        {
            bgColor = "#78350F";
            fgColor = "#FCD34D";
            rankIconAsset = "dong.png";
            rankIcon = "🥉";
        }
        else if (rankNormalized.Contains("BAC") || rankUpper.Contains("SILVER"))
        {
            bgColor = "#4B5563";
            fgColor = "#E5E7EB";
            rankIconAsset = "bac.png";
            rankIcon = "🥈";
        }
        else if (rankNormalized.Contains("VANG") || rankUpper.Contains("GOLD"))
        {
            bgColor = "#854D0E";
            fgColor = "#FDE047";
            rankIconAsset = "vang.png";
            rankIcon = "🥇";
        }
        else if (rankNormalized.Contains("BACH KIM") || rankUpper.Contains("PLATINUM"))
        {
            bgColor = "#164E63";
            fgColor = "#22D3EE";
            rankIconAsset = "back-kim.png";
            rankIcon = "💠";
        }
        else if (rankNormalized.Contains("TINH ANH") ||
                 rankNormalized.Contains("LUC BAO") ||
                 rankUpper.Contains("EMERALD"))
        {
            bgColor = "#064E3B";
            fgColor = "#34D399";
            rankIconAsset = "tinh-anh.png";
            rankIcon = "💚";
        }
        else if (rankNormalized.Contains("KIM CUONG") || rankUpper.Contains("DIAMOND"))
        {
            bgColor = "#312E81";
            fgColor = "#C7D2FE";
            rankIconAsset = "kim-cuong.png";
            rankIcon = "💎";
            shouldPulse = true;
        }
        else if (rankNormalized.Contains("DAI CAO THU") || rankUpper.Contains("GRANDMASTER"))
        {
            bgColor = "#7F1D1D";
            fgColor = "#FCA5A5";
            rankIconAsset = "dai-cao-thu.png";
            rankIcon = "🛡️";
            shouldPulse = true;
        }
        else if (rankNormalized.Contains("CAO THU") || rankUpper.Contains("MASTER"))
        {
            bgColor = "#701A75";
            fgColor = "#F0ABFC";
            rankIconAsset = "cao-thu.png";
            rankIcon = "👑";
            shouldPulse = true;
        }
        else if (rankNormalized.Contains("THACH DAU") ||
                 rankCompact.Contains("THACHDAU") ||
                 rankUpper.Contains("CHALLENGER"))
        {
            bgColor = "#1E3A8A";
            fgColor = "#FDE047";
            rankIconAsset = "thach-dau.png";
            rankIcon = "🏆";
            shouldPulse = true;
        }
        else if (rankUpper.Contains("VIP"))
        {
            bgColor = "#831843";
            fgColor = "#F9A8D4";
            rankIcon = "✨";
        }

        var bc = new BrushConverter();
        var rankAccentBrush = (Brush)bc.ConvertFromString(fgColor)!;
        MemberRankBorder.Background = (Brush)bc.ConvertFromString("#0D1B3D")!;
        MemberRankBorder.BorderBrush = rankAccentBrush;
        MemberRankTextBlock.Foreground = rankAccentBrush;
        MemberRankIconTextBlock.Foreground = rankAccentBrush;
        MemberRankIconBadgeBorder.Background = (Brush)bc.ConvertFromString(bgColor)!;
        MemberRankIconBadgeBorder.BorderBrush = (Brush)bc.ConvertFromString(bgColor)!;
        ApplyRankIcon(rankIconAsset, rankIcon);
        if (MemberRankBorder.Effect is DropShadowEffect glow)
        {
            var glowColor = (Color)ColorConverter.ConvertFromString(fgColor);
            glowColor.A = 120;
            glow.Color = glowColor;
        }
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
        if (MemberRankBorder.Effect is not DropShadowEffect glow)
        {
            return;
        }

        if (!shouldPulse)
        {
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            glow.Opacity = 0.65;
            return;
        }

        var pulseAnimation = new DoubleAnimation
        {
            From = 0.45,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(900),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, pulseAnimation);
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
    }

    public void SetWithdrawActionVisible(bool visible)
    {
        if (WithdrawActionButton is null)
        {
            return;
        }

        WithdrawActionButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetTopupRequestActionVisible(bool visible)
    {
        if (TopupRequestActionButton is null)
        {
            return;
        }

        TopupRequestActionButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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
        var total = TimeSpan.FromMinutes(Math.Max(1, _totalSessionMinutes));
        var used = GetCurrentUsedDuration();
        var totalMins = (int)total.TotalMinutes;
        var elapsedSeconds = Math.Max(0, (int)used.TotalSeconds);
        var hasSessionUsage = _runningStartedAtUtc is not null || _usedDuration > TimeSpan.Zero;
        var usedMins = hasSessionUsage
            ? Math.Max(1, (int)Math.Ceiling(elapsedSeconds / 60.0))
            : 0;

        return Math.Max(0, totalMins - usedMins);
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
        var usedMins = hasSessionUsage
            ? Math.Max(1, (int)Math.Ceiling(elapsedSeconds / 60.0))
            : 0;
        var remainingMins = Math.Max(0, totalMins - usedMins);

        // Match backend logic: use billable minutes (rounded up) and apply pricing step and minimum charge
        var billableMins = hasSessionUsage
            ? Math.Max(1, (int)Math.Ceiling(elapsedSeconds / 60.0))
            : 0;
        var rawCost = billableMins * (_hourlyRate / 60m);
        var step = PricingStep > 0 ? PricingStep : 1000m;
        var roundedCost = Math.Ceiling(rawCost / step) * step;
        var gameCost = billableMins <= 0
            ? 0m
            : Math.Max(MinimumCharge > 0 ? MinimumCharge : 1000m, roundedCost);

        TotalTimeValueTextBlock.Text = FormatMinutes(totalMins);
        UsedTimeValueTextBlock.Text = FormatMinutes(usedMins);
        RemainingTimeValueTextBlock.Text = FormatMinutes(remainingMins);
        
        if (_isMemberSession)
        {
            GameCostValueTextBlock.Text = "-";
            ServiceCostValueTextBlock.Text = "-";
            return;
        }

        GameCostValueTextBlock.Text = gameCost.ToString("N0", CultureInfo.InvariantCulture);
        ServiceCostValueTextBlock.Text = _serviceCost.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatMinutes(int totalMinutes)
    {
        var hours = totalMinutes / 60;
        var mins = totalMinutes % 60;
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