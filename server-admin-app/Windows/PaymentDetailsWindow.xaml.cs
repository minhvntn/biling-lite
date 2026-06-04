using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Server.Admin.App.Windows;

public partial class PaymentDetailsWindow : Window, INotifyPropertyChanged
{
    private readonly MachineRow _machine;
    private readonly PricingSettingsResponse? _pricingSettings;
    private readonly HttpClient _httpClient;
    private readonly Func<string, MachineRow, bool, Task> _sendCommandAsync;
    private readonly Func<string, string> _buildApiUrl;
    
    private MachineRow _machineSnapshot;
    private int _elapsedAnchorSeconds;
    private DateTime _elapsedAnchorAt;
    private DispatcherTimer? _popupSyncTimer;
    private List<TimeBasedPromotionDto> _allPromotions = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private Visibility _expandedVisibility = Visibility.Collapsed;
    public Visibility ExpandedVisibility
    {
        get => _expandedVisibility;
        set { _expandedVisibility = value; OnPropertyChanged(); }
    }

    public double ExpandAngle => ExpandedVisibility == Visibility.Visible ? 180 : 0;
    
    public string MachineName => _machineSnapshot.Name;
    public string StatusText => string.IsNullOrWhiteSpace(_machineSnapshot.StatusText) ? "-" : _machineSnapshot.StatusText;
    public SolidColorBrush StatusColor => new SolidColorBrush(_machineSnapshot.StatusCode?.Trim().ToUpperInvariant() == "ONLINE" ? Color.FromRgb(34, 197, 94) : Color.FromRgb(156, 163, 175));

    private string _startedAt = "-";
    public string StartedAt { get => _startedAt; set { _startedAt = value; OnPropertyChanged(); } }

    private string _playDuration = "00:00";
    public string PlayDuration { get => _playDuration; set { _playDuration = value; OnPropertyChanged(); } }

    private string _sessionRate = "0";
    public string SessionRate { get => _sessionRate; set { _sessionRate = value; OnPropertyChanged(); } }

    private string _playAmountText = "0 VND";
    public string PlayAmountText { get => _playAmountText; set { _playAmountText = value; OnPropertyChanged(); } }

    private string _totalServiceAmountText = "0 VND";
    public string TotalServiceAmountText { get => _totalServiceAmountText; set { _totalServiceAmountText = value; OnPropertyChanged(); } }

    private string _groupName = "-";
    public string GroupName { get => _groupName; set { _groupName = value; OnPropertyChanged(); } }

    private string _currentRate = "0";
    public string CurrentRate { get => _currentRate; set { _currentRate = value; OnPropertyChanged(); } }

    private string _currentRatePlayAmountText = "0 VND";
    public string CurrentRatePlayAmountText { get => _currentRatePlayAmountText; set { _currentRatePlayAmountText = value; OnPropertyChanged(); } }

    private string _clientServiceAmountText = "0 VND";
    public string ClientServiceAmountText { get => _clientServiceAmountText; set { _clientServiceAmountText = value; OnPropertyChanged(); } }

    private string _serverServiceAmountText = "0 VND";
    public string ServerServiceAmountText { get => _serverServiceAmountText; set { _serverServiceAmountText = value; OnPropertyChanged(); } }

    private string _totalAmountValue = "0";
    public string TotalAmountValue { get => _totalAmountValue; set { _totalAmountValue = value; OnPropertyChanged(); } }

    public bool IsVipSession => _machineSnapshot.IsVipSession;

    public ObservableCollection<BillingTimeBlockViewModel> BreakdownBlocks { get; } = new();

    public PaymentDetailsWindow(
        MachineRow machine,
        PricingSettingsResponse? pricingSettings,
        HttpClient httpClient,
        List<TimeBasedPromotionDto> allPromotions,
        Func<string, string> buildApiUrl,
        Func<string, MachineRow, bool, Task> sendCommandAsync)
    {
        InitializeComponent();
        DataContext = this;

        _machine = machine;
        _machineSnapshot = machine;
        _pricingSettings = pricingSettings;
        _httpClient = httpClient;
        _allPromotions = allPromotions ?? new();
        _buildApiUrl = buildApiUrl;
        _sendCommandAsync = sendCommandAsync;

        _elapsedAnchorSeconds = machine.ActiveSessionElapsedSeconds;
        _elapsedAnchorAt = DateTime.Now;

        Loaded += PaymentDetailsWindow_Loaded;
        Unloaded += PaymentDetailsWindow_Unloaded;
    }

    private JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    private async void PaymentDetailsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RenderBillingDetailsAsync();

        _popupSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _popupSyncTimer.Tick += async (_, _) =>
        {
            // Update elapsed extra seconds directly instead of waiting for MainWindow poll
            await RenderBillingDetailsAsync();
        };
        _popupSyncTimer.Start();
    }

    private void PaymentDetailsWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        _popupSyncTimer?.Stop();
        _popupSyncTimer = null;
    }

    public void SyncMachineSnapshot(MachineRow latest)
    {
        if (latest == null || ReferenceEquals(latest, _machineSnapshot)) return;
        _machineSnapshot = latest;
        _elapsedAnchorSeconds = latest.ActiveSessionElapsedSeconds;
        _elapsedAnchorAt = DateTime.Now;
        _ = RenderBillingDetailsAsync();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void CheckoutButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        await _sendCommandAsync("lock", _machine, true);
    }

    private void ToggleExpandButton_Click(object sender, RoutedEventArgs e)
    {
        ExpandedVisibility = ExpandedVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        OnPropertyChanged(nameof(ExpandAngle));
    }

    private bool ShouldAdvanceElapsed()
    {
        if (string.IsNullOrWhiteSpace(_machineSnapshot.ActiveSessionId))
        {
            return false;
        }
        var status = _machineSnapshot.StatusCode?.Trim().ToUpperInvariant() ?? string.Empty;
        return status != "OFFLINE" && status != "LOCKED";
    }

    private int ResolveDisplayedElapsedSeconds()
    {
        if (!ShouldAdvanceElapsed())
        {
            return _machineSnapshot.ActiveSessionElapsedSeconds;
        }

        var elapsedExtra = Math.Max(0, (int)Math.Floor((DateTime.Now - _elapsedAnchorAt).TotalSeconds));
        return Math.Max(_machineSnapshot.ActiveSessionElapsedSeconds, _elapsedAnchorSeconds + elapsedExtra);
    }

    private async Task RenderBillingDetailsAsync()
    {
        var hasSession = !string.IsNullOrWhiteSpace(_machineSnapshot.ActiveSessionId);
        var displayedElapsedSeconds = hasSession ? ResolveDisplayedElapsedSeconds() : 0;
        
        PlayDuration = hasSession ? FormatUsed(displayedElapsedSeconds) : "00:00";
        StartedAt = string.IsNullOrWhiteSpace(_machineSnapshot.StartedAtText) ? "-" : _machineSnapshot.StartedAtText;
        GroupName = string.IsNullOrWhiteSpace(_machineSnapshot.GroupName) ? "-" : _machineSnapshot.GroupName;
        OnPropertyChanged(nameof(ExpandedVisibility));
        OnPropertyChanged(nameof(ExpandAngle));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(MachineName));
        OnPropertyChanged(nameof(IsVipSession));

        var groups = _pricingSettings?.Groups?.ToList() ?? new List<PricingGroupItem>();
        var defaultGroup = groups.FirstOrDefault(x => x.IsDefault) ?? new PricingGroupItem
        {
            Id = _pricingSettings?.DefaultGroupId ?? "default",
            HourlyRate = _pricingSettings?.DefaultRatePerHour > 0 ? _pricingSettings.DefaultRatePerHour : 5000,
            MemberHourlyRate = _pricingSettings?.DefaultMemberRatePerHour > 0
                ? _pricingSettings.DefaultMemberRatePerHour
                : (_pricingSettings?.DefaultRatePerHour > 0 ? _pricingSettings.DefaultRatePerHour : 5000),
            IsDefault = true,
        };
        var groupById = groups.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        
        var machineGroup = defaultGroup;
        if (!string.IsNullOrWhiteSpace(_machineSnapshot.GroupId) && groupById.TryGetValue(_machineSnapshot.GroupId, out var g))
        {
            machineGroup = g;
        }

        var baseHourlyRate = _machineSnapshot.IsVipSession
            ? (machineGroup.MemberHourlyRate > 0 ? machineGroup.MemberHourlyRate : machineGroup.HourlyRate)
            : machineGroup.HourlyRate;

        SessionRate = $"{baseHourlyRate:N0}";
        CurrentRate = $"{_machineSnapshot.HourlyRate:N0}";

        var playAmount = _machineSnapshot.ActiveSessionEstimatedAmount;
        var playAmountToPay = playAmount;

        if (_machineSnapshot.ActiveMemberId != null)
        {
            if (_machineSnapshot.ActiveMemberBalance < 0)
            {
                var rawDebt = Math.Abs(_machineSnapshot.ActiveMemberBalance);
                playAmountToPay = Math.Ceiling(rawDebt / 500m) * 500m;
            }
            else
            {
                playAmountToPay = 0m;
            }
        }
        else if (_machineSnapshot.IsGuestSession)
        {
            playAmountToPay = Math.Max(0m, playAmount - _machineSnapshot.ActiveGuestPrepaidAmount);
        }

        var currentRatePlayAmount = CalculatePrecisePlayAmount(displayedElapsedSeconds, _machineSnapshot.HourlyRate);
        
        decimal serviceAmount = 0m;
        decimal clientServiceAmount = 0m;
        decimal serverServiceAmount = 0m;

        try
        {
            // Simple fetch for service amount snapshot
            var serviceRes = await _httpClient.GetFromJsonAsync<PcServiceOrdersResponse>(
                _buildApiUrl($"/pcs/{_machineSnapshot.Id}/service-orders/pending"),
                JsonOptions());
            
            if (serviceRes != null)
            {
                serviceAmount = serviceRes.Items.Sum(x => x.LineTotal);
                clientServiceAmount = serviceRes.Items.Sum(x => x.LineTotal); // Assuming all from client for now, or need full split logic
                serverServiceAmount = Math.Max(0m, serviceAmount - clientServiceAmount);
            }
        }
        catch { }

        TotalServiceAmountText = $"{serviceAmount:N0} VND";
        ClientServiceAmountText = $"{clientServiceAmount:N0} VND";
        ServerServiceAmountText = $"{serverServiceAmount:N0} VND";

        PlayAmountText = $"{playAmount:N0} VND";
        CurrentRatePlayAmountText = $"{currentRatePlayAmount:N0} VND";

        var totalAmount = playAmountToPay + serviceAmount;
        if (_machineSnapshot.ActiveMemberId != null && _machineSnapshot.ActiveMemberBalance >= 0)
        {
            totalAmount = playAmount + serviceAmount;
        }
        TotalAmountValue = $"{totalAmount:N0}";

        var startedAtTime = DateTime.TryParse(_machineSnapshot.ActiveSessionStartedAt, out var dt) 
            ? dt : DateTime.Now.AddSeconds(-displayedElapsedSeconds);
            
        var breakdowns = CalculateSessionBreakdown(
            startedAtTime,
            displayedElapsedSeconds,
            baseHourlyRate,
            _allPromotions);

        BreakdownBlocks.Clear();
        foreach (var b in breakdowns)
        {
            BreakdownBlocks.Add(new BillingTimeBlockViewModel
            {
                IconPath = b.DiscountPercent > 0 ? "/Assets/payment/vip_badge.svg" : "/Assets/payment/calendar.svg",
                TimeRange = b.TimeRange,
                Title = b.Title,
                Info = b.Info,
                AmountText = $"{b.Amount:N0}đ"
            });
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string FormatUsed(int elapsedSeconds)
    {
        var hours = elapsedSeconds / 3600;
        var minutes = (elapsedSeconds % 3600) / 60;
        return $"{hours:00}:{minutes:00}";
    }

    private static decimal CalculatePrecisePlayAmount(int elapsedSeconds, decimal hourlyRate)
    {
        if (elapsedSeconds <= 0 || hourlyRate <= 0) return 0m;
        var billableMinutes = Math.Max(0, (int)Math.Ceiling(elapsedSeconds / 60.0));
        var perMinute = hourlyRate / 60m;
        var amount = billableMinutes * perMinute;
        return Math.Round(amount, 0, MidpointRounding.AwayFromZero);
    }

    public class BillingTimeBlock
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public decimal BaseHourlyRate { get; set; }
        public decimal DiscountPercent { get; set; }
        public string PromotionName { get; set; } = string.Empty;
        public decimal HourlyRate { get; set; }
        public decimal Amount { get; set; }
        
        public string Title => string.IsNullOrEmpty(PromotionName) ? "Không có KM" : PromotionName;
        public string TimeRange => $"{StartTime:HH:mm} - {EndTime:HH:mm}";
        public string Info => DiscountPercent > 0 
            ? $"Giảm {DiscountPercent:0.##}% (còn {HourlyRate:N0}đ/h)" 
            : $"{HourlyRate:N0}đ/h";
    }

    public class BillingTimeBlockViewModel
    {
        public string IconPath { get; set; } = string.Empty;
        public string TimeRange { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Info { get; set; } = string.Empty;
        public string AmountText { get; set; } = string.Empty;
    }

    private static bool TryParsePromotionTime(string? timeStr, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(timeStr)) return false;
        var parts = timeStr.Split(':');
        if (parts.Length >= 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
        {
            time = new TimeSpan(h, m, 0);
            return true;
        }
        return false;
    }

    private static List<BillingTimeBlock> CalculateSessionBreakdown(
        DateTime startedAt,
        int elapsedSeconds,
        decimal baseHourlyRate,
        List<TimeBasedPromotionDto> promotions)
    {
        var blocks = new List<BillingTimeBlock>();
        if (elapsedSeconds <= 0) return blocks;

        var startMs = new DateTimeOffset(startedAt).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(startedAt.AddSeconds(elapsedSeconds)).ToUnixTimeMilliseconds();
        if (endMs <= startMs) return blocks;

        var cursorMs = startMs;
        BillingTimeBlock? currentBlock = null;

        while (cursorMs < endMs)
        {
            var at = DateTimeOffset.FromUnixTimeMilliseconds(cursorMs).ToLocalTime().DateTime;
            
            var currentDay = (int)at.DayOfWeek;
            var currentTime = at.TimeOfDay;
            decimal bestDiscount = 0m;
            string bestPromotionName = string.Empty;

            foreach (var promo in promotions)
            {
                if (promo is null || !promo.IsActive) continue;

                if (promo.AnnualDates != null && promo.AnnualDates.Count > 0)
                {
                    var ddmm = at.ToString("dd/MM");
                    if (!promo.AnnualDates.Contains(ddmm)) continue;
                }
                else if (promo.DaysOfWeek != null && promo.DaysOfWeek.Count > 0)
                {
                    if (!promo.DaysOfWeek.Contains(currentDay)) continue;
                }

                if (!TryParsePromotionTime(promo.StartTime, out var start) ||
                    !TryParsePromotionTime(promo.EndTime, out var end))
                {
                    continue;
                }

                bool inRange;
                if (start <= end) inRange = currentTime >= start && currentTime <= end;
                else inRange = currentTime >= start || currentTime <= end;

                if (!inRange) continue;

                if (promo.DiscountPercent > bestDiscount)
                {
                    bestDiscount = promo.DiscountPercent;
                    bestPromotionName = (promo.Name ?? string.Empty).Trim();
                }
            }

            var hourlyRate = bestDiscount > 0 
                ? Math.Round(baseHourlyRate * (1m - bestDiscount / 100m)) 
                : baseHourlyRate;

            var amountForThisMinute = hourlyRate / 60m;
            
            var minuteMs = 60_000L;
            var nextBoundary = (cursorMs / minuteMs) * minuteMs + minuteMs;
            var sliceEndMs = Math.Min(endMs, nextBoundary);
            
            if (currentBlock == null || 
                currentBlock.DiscountPercent != bestDiscount || 
                currentBlock.PromotionName != bestPromotionName)
            {
                currentBlock = new BillingTimeBlock
                {
                    StartTime = at,
                    EndTime = DateTimeOffset.FromUnixTimeMilliseconds(sliceEndMs).ToLocalTime().DateTime,
                    BaseHourlyRate = baseHourlyRate,
                    DiscountPercent = bestDiscount,
                    PromotionName = bestPromotionName,
                    HourlyRate = hourlyRate,
                    Amount = amountForThisMinute
                };
                blocks.Add(currentBlock);
            }
            else
            {
                currentBlock.EndTime = DateTimeOffset.FromUnixTimeMilliseconds(sliceEndMs).ToLocalTime().DateTime;
                currentBlock.Amount += amountForThisMinute;
            }

            cursorMs = sliceEndMs;
        }

        return blocks;
    }
}
