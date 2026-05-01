using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Client.Agent.Wpf;

public partial class MainWindow : Window
{
    private const int DefaultTotalSessionMinutes = 60_000; // 1000 giờ
    private const decimal DefaultHourlyRate = 12_000m;

    private readonly DispatcherTimer _usageTimer = new();

    private bool _allowClose;
    private int _totalSessionMinutes = DefaultTotalSessionMinutes;
    private decimal _hourlyRate = DefaultHourlyRate;
    private decimal _serviceCost;
    private TimeSpan _usedDuration = TimeSpan.Zero;
    private DateTime? _runningStartedAtUtc;

    public MainWindow()
    {
        InitializeComponent();

        _usageTimer.Interval = TimeSpan.FromSeconds(1);
        _usageTimer.Tick += UsageTimer_Tick;
        _usageTimer.Start();

        UpdateUsageUi();
    }

    public void ConfigureBilling(int totalSessionMinutes, decimal hourlyRate, bool resetUsage = false)
    {
        if (resetUsage)
        {
            _usedDuration = TimeSpan.Zero;
            _runningStartedAtUtc = null;
            _serviceCost = 0;
        }

        _totalSessionMinutes = Math.Max(1, totalSessionMinutes);
        _hourlyRate = hourlyRate < 0 ? 0 : hourlyRate;
        UpdateUsageUi();
    }

    public void SetServiceCost(decimal amount)
    {
        _serviceCost = amount < 0 ? 0 : amount;
        UpdateUsageUi();
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
            ConnectionStatusTextBlock.Text = "Đã kết nối";
            ConnectionIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
            return;
        }

        if (normalized.StartsWith("Reconnecting", StringComparison.OrdinalIgnoreCase))
        {
            ConnectionStatusTextBlock.Text = normalized;
            ConnectionIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            return;
        }

        ConnectionStatusTextBlock.Text = "Mất kết nối";
        ConnectionIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
    }

    public void SetMachineState(string state)
    {
        var code = (state ?? string.Empty).Trim().ToUpperInvariant();
        var now = DateTime.Now.ToString("HH:mm:ss");

        switch (code)
        {
            case "IN_USE":
                ResumeSession();
                MachineStateTextBlock.Text = "Đang sử dụng";
                MachineStateTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                LastCommandTextBlock.Text = $"Lệnh gần nhất: Mở máy ({now})";
                break;

            case "PAUSED":
                PauseSession();
                MachineStateTextBlock.Text = "Tạm nghỉ";
                MachineStateTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
                LastCommandTextBlock.Text = $"Lệnh gần nhất: Tạm nghỉ ({now})";
                break;

            case "ONLINE":
                EndSession(resetUsage: true);
                MachineStateTextBlock.Text = "Sẵn sàng";
                MachineStateTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8"));
                LastCommandTextBlock.Text = $"Lệnh gần nhất: Sẵn sàng ({now})";
                break;

            case "LOCKED":
                EndSession(resetUsage: true);
                MachineStateTextBlock.Text = "Đã khóa";
                MachineStateTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                LastCommandTextBlock.Text = $"Lệnh gần nhất: Khóa máy ({now})";
                break;

            default:
                EndSession(resetUsage: false);
                MachineStateTextBlock.Text = "Ngoại tuyến";
                MachineStateTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
                LastCommandTextBlock.Text = $"Lệnh gần nhất: {code} ({now})";
                break;
        }

        UpdateUsageUi();
    }

    public void SetLastCommand(string command)
    {
        var value = string.IsNullOrWhiteSpace(command) ? "-" : command;
        LastCommandTextBlock.Text = $"Lệnh gần nhất: {value}";
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

    private void UpdateUsageUi()
    {
        var total = TimeSpan.FromMinutes(Math.Max(1, _totalSessionMinutes));
        var used = GetCurrentUsedDuration();
        var remaining = total - used;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var gameCost = Math.Round((decimal)used.TotalHours * _hourlyRate, 0, MidpointRounding.AwayFromZero);

        TotalTimeValueTextBlock.Text = FormatDuration(total);
        UsedTimeValueTextBlock.Text = FormatDuration(used);
        RemainingTimeValueTextBlock.Text = FormatDuration(remaining);
        GameCostValueTextBlock.Text = gameCost.ToString("N0", CultureInfo.InvariantCulture);
        ServiceCostValueTextBlock.Text = _serviceCost.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var totalHours = (int)value.TotalHours;
        return $"{totalHours:00}:{value.Minutes:00}";
    }

    private void MessagesButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Hiện chưa có tin nhắn mới từ quản trị.",
            "Tin nhắn",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ServicesButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Tính năng gọi dịch vụ sẽ được bật ở phase bán hàng.\nHiện tại chi phí dịch vụ đang là 0.",
            "Dịch vụ",
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

    private void TransferBalanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.OpenTransferBalancePanelFromClientUi();
            return;
        }

        MessageBox.Show(
            "Ứng dụng chưa sẵn sàng để chuyển tiền hội viên.",
            "Chuyển tiền",
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
        MessageBox.Show(
            "Tính năng đổi mật khẩu hội viên sẽ bổ sung ở phase hội viên nâng cao.",
            "Mật khẩu",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.RequestLockFromClientUi("Khóa máy thủ công");
            return;
        }

        SetMachineState("LOCKED");
    }
}
