using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<MachineRow> _virtualMachineRows = new();
    private bool _isVirtualPcEnabled = true;
    private int _virtualPcCount = 30;
    private string _virtualPcDistributionMode = "RANDOM";
    private bool _virtualPcAssignGroups = true;
    private bool _virtualPcAddServices = true;
    private readonly Random _virtualRnd = new();

    private static readonly string[] VirtualMemberUsernames =
    [
        "anhkhoa", "tuan_pro", "hoanglong", "quang_huy", "faker_viet",
        "duy_sniper", "gamer_99", "thanh_binh", "viet_anh", "minh_duc",
        "bao_long", "trung_kien", "van_nam", "phuc_thinh", "hai_dang",
        "tien_dung", "quoc_viet", "tan_tai", "ngoc_son", "hieu_thu2",
        "chi_pheo", "ba_kien", "dinh_nam", "xuan_bac"
    ];

    private static readonly string[] VirtualVipUsernames =
    [
        "dai_gia_net", "shark_hung", "tuan_vip", "pro_player", "streamer_long", "chu_tich_xa"
    ];

    private void InitializeVirtualPcsTab()
    {
        if (VirtualPcsPreviewDataGrid != null)
        {
            VirtualPcsPreviewDataGrid.ItemsSource = _virtualMachineRows;
        }

        LoadVirtualPcSettings();

        // Nếu chưa có máy ảo nào, tạo mặc định 30 máy
        if (_virtualMachineRows.Count == 0)
        {
            GenerateVirtualPcsInternal(30, "RANDOM", true, true);
        }
        else
        {
            UpdateVirtualPcSummaryBadges();
        }
    }

    private void VirtualPcQuickCount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out var count))
        {
            if (VirtualPcCountTextBox != null)
            {
                VirtualPcCountTextBox.Text = count.ToString();
            }
            GenerateVirtualPcsInternal(count, GetCurrentVirtualDistributionMode(), IsAssignGroupsChecked(), IsAddServicesChecked());
        }
    }

    private void GenerateVirtualPcsButton_Click(object sender, RoutedEventArgs e)
    {
        var count = 30;
        if (VirtualPcCountTextBox != null && int.TryParse(VirtualPcCountTextBox.Text.Trim(), out var parsedCount))
        {
            count = Math.Clamp(parsedCount, 1, 200);
        }

        GenerateVirtualPcsInternal(count, GetCurrentVirtualDistributionMode(), IsAssignGroupsChecked(), IsAddServicesChecked());
    }

    private void ShuffleVirtualPcsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_virtualMachineRows.Count == 0)
        {
            var count = 30;
            if (VirtualPcCountTextBox != null && int.TryParse(VirtualPcCountTextBox.Text.Trim(), out var parsedCount))
            {
                count = Math.Clamp(parsedCount, 1, 200);
            }
            GenerateVirtualPcsInternal(count, GetCurrentVirtualDistributionMode(), IsAssignGroupsChecked(), IsAddServicesChecked());
            return;
        }

        ShuffleVirtualPcsStateInternal(GetCurrentVirtualDistributionMode(), IsAddServicesChecked());
    }

    private void ClearVirtualPcsButton_Click(object sender, RoutedEventArgs e)
    {
        _virtualMachineRows.Clear();
        UpdateVirtualPcSummaryBadges();
        SaveVirtualPcSettings();
        _ = RefreshMachinesAsync();

        if (VirtualPcStatusMessageTextBlock != null)
        {
            VirtualPcStatusMessageTextBlock.Text = "Đã xóa toàn bộ máy trạm ảo. Danh sách máy trạm đã trở về trạng thái thực tế.";
        }
    }

    private void JumpToMachinesTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (MainTabControl != null)
        {
            MainTabControl.SelectedIndex = 0; // Tab Máy trạm
        }
    }

    private void VirtualPcEnableDisplay_Changed(object sender, RoutedEventArgs e)
    {
        _isVirtualPcEnabled = VirtualPcEnableDisplayCheckBox?.IsChecked ?? true;
        SaveVirtualPcSettings();
        _ = RefreshMachinesAsync();
    }

    private string GetCurrentVirtualDistributionMode()
    {
        if (VirtualPcDistributionComboBox != null && VirtualPcDistributionComboBox.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() ?? "RANDOM";
        }
        return "RANDOM";
    }

    private bool IsAssignGroupsChecked() => VirtualPcAssignGroupsCheckBox?.IsChecked ?? true;
    private bool IsAddServicesChecked() => VirtualPcAddServicesCheckBox?.IsChecked ?? true;

    private void GenerateVirtualPcsInternal(int count, string mode, bool assignGroups, bool addServices)
    {
        _virtualPcCount = count;
        _virtualPcDistributionMode = mode;
        _virtualPcAssignGroups = assignGroups;
        _virtualPcAddServices = addServices;

        _virtualMachineRows.Clear();
        var now = DateTime.Now;

        for (int i = 1; i <= count; i++)
        {
            var pcName = $"Máy {i:D2}";
            var row = CreateVirtualMachineRow(i, pcName, mode, assignGroups, addServices, now);
            _virtualMachineRows.Add(row);
        }

        UpdateVirtualPcSummaryBadges();
        SaveVirtualPcSettings();
        _ = RefreshMachinesAsync();

        if (VirtualPcStatusMessageTextBlock != null)
        {
            VirtualPcStatusMessageTextBlock.Text = $"Đã tạo thành công {count} máy trạm ảo ({GetModeDisplayName(mode)}) lúc {now:HH:mm:ss}.";
        }
    }

    private void ShuffleVirtualPcsStateInternal(string mode, bool addServices)
    {
        var now = DateTime.Now;
        int i = 1;

        foreach (var row in _virtualMachineRows)
        {
            ApplyVirtualStateToRow(row, i, row.Name, mode, addServices, now);
            i++;
        }

        UpdateVirtualPcSummaryBadges();
        SaveVirtualPcSettings();
        _ = RefreshMachinesAsync();

        if (VirtualPcStatusMessageTextBlock != null)
        {
            VirtualPcStatusMessageTextBlock.Text = $"Đã tạo ngẫu nhiên lại trạng thái cho {_virtualMachineRows.Count} máy ảo lúc {now:HH:mm:ss}.";
        }
    }

    private MachineRow CreateVirtualMachineRow(int index, string pcName, string mode, bool assignGroups, bool addServices, DateTime now)
    {
        string groupName = "Mặc định";
        decimal hourlyRate = 10000;

        if (assignGroups)
        {
            double ratio = (double)index / Math.Max(1, _virtualPcCount);
            if (ratio <= 0.50)
            {
                groupName = "Phòng Thường";
                hourlyRate = 8000;
            }
            else if (ratio <= 0.75)
            {
                groupName = "Phòng VIP";
                hourlyRate = 12000;
            }
            else if (ratio <= 0.90)
            {
                groupName = "Khu Thi Đấu";
                hourlyRate = 18000;
            }
            else
            {
                groupName = "Phòng Stream";
                hourlyRate = 25000;
            }
        }

        var row = new MachineRow
        {
            Id = $"virtual-pc-{index:D3}",
            AgentId = $"VIRTPC{index:D3}",
            Name = pcName,
            GroupName = groupName,
            HourlyRate = hourlyRate,
            IpAddress = $"192.168.1.{100 + (index % 150)}",
            MacAddress = $"02:00:00:00:{(index / 256):X2}:{(index % 256):X2}",
            DateText = now.ToString("dd-MM-yyyy"),
            VersionText = "0.1.0",
            IsVirtual = true,
        };

        ApplyVirtualStateToRow(row, index, pcName, mode, addServices, now);
        return row;
    }

    private void ApplyVirtualStateToRow(MachineRow row, int index, string pcName, string mode, bool addServices, DateTime now)
    {
        // Phân bổ trạng thái
        string targetState;

        if (mode == "ALL_ONLINE")
        {
            targetState = "ONLINE";
        }
        else if (mode == "ALL_OFFLINE")
        {
            targetState = "OFFLINE";
        }
        else if (mode == "FULL")
        {
            int r = _virtualRnd.Next(100);
            if (r < 50) targetState = "MEMBER";
            else if (r < 75) targetState = "VIP";
            else if (r < 93) targetState = "GUEST";
            else targetState = "ADMIN";
        }
        else if (mode == "BUSY") // 80% bận, 20% rảnh/tắt
        {
            int r = _virtualRnd.Next(100);
            if (r < 15) targetState = "ONLINE";
            else if (r < 55) targetState = "MEMBER";
            else if (r < 73) targetState = "VIP";
            else if (r < 87) targetState = "GUEST";
            else if (r < 93) targetState = "ADMIN";
            else targetState = "OFFLINE";
        }
        else // RANDOM (Thực tế)
        {
            int r = _virtualRnd.Next(100);
            if (r < 25) targetState = "ONLINE";        // 25% Sẵn sàng
            else if (r < 55) targetState = "MEMBER";     // 30% Hội viên
            else if (r < 70) targetState = "VIP";        // 15% VIP
            else if (r < 84) targetState = "GUEST";      // 14% Khách
            else if (r < 90) targetState = "ADMIN";      // 6% Admin
            else targetState = "OFFLINE";                // 10% Offline / Đang tắt
        }

        // Reset các flag phiên
        row.IsAdminSession = false;
        row.IsVipSession = false;
        row.IsGuestSession = false;
        row.IsOfflineUnpaidGuest = false;
        row.HasPendingClientServiceOrderHighlight = false;
        row.HasServiceDebtHighlight = false;

        if (targetState == "ONLINE")
        {
            row.StatusCode = "ONLINE";
            row.StatusText = "Sẵn sàng";
            row.StatusSubText = string.Empty;
            row.StatusIconPath = "/Assets/pc-available.svg";
            row.StatusIconBrush = Brushes.LimeGreen;
            row.StatusBrush = Brushes.DarkSlateBlue;
            row.StatusIconToolTip = "Sẵn sàng";
            row.UserName = "-";
            row.StartedAtText = "-";
            row.UsedText = "-";
            row.RemainingText = "-";
            row.MoneyText = "-";
            row.ServiceAmountRaw = 0;
            row.ServiceAmountText = "-";
            row.LastSeenAtText = now.AddSeconds(-_virtualRnd.Next(5, 45)).ToString("HH:mm:ss");
        }
        else if (targetState == "OFFLINE")
        {
            row.StatusCode = "OFFLINE";
            row.StatusText = _virtualRnd.Next(2) == 0 ? "Mất kết nối" : "Đang tắt";
            row.StatusSubText = string.Empty;
            row.StatusIconPath = "/Assets/pc-offline.svg";
            row.StatusIconBrush = Brushes.Crimson;
            row.StatusBrush = Brushes.White;
            row.StatusIconToolTip = row.StatusText;
            row.UserName = "-";
            row.StartedAtText = "-";
            row.UsedText = "-";
            row.RemainingText = "-";
            row.MoneyText = "-";
            row.ServiceAmountRaw = 0;
            row.ServiceAmountText = "-";
            row.LastSeenAtText = now.AddMinutes(-_virtualRnd.Next(15, 180)).ToString("HH:mm:ss");
        }
        else if (targetState == "ADMIN")
        {
            int usedMins = _virtualRnd.Next(5, 75);
            var startedAt = now.AddMinutes(-usedMins);

            row.StatusCode = "IN_USE";
            row.StatusText = "Admin đang nhập";
            row.StatusSubText = string.Empty;
            row.IsAdminSession = true;
            row.StatusIconPath = "/Assets/pc-admin.svg";
            row.StatusIconBrush = Brushes.Gold;
            row.StatusBrush = Brushes.RoyalBlue;
            row.StatusIconToolTip = "Admin đang sử dụng";
            row.UserName = "Admin";
            row.StartedAtText = startedAt.ToString("HH:mm:ss");
            row.UsedText = $"{usedMins / 60:D2}:{usedMins % 60:D2}";
            row.RemainingText = "-";
            row.MoneyText = "-";
            row.ServiceAmountRaw = 0;
            row.ServiceAmountText = "-";
            row.LastSeenAtText = now.ToString("HH:mm:ss");
        }
        else if (targetState == "VIP")
        {
            int usedMins = _virtualRnd.Next(20, 280);
            var startedAt = now.AddMinutes(-usedMins);
            var username = VirtualVipUsernames[_virtualRnd.Next(VirtualVipUsernames.Length)];
            decimal balance = _virtualRnd.Next(300, 1500) * 1000m;
            decimal pricePerMin = row.HourlyRate / 60m;
            decimal estimatedMoney = Math.Round(usedMins * pricePerMin);

            row.StatusCode = "IN_USE";
            row.StatusText = "Đang sử dụng";
            row.StatusSubText = string.Empty;
            row.IsVipSession = true;
            row.StatusIconPath = "/Assets/pc-blue-user.svg";
            row.StatusIconBrush = Brushes.Goldenrod;
            row.StatusBrush = Brushes.RoyalBlue;
            row.StatusIconToolTip = "Hội viên VIP đang sử dụng";
            row.UserName = $"VIP: {username}";
            row.ActiveMemberUsername = username;
            row.ActiveMemberBalance = balance;
            row.StartedAtText = startedAt.ToString("HH:mm:ss");
            row.UsedText = $"{usedMins / 60:D2}:{usedMins % 60:D2}";
            row.RemainingText = $"{99 - (usedMins / 60)}:{60 - (usedMins % 60):D2}";
            row.MoneyText = estimatedMoney.ToString("N0");
            row.LastSeenAtText = now.ToString("HH:mm:ss");

            ApplyVirtualService(row, addServices);
        }
        else if (targetState == "MEMBER")
        {
            int usedMins = _virtualRnd.Next(15, 210);
            var startedAt = now.AddMinutes(-usedMins);
            var username = VirtualMemberUsernames[_virtualRnd.Next(VirtualMemberUsernames.Length)];
            decimal balance = _virtualRnd.Next(30, 180) * 1000m;
            decimal pricePerMin = row.HourlyRate / 60m;
            decimal estimatedMoney = Math.Round(usedMins * pricePerMin);
            int remMins = Math.Max(5, (int)Math.Floor(balance / Math.Max(1m, pricePerMin)));

            row.StatusCode = "IN_USE";
            row.StatusText = "Đang sử dụng";
            row.StatusSubText = string.Empty;
            row.StatusIconPath = "/Assets/pc-blue-user.svg";
            row.StatusIconBrush = Brushes.DodgerBlue;
            row.StatusBrush = Brushes.RoyalBlue;
            row.StatusIconToolTip = "Hội viên đang sử dụng";
            row.UserName = username;
            row.ActiveMemberUsername = username;
            row.ActiveMemberBalance = balance;
            row.StartedAtText = startedAt.ToString("HH:mm:ss");
            row.UsedText = $"{usedMins / 60:D2}:{usedMins % 60:D2}";
            row.RemainingText = $"{remMins / 60:D2}:{remMins % 60:D2}";
            row.MoneyText = estimatedMoney.ToString("N0");
            row.LastSeenAtText = now.ToString("HH:mm:ss");

            ApplyVirtualService(row, addServices);
        }
        else // GUEST
        {
            int usedMins = _virtualRnd.Next(10, 150);
            var startedAt = now.AddMinutes(-usedMins);
            decimal pricePerMin = row.HourlyRate / 60m;
            decimal estimatedMoney = Math.Round(usedMins * pricePerMin);
            bool isPrepaid = _virtualRnd.Next(2) == 0;
            decimal prepaidAmount = isPrepaid ? (_virtualRnd.Next(2, 8) * 10000m) : 0;
            int remMins = isPrepaid
                ? Math.Max(2, (int)Math.Floor((prepaidAmount - estimatedMoney) / Math.Max(1m, pricePerMin)))
                : (99 * 60 - usedMins);

            row.StatusCode = "IN_USE";
            row.StatusText = "Đang sử dụng";
            row.StatusSubText = string.Empty;
            row.IsGuestSession = true;
            row.StatusIconPath = "/Assets/pc-guest.svg";
            row.StatusIconBrush = Brushes.Orange;
            row.StatusBrush = Brushes.RoyalBlue;
            row.StatusIconToolTip = "Khách đang sử dụng";
            row.UserName = $"Khách {pcName}";
            row.ActiveGuestDisplayName = $"Khách {pcName}";
            row.ActiveGuestPrepaidAmount = prepaidAmount;
            row.StartedAtText = startedAt.ToString("HH:mm:ss");
            row.UsedText = $"{usedMins / 60:D2}:{usedMins % 60:D2}";
            row.RemainingText = $"{remMins / 60:D2}:{remMins % 60:D2}";
            row.MoneyText = (isPrepaid ? prepaidAmount : estimatedMoney).ToString("N0");
            row.LastSeenAtText = now.ToString("HH:mm:ss");

            ApplyVirtualService(row, addServices);
        }
    }

    private void ApplyVirtualService(MachineRow row, bool addServices)
    {
        if (addServices && _virtualRnd.Next(100) < 40) // 40% có gọi dịch vụ
        {
            int[] servicePrices = [15000, 20000, 30000, 45000, 60000];
            decimal sAmount = servicePrices[_virtualRnd.Next(servicePrices.Length)];
            row.ServiceAmountRaw = sAmount;
            row.ServiceAmountText = sAmount.ToString("N0");
            row.HasPendingClientServiceOrderHighlight = _virtualRnd.Next(100) < 15; // 15% highlight có order mới
        }
        else
        {
            row.ServiceAmountRaw = 0;
            row.ServiceAmountText = "0";
            row.HasPendingClientServiceOrderHighlight = false;
        }
    }

    private void HandleVirtualMachineCommand(MachineRow selected, string action)
    {
        var now = DateTime.Now;

        switch (action.ToLowerInvariant())
        {
            case "lock":
                selected.StatusCode = "LOCKED";
                selected.StatusText = "Đang khóa";
                selected.StatusSubText = string.Empty;
                selected.StatusIconPath = "/Assets/pc-offline.svg";
                selected.StatusIconBrush = Brushes.Orange;
                selected.StatusBrush = Brushes.Orange;
                selected.StatusIconToolTip = "Đang khóa";
                selected.UserName = "-";
                selected.IsAdminSession = false;
                selected.IsVipSession = false;
                selected.IsGuestSession = false;
                selected.RemainingText = "-";
                selected.MoneyText = "-";
                break;

            case "open":
            case "guest-login":
                selected.StatusCode = "IN_USE";
                selected.StatusText = "Đang sử dụng";
                selected.StatusSubText = string.Empty;
                selected.IsGuestSession = true;
                selected.StatusIconPath = "/Assets/pc-guest.svg";
                selected.StatusIconBrush = Brushes.Orange;
                selected.StatusBrush = Brushes.RoyalBlue;
                selected.StatusIconToolTip = "Khách đang sử dụng";
                selected.UserName = $"Khách {selected.Name}";
                selected.StartedAtText = now.ToString("HH:mm:ss");
                selected.UsedText = "00:01";
                selected.RemainingText = "99:59";
                selected.MoneyText = "1,000";
                break;

            case "admin-login":
                selected.StatusCode = "IN_USE";
                selected.StatusText = "Admin đang nhập";
                selected.StatusSubText = string.Empty;
                selected.IsAdminSession = true;
                selected.StatusIconPath = "/Assets/pc-admin.svg";
                selected.StatusIconBrush = Brushes.Gold;
                selected.StatusBrush = Brushes.RoyalBlue;
                selected.StatusIconToolTip = "Admin đang sử dụng";
                selected.UserName = "Admin";
                selected.StartedAtText = now.ToString("HH:mm:ss");
                selected.UsedText = "00:01";
                selected.RemainingText = "-";
                selected.MoneyText = "-";
                break;

            case "shutdown":
                selected.StatusCode = "OFFLINE";
                selected.StatusText = "Đang tắt";
                selected.StatusSubText = string.Empty;
                selected.StatusIconPath = "/Assets/pc-offline.svg";
                selected.StatusIconBrush = Brushes.Crimson;
                selected.StatusBrush = Brushes.White;
                selected.StatusIconToolTip = "Đang tắt";
                selected.UserName = "-";
                selected.StartedAtText = "-";
                selected.UsedText = "-";
                selected.RemainingText = "-";
                selected.MoneyText = "-";
                selected.ServiceAmountRaw = 0;
                selected.ServiceAmountText = "-";
                break;

            case "restart":
                selected.StatusCode = "ONLINE";
                selected.StatusText = "Sẵn sàng";
                selected.StatusSubText = string.Empty;
                selected.StatusIconPath = "/Assets/pc-available.svg";
                selected.StatusIconBrush = Brushes.LimeGreen;
                selected.StatusBrush = Brushes.DarkSlateBlue;
                selected.StatusIconToolTip = "Sẵn sàng";
                selected.UserName = "-";
                selected.StartedAtText = "-";
                selected.UsedText = "-";
                selected.RemainingText = "-";
                selected.MoneyText = "-";
                selected.ServiceAmountRaw = 0;
                selected.ServiceAmountText = "-";
                break;
        }

        UpdateVirtualPcSummaryBadges();
        _ = RefreshMachinesAsync();

        MessageBox.Show(
            $"Đã thực hiện lệnh [{action}] trên máy trạm ảo {selected.Name} thành công.",
            "Máy trạm ảo (Demo)",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void UpdateVirtualPcSummaryBadges()
    {
        var total = _virtualMachineRows.Count;
        var onlineCount = _virtualMachineRows.Count(r => r.StatusCode == "ONLINE");
        var memberCount = _virtualMachineRows.Count(r => r.StatusCode == "IN_USE" && !r.IsAdminSession && !r.IsVipSession && !r.IsGuestSession);
        var vipCount = _virtualMachineRows.Count(r => r.IsVipSession);
        var guestCount = _virtualMachineRows.Count(r => r.IsGuestSession);
        var adminCount = _virtualMachineRows.Count(r => r.IsAdminSession);
        var offlineCount = _virtualMachineRows.Count(r => r.StatusCode == "OFFLINE" || r.StatusCode == "LOCKED");

        decimal totalHoursMoney = _virtualMachineRows.Sum(r => ParseMoney(r.MoneyText));
        decimal totalServicesMoney = _virtualMachineRows.Sum(r => r.ServiceAmountRaw);

        if (VpBadgeTotalTextBlock != null) VpBadgeTotalTextBlock.Text = total.ToString();
        if (VpBadgeOnlineTextBlock != null) VpBadgeOnlineTextBlock.Text = onlineCount.ToString();
        if (VpBadgeMemberTextBlock != null) VpBadgeMemberTextBlock.Text = memberCount.ToString();
        if (VpBadgeVipTextBlock != null) VpBadgeVipTextBlock.Text = vipCount.ToString();
        if (VpBadgeGuestTextBlock != null) VpBadgeGuestTextBlock.Text = guestCount.ToString();
        if (VpBadgeAdminTextBlock != null) VpBadgeAdminTextBlock.Text = adminCount.ToString();
        if (VpBadgeOffTextBlock != null) VpBadgeOffTextBlock.Text = offlineCount.ToString();
        if (VpBadgeMoneyTextBlock != null) VpBadgeMoneyTextBlock.Text = $"{totalHoursMoney:N0} đ";
        if (VpBadgeServicesTextBlock != null) VpBadgeServicesTextBlock.Text = $"{totalServicesMoney:N0} đ";
    }

    private static string GetModeDisplayName(string mode) => mode switch
    {
        "BUSY" => "Phòng máy đông đúc ~80%",
        "FULL" => "100% Đang sử dụng",
        "ALL_ONLINE" => "100% Sẵn sàng",
        "ALL_OFFLINE" => "100% Offline",
        _ => "Ngẫu nhiên thực tế"
    };

    private void SaveVirtualPcSettings()
    {
        try
        {
            var config = new VirtualPcConfigModel
            {
                IsEnabled = _isVirtualPcEnabled,
                Count = _virtualPcCount,
                Mode = _virtualPcDistributionMode,
                AssignGroups = _virtualPcAssignGroups,
                AddServices = _virtualPcAddServices,
                Items = _virtualMachineRows.Select(ToVirtualPcDto).ToList(),
            };

            var path = Path.Combine(AppContext.BaseDirectory, "virtual-pcs-cache.json");
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore settings save errors on write-restricted paths
        }
    }

    private void LoadVirtualPcSettings()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "virtual-pcs-cache.json");
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<VirtualPcConfigModel>(json);
            if (config is null) return;

            _isVirtualPcEnabled = config.IsEnabled;
            _virtualPcCount = config.Count > 0 ? config.Count : 30;
            _virtualPcDistributionMode = config.Mode ?? "RANDOM";
            _virtualPcAssignGroups = config.AssignGroups;
            _virtualPcAddServices = config.AddServices;

            if (VirtualPcEnableDisplayCheckBox != null) VirtualPcEnableDisplayCheckBox.IsChecked = _isVirtualPcEnabled;
            if (VirtualPcCountTextBox != null) VirtualPcCountTextBox.Text = _virtualPcCount.ToString();
            if (VirtualPcAssignGroupsCheckBox != null) VirtualPcAssignGroupsCheckBox.IsChecked = _virtualPcAssignGroups;
            if (VirtualPcAddServicesCheckBox != null) VirtualPcAddServicesCheckBox.IsChecked = _virtualPcAddServices;

            if (VirtualPcDistributionComboBox != null)
            {
                foreach (ComboBoxItem item in VirtualPcDistributionComboBox.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), _virtualPcDistributionMode, StringComparison.OrdinalIgnoreCase))
                    {
                        VirtualPcDistributionComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            if (config.Items != null && config.Items.Count > 0)
            {
                _virtualMachineRows.Clear();
                foreach (var dto in config.Items)
                {
                    _virtualMachineRows.Add(FromVirtualPcDto(dto));
                }
            }
        }
        catch
        {
            // Fallback to fresh generated items
        }
    }

    private static VirtualPcItemDto ToVirtualPcDto(MachineRow row) => new()
    {
        Id = row.Id,
        AgentId = row.AgentId,
        Name = row.Name,
        GroupName = row.GroupName,
        HourlyRate = row.HourlyRate,
        IpAddress = row.IpAddress,
        MacAddress = row.MacAddress,
        StatusCode = row.StatusCode,
        StatusText = row.StatusText,
        StatusSubText = row.StatusSubText,
        StatusIconPath = row.StatusIconPath,
        StatusIconToolTip = row.StatusIconToolTip,
        UserName = row.UserName,
        StartedAtText = row.StartedAtText,
        UsedText = row.UsedText,
        RemainingText = row.RemainingText,
        MoneyText = row.MoneyText,
        ServiceAmountRaw = row.ServiceAmountRaw,
        ServiceAmountText = row.ServiceAmountText,
        IsAdminSession = row.IsAdminSession,
        IsVipSession = row.IsVipSession,
        IsGuestSession = row.IsGuestSession,
        ActiveMemberUsername = row.ActiveMemberUsername,
        ActiveMemberBalance = row.ActiveMemberBalance,
        LastSeenAtText = row.LastSeenAtText,
        DateText = row.DateText,
    };

    private static MachineRow FromVirtualPcDto(VirtualPcItemDto dto)
    {
        var row = new MachineRow
        {
            Id = dto.Id,
            AgentId = dto.AgentId,
            Name = dto.Name,
            GroupName = dto.GroupName,
            HourlyRate = dto.HourlyRate,
            IpAddress = dto.IpAddress,
            MacAddress = dto.MacAddress,
            StatusCode = dto.StatusCode,
            StatusText = dto.StatusText,
            StatusSubText = dto.StatusSubText,
            StatusIconPath = dto.StatusIconPath,
            StatusIconToolTip = dto.StatusIconToolTip,
            UserName = dto.UserName,
            StartedAtText = dto.StartedAtText,
            UsedText = dto.UsedText,
            RemainingText = dto.RemainingText,
            MoneyText = dto.MoneyText,
            ServiceAmountRaw = dto.ServiceAmountRaw,
            ServiceAmountText = dto.ServiceAmountText,
            IsAdminSession = dto.IsAdminSession,
            IsVipSession = dto.IsVipSession,
            IsGuestSession = dto.IsGuestSession,
            ActiveMemberUsername = dto.ActiveMemberUsername,
            ActiveMemberBalance = dto.ActiveMemberBalance,
            LastSeenAtText = dto.LastSeenAtText,
            DateText = dto.DateText,
            VersionText = "0.1.0",
            IsVirtual = true,
        };

        // Gán lại brushes
        if (dto.StatusCode == "ONLINE")
        {
            row.StatusIconBrush = Brushes.LimeGreen;
            row.StatusBrush = Brushes.DarkSlateBlue;
        }
        else if (dto.StatusCode == "IN_USE")
        {
            row.StatusBrush = Brushes.RoyalBlue;
            if (dto.IsAdminSession) row.StatusIconBrush = Brushes.Gold;
            else if (dto.IsVipSession) row.StatusIconBrush = Brushes.Goldenrod;
            else if (dto.IsGuestSession) row.StatusIconBrush = Brushes.Orange;
            else row.StatusIconBrush = Brushes.DodgerBlue;
        }
        else if (dto.StatusCode == "OFFLINE")
        {
            row.StatusIconBrush = Brushes.Crimson;
            row.StatusBrush = Brushes.White;
        }
        else
        {
            row.StatusIconBrush = Brushes.Orange;
            row.StatusBrush = Brushes.Orange;
        }

        return row;
    }
}

public sealed class VirtualPcConfigModel
{
    public bool IsEnabled { get; set; } = true;
    public int Count { get; set; } = 30;
    public string Mode { get; set; } = "RANDOM";
    public bool AssignGroups { get; set; } = true;
    public bool AddServices { get; set; } = true;
    public List<VirtualPcItemDto> Items { get; set; } = [];
}

public sealed class VirtualPcItemDto
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string StatusSubText { get; set; } = string.Empty;
    public string StatusIconPath { get; set; } = string.Empty;
    public string StatusIconToolTip { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string StartedAtText { get; set; } = string.Empty;
    public string UsedText { get; set; } = string.Empty;
    public string RemainingText { get; set; } = string.Empty;
    public string MoneyText { get; set; } = string.Empty;
    public decimal ServiceAmountRaw { get; set; }
    public string ServiceAmountText { get; set; } = string.Empty;
    public bool IsAdminSession { get; set; }
    public bool IsVipSession { get; set; }
    public bool IsGuestSession { get; set; }
    public string? ActiveMemberUsername { get; set; }
    public decimal ActiveMemberBalance { get; set; }
    public string LastSeenAtText { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
}
