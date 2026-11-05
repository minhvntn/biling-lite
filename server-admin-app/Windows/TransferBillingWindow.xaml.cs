using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Server.Admin.App.Models;

namespace Server.Admin.App.Windows;

public partial class TransferBillingWindow : Window
{
    private readonly MachineRow _sourceMachine;
    private readonly IReadOnlyList<MachineRow> _allMachines;
    private readonly HttpClient _httpClient;
    private readonly Func<string, string> _buildApiUrl;
    private readonly Func<string, MachineRow, bool, Task> _sendCommandAsync;
    private readonly Action<string> _appendServiceLog;
    private readonly Func<Task> _onCompletedAsync;

    private decimal _playAmount;
    private decimal _serviceAmount;
    private decimal _previousTransferredAmount;
    private decimal _totalAmount;

    public sealed class TargetCandidateItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SubText { get; set; } = string.Empty;
        public string UserDisplayName { get; set; } = string.Empty;
        public string DurationText { get; set; } = string.Empty;
        public bool IsVip { get; set; }

        public string BadgeText => IsVip ? "👑 VIP" : "👤 Khách";
        public Brush BadgeBackground => IsVip 
            ? new SolidColorBrush(Color.FromRgb(254, 243, 199))   // #FEF3C7 amber-100
            : new SolidColorBrush(Color.FromRgb(241, 245, 249));  // #F1F5F9 slate-100
        public Brush BadgeBorderBrush => IsVip 
            ? new SolidColorBrush(Color.FromRgb(253, 230, 138))   // #FDE68A amber-200
            : new SolidColorBrush(Color.FromRgb(226, 232, 240));  // #E2E8F0 slate-200
        public Brush BadgeForeground => IsVip 
            ? new SolidColorBrush(Color.FromRgb(180, 83, 9))      // #B45309 amber-700
            : new SolidColorBrush(Color.FromRgb(71, 85, 105));    // #475569 slate-600

        public MachineRow Machine { get; set; } = null!;
    }

    public static bool IsEligibleForTransferBilling(MachineRow? m)
    {
        if (m is null) return false;
        if (m.IsAdminSession) return false;
        if (string.Equals(m.UserName, "Admin", StringComparison.OrdinalIgnoreCase)) return false;

        var isActive = m.StatusCode == "IN_USE" || !string.IsNullOrWhiteSpace(m.ActiveSessionId) || m.IsVirtual;
        if (!isActive) return false;

        var hasMember = !string.IsNullOrWhiteSpace(m.ActiveMemberId) || !string.IsNullOrWhiteSpace(m.ActiveMemberUsername);
        if (hasMember)
        {
            // Chỉ hội viên VIP mới được chuyển hoặc nhận thanh toán
            return m.IsVipSession;
        }

        // Khách vãng lai
        return true;
    }

    public TransferBillingWindow(
        MachineRow sourceMachine,
        IReadOnlyList<MachineRow> allMachines,
        HttpClient httpClient,
        Func<string, string> buildApiUrl,
        Func<string, MachineRow, bool, Task> sendCommandAsync,
        Action<string> appendServiceLog,
        Func<Task> onCompletedAsync)
    {
        InitializeComponent();

        _sourceMachine = sourceMachine ?? throw new ArgumentNullException(nameof(sourceMachine));
        _allMachines = allMachines ?? throw new ArgumentNullException(nameof(allMachines));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _buildApiUrl = buildApiUrl ?? throw new ArgumentNullException(nameof(buildApiUrl));
        _sendCommandAsync = sendCommandAsync ?? throw new ArgumentNullException(nameof(sendCommandAsync));
        _appendServiceLog = appendServiceLog ?? throw new ArgumentNullException(nameof(appendServiceLog));
        _onCompletedAsync = onCompletedAsync ?? throw new ArgumentNullException(nameof(onCompletedAsync));

        Loaded += async (_, _) => await InitializeDataAsync();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async Task InitializeDataAsync()
    {
        if (!IsEligibleForTransferBilling(_sourceMachine))
        {
            StatusTextBlock.Text = "Tính năng này chỉ áp dụng đối với khách vãng lai và hội viên VIP.";
            StatusTextBlock.Visibility = Visibility.Visible;
            ConfirmTransferButton.IsEnabled = false;
            return;
        }

        SourceMachineNameTextBlock.Text = _sourceMachine.Name;
        SourceCustomerNameTextBlock.Text = string.IsNullOrWhiteSpace(_sourceMachine.ActiveGuestDisplayName)
            ? (string.IsNullOrWhiteSpace(_sourceMachine.UserName) ? "Khách vãng lai" : _sourceMachine.UserName)
            : _sourceMachine.ActiveGuestDisplayName;

        // 1. Play amount
        _playAmount = _sourceMachine.ActiveSessionEstimatedAmount;
        if (_sourceMachine.IsVirtual && _playAmount == 0)
        {
            var cleanMoney = _sourceMachine.MoneyText?.Replace(",", "")?.Replace(".", "")?.Trim();
            if (decimal.TryParse(cleanMoney, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedMoney))
            {
                _playAmount = parsedMoney;
            }
        }

        var durationSec = _sourceMachine.ActiveSessionElapsedSeconds;
        var durationText = durationSec > 0
            ? FormatDuration(durationSec)
            : (_sourceMachine.UsedText != "-" ? _sourceMachine.UsedText : "0 phút");

        SourcePlayAmountTextBlock.Text = $"{_playAmount:N0} VND";
        SourcePlayDurationTextBlock.Text = $" ({durationText})";

        // 2. Service orders
        _serviceAmount = 0m;
        var serviceSummary = "Không có dịch vụ";

        if (_sourceMachine.IsVirtual)
        {
            _serviceAmount = _sourceMachine.ServiceAmountRaw;
            if (_serviceAmount > 0)
            {
                serviceSummary = "Dịch vụ máy ảo";
            }
        }
        else
        {
            try
            {
                var serviceRes = await _httpClient.GetFromJsonAsync<PcServiceOrdersResponse>(
                    _buildApiUrl($"/services/pcs/{_sourceMachine.Id}/orders"),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (serviceRes?.Items is not null)
                {
                    var unpaid = serviceRes.Items.Where(x => !x.IsPaid).ToList();
                    _serviceAmount = unpaid.Sum(x => x.LineTotal);
                    if (unpaid.Count > 0)
                    {
                        serviceSummary = string.Join(", ", unpaid.Select(x => $"{x.Quantity}x {x.ServiceItem?.Name ?? "Món"}"));
                    }
                }
            }
            catch (Exception ex)
            {
                _appendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi tải dịch vụ cho {_sourceMachine.Name}: {ex.Message}");
            }
        }

        SourceServiceAmountTextBlock.Text = $"{_serviceAmount:N0} VND";
        SourceServiceSummaryTextBlock.Text = serviceSummary;

        // 3. Existing transferred bills on source
        _previousTransferredAmount = 0m;
        if (!_sourceMachine.IsVirtual)
        {
            try
            {
                var transferredRes = await _httpClient.GetFromJsonAsync<TransferredBillsResponse>(
                    _buildApiUrl($"/pcs/{_sourceMachine.Id}/transferred-bills"),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (transferredRes?.Bills is { Count: > 0 })
                {
                    _previousTransferredAmount = transferredRes.TotalTransferredAmount;
                    SourcePreviousTransferredAmountTextBlock.Text = $"{_previousTransferredAmount:N0} VND";
                    SourcePreviousTransferredRow.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        // Total
        _totalAmount = _playAmount + _serviceAmount + _previousTransferredAmount;
        SourceTotalAmountTextBlock.Text = $"{_totalAmount:N0} VND";

        // 4. Populate candidate target machines (Chỉ khách vãng lai và hội viên VIP)
        var candidates = _allMachines
            .Where(m => !string.Equals(m.Id, _sourceMachine.Id, StringComparison.OrdinalIgnoreCase) &&
                        IsEligibleForTransferBilling(m))
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m =>
            {
                var isVip = m.IsVipSession;
                string user;
                if (isVip)
                {
                    user = !string.IsNullOrWhiteSpace(m.ActiveMemberUsername)
                        ? m.ActiveMemberUsername
                        : (m.UserName.StartsWith("VIP:", StringComparison.OrdinalIgnoreCase)
                            ? m.UserName.Substring(4).Trim()
                            : m.UserName);
                }
                else
                {
                    user = !string.IsNullOrWhiteSpace(m.ActiveGuestDisplayName)
                        ? m.ActiveGuestDisplayName
                        : (string.IsNullOrWhiteSpace(m.UserName) || m.UserName == "-" ? "Khách vãng lai" : m.UserName);
                }

                var used = m.UsedText != "-" && !string.IsNullOrWhiteSpace(m.UsedText)
                    ? $"Đang dùng: {m.UsedText}"
                    : "Mới vào";

                var sub = $"{user} • {used}";

                return new TargetCandidateItem
                {
                    Id = m.Id,
                    Name = m.Name,
                    SubText = sub,
                    UserDisplayName = user,
                    DurationText = used,
                    IsVip = isVip,
                    Machine = m,
                };
            })
            .ToList();

        TargetMachineComboBox.ItemsSource = candidates;

        if (candidates.Count > 0)
        {
            TargetMachineComboBox.SelectedIndex = 0;
            ConfirmTransferButton.IsEnabled = true;
        }
        else
        {
            StatusTextBlock.Text = "Không có máy khách vãng lai hoặc hội viên VIP nào khác đang hoạt động để nhận cộng dồn thanh toán.";
            StatusTextBlock.Visibility = Visibility.Visible;
            ConfirmTransferButton.IsEnabled = false;
        }
    }

    private void TargetMachineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        StatusTextBlock.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmTransferButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEligibleForTransferBilling(_sourceMachine))
        {
            StatusTextBlock.Text = "Tính năng này chỉ áp dụng đối với khách vãng lai và hội viên VIP.";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (TargetMachineComboBox.SelectedItem is not TargetCandidateItem selectedTarget ||
            !IsEligibleForTransferBilling(selectedTarget.Machine))
        {
            StatusTextBlock.Text = "Máy nhận thanh toán phải là khách vãng lai hoặc hội viên VIP.";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn chuyển thanh toán từ {_sourceMachine.Name} sang {selectedTarget.Name} không?\n\n" +
            $"• Máy chuyển: {_sourceMachine.Name} (Sẽ kết thúc phiên & khóa máy)\n" +
            $"• Máy nhận: {selectedTarget.Name}\n" +
            $"• Tổng số tiền cộng dồn: {_totalAmount:N0} VND",
            "Xác nhận chuyển thanh toán",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        ConfirmTransferButton.IsEnabled = false;
        Cursor = Cursors.Wait;
        StatusTextBlock.Visibility = Visibility.Collapsed;

        try
        {
            if (_sourceMachine.IsVirtual)
            {
                // Virtual PC transfer handled directly
                await _sendCommandAsync("available", _sourceMachine, true);
                _appendServiceLog($"[{DateTime.Now:HH:mm:ss}] {_sourceMachine.Name} (máy ảo): Đã gộp {_totalAmount:N0} VND sang {selectedTarget.Name}");
            }
            else
            {
                var payload = new TransferBillingRequest
                {
                    TargetPcId = selectedTarget.Id,
                    Note = NoteTextBox.Text.Trim(),
                    RequestedBy = "admin.desktop"
                };

                using var response = await _httpClient.PostAsJsonAsync(
                    _buildApiUrl($"/pcs/{_sourceMachine.Id}/transfer-billing"),
                    payload);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    try
                    {
                        using var doc = JsonDocument.Parse(errorMsg);
                        if (doc.RootElement.TryGetProperty("message", out var msgEl))
                        {
                            errorMsg = msgEl.GetString() ?? errorMsg;
                        }
                    }
                    catch { }

                    StatusTextBlock.Text = $"Chuyển thất bại: {errorMsg}";
                    StatusTextBlock.Visibility = Visibility.Visible;
                    ConfirmTransferButton.IsEnabled = true;
                    Cursor = Cursors.Arrow;
                    return;
                }
            }

            _appendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã chuyển thanh toán từ {_sourceMachine.Name} ({_totalAmount:N0} VND) sang {selectedTarget.Name}");

            await _onCompletedAsync();

            MessageBox.Show(
                $"Đã chuyển hóa đơn thành công!\n\n" +
                $"• Từ máy: {_sourceMachine.Name}\n" +
                $"• Sang máy: {selectedTarget.Name}\n" +
                $"• Số tiền cộng dồn: {_totalAmount:N0} VND\n\n" +
                $"Máy {_sourceMachine.Name} đã được kết thúc và khóa.",
                "Chuyển thanh toán thành công",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Lỗi xử lý: {ex.Message}";
            StatusTextBlock.Visibility = Visibility.Visible;
            ConfirmTransferButton.IsEnabled = true;
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    private static string FormatDuration(int seconds)
    {
        var h = seconds / 3600;
        var m = (seconds % 3600) / 60;
        if (h > 0)
        {
            return m > 0 ? $"{h} giờ {m} phút" : $"{h} giờ";
        }
        return $"{Math.Max(1, m)} phút";
    }
}
