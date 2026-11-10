using System.Collections.ObjectModel;
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
public partial class MainWindow : Window
{
    private async Task RefreshSystemLogsAsync(bool forceRefresh = false)
    {
        if (_isRefreshingSystemLogs)
        {
            return;
        }

        _isRefreshingSystemLogs = true;
        try
        {
            var limit = GetSelectedSystemLogLimit();
            SystemEventsResponse? response;
            if (!forceRefresh &&
                _systemLogsCacheResponse is not null &&
                _systemLogsCacheLimit == limit &&
                IsCacheValid(_systemLogsCacheAtUtc, SystemLogsCacheTtlSeconds))
            {
                response = _systemLogsCacheResponse;
            }
            else
            {
                response = await _httpClient.GetFromJsonAsync<SystemEventsResponse>(
                    BuildApiUrl($"/reports/events/system?limit={limit}"),
                    JsonOptions());
                if (response is not null)
                {
                    _systemLogsCacheResponse = response;
                    _systemLogsCacheLimit = limit;
                    _systemLogsCacheAtUtc = DateTime.UtcNow;
                }
            }

            if (response is null)
            {
                return;
            }

            _latestSystemEventItems = response.Items?.ToList() ?? new List<SystemEventItem>();
            ProcessMemberTransferNotifications(_latestSystemEventItems);
            UpdateSystemLogMachineFilterOptions(_latestSystemEventItems);
            ApplySystemLogMachineFilterAndTimeline(response.Total);
            UpdateRealtimeAlertPanelFromEvents(_latestSystemEventItems);
        }
        catch
        {
            // Ignore temporary errors.
        }
        finally
        {
            _isRefreshingSystemLogs = false;
        }
    }

    private static SystemLogRow ToSystemLogRow(SystemEventItem item)
    {
        var (eventName, details) = TranslateSystemEvent(item);

        var pcText = item.PcName is null
            ? "-"
            : string.IsNullOrWhiteSpace(item.AgentId)
                ? item.PcName
                : $"{item.PcName} ({item.AgentId})";

        return new SystemLogRow
        {
            CreatedAtText = FormatDateTime(item.CreatedAt),
            Source = item.Source,
            EventType = eventName,
            PcText = pcText,
            PayloadText = details,
        };
    }

    private static (string EventName, string Details) TranslateSystemEvent(SystemEventItem item)
    {
        var eventType = item.EventType ?? string.Empty;
        var eventName = eventType;
        var details = "-";

        if (item.Payload is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            if (item.Payload is not null)
            {
                var rawJson = JsonSerializer.Serialize(item.Payload);
                details = rawJson.Length > 220 ? rawJson[..220] + "..." : rawJson;
            }
            return (eventName, details);
        }

        try
        {
            switch (eventType.ToLowerInvariant())
            {
                case "member.withdraw.requested":
                    eventName = "Yêu cầu rút tiền";
                    details = $"Tài khoản: {ReadJsonString(json, "username")} - Số tiền: {ReadJsonDecimal(json, "amount"):N0} VND";
                    break;
                case "member.topup.requested":
                    eventName = "Yêu cầu nạp tiền";
                    details = $"Tài khoản: {ReadJsonString(json, "username")} - Số tiền: {ReadJsonDecimal(json, "amount"):N0} VND";
                    break;
                case "member.topup":
                    eventName = "Nạp tiền hội viên";
                    details = $"Tài khoản: {ReadJsonString(json, "username")} - Số tiền: {ReadJsonDecimal(json, "amount"):N0} VND";
                    break;
                case "member.balance.adjusted":
                    eventName = "Điều chỉnh số dư";
                    details = $"Tài khoản: {ReadJsonString(json, "username")} - Số tiền: {ReadJsonDecimal(json, "amountDelta"):N0} VND";
                    break;
                case "member.pc.presence":
                    var memberIsActive = ReadJsonBool(json, "isActive");
                    eventName = memberIsActive ? "Hội viên đăng nhập" : "Hội viên đăng xuất";
                    details = $"Tài khoản: {ReadJsonString(json, "username")}";
                    break;
                case "guest.pc.presence":
                    var guestIsActive = ReadJsonBool(json, "isActive");
                    eventName = guestIsActive ? "Khách đăng nhập" : "Khách đăng xuất";
                    details = $"Tên hiển thị: {ReadJsonString(json, "displayName")}";
                    break;
                case "admin.pc.presence":
                    var adminIsActive = ReadJsonBool(json, "isActive");
                    eventName = adminIsActive ? "Admin đăng nhập" : "Admin đăng xuất";
                    details = $"Quản trị viên";
                    break;
                case "pc.status.changed":
                    eventName = "Trạng thái máy";
                    var prevStatus = ReadJsonString(json, "previousStatus");
                    var newStatus = ReadJsonString(json, "status");
                    details = $"Chuyển từ {TranslateStatus(prevStatus)} sang {TranslateStatus(newStatus)}";
                    break;
                case "session.closed.auto_offline":
                    eventName = "Đóng phiên (Mất kết nối)";
                    details = $"Tiền: {ReadJsonDecimal(json, "amount"):N0} VND - Thời gian: {ReadJsonInt(json, "billableMinutes")} phút";
                    break;
                case "session.closed":
                    eventName = "Đóng phiên";
                    details = $"Tiền: {ReadJsonDecimal(json, "amount"):N0} VND - Thời gian: {ReadJsonInt(json, "billableMinutes")} phút";
                    break;
                case "command.ack.received":
                    eventName = "Phản hồi lệnh";
                    details = $"Kết quả: {ReadJsonString(json, "result")} - Lệnh: {ReadJsonString(json, "message")}";
                    break;
                case "member.balance.transferred":
                    eventName = "Chuyển tiền hội viên";
                    details = $"Từ {ReadJsonString(json, "sourceUsername")} sang {ReadJsonString(json, "targetUsername")} - Số tiền: {ReadJsonDecimal(json, "amount"):N0} VND";
                    break;
                case "service.order.created":
                    eventName = "Goi dich vu";
                    details =
                        $"Mon: {ReadJsonStringOrDefault(json, "serviceName", "Dich vu")} - " +
                        $"SL: {ReadJsonInt(json, "quantity")} - " +
                        $"Thanh tien: {ReadJsonDecimal(json, "lineTotal"):N0} VND";
                    break;
                case "service.order.canceled":
                    eventName = "Huy dich vu";
                    details =
                        $"SL huy: {ReadJsonInt(json, "canceledQuantity")} - " +
                        $"Tien huy: {ReadJsonDecimal(json, "canceledAmount"):N0} VND - " +
                        $"Dong xoa: {ReadJsonArrayCount(json, "canceledOrderIds")} - " +
                        $"Dong giam SL: {ReadJsonArrayCount(json, "updatedOrders")}";
                    break;
                case "service.order.paid":
                    eventName = "Thanh toan dich vu";
                    details =
                        $"So dong: {ReadJsonInt(json, "paidOrderCount")} - " +
                        $"Tien thu: {ReadJsonDecimal(json, "paidAmount"):N0} VND - " +
                        $"Con no: {ReadJsonDecimal(json, "unpaidAmount"):N0} VND";
                    break;
                case "command.created":
                    eventName = "Tao lenh";
                    details = $"Lenh: {TranslateCommandType(ReadJsonString(json, "type"))} - Ma lenh: {ShortId(ReadJsonString(json, "commandId"))}";
                    break;
                case "command.dispatched":
                    eventName = "Da gui lenh";
                    details =
                        $"Lenh: {TranslateCommandType(ReadJsonString(json, "type"))} - " +
                        $"Socket: {ReadJsonInt(json, "sockets")} - " +
                        $"Ma lenh: {ShortId(ReadJsonString(json, "commandId"))}";
                    break;
                case "command.redispatched.inflight":
                    eventName = "Gui lai lenh";
                    details =
                        $"Lenh: {TranslateCommandType(ReadJsonString(json, "type"))} - " +
                        $"Socket: {ReadJsonInt(json, "sockets")} - " +
                        $"Ma lenh: {ShortId(ReadJsonString(json, "commandId"))}";
                    break;
                case "command.timeout.ack_missing":
                    eventName = "Lenh qua han phan hoi";
                    details = $"Ma lenh: {ShortId(ReadJsonString(json, "commandId"))}";
                    break;
                case "command.timeout.agent_offline":
                case "command.timeout.agent_offline.inflight":
                    eventName = "Lenh that bai (may offline)";
                    details =
                        $"Lenh: {TranslateCommandType(ReadJsonString(json, "type"))} - " +
                        $"Ma lenh: {ShortId(ReadJsonString(json, "commandId"))}";
                    break;
                case "command.noop.open_already_in_use":
                    eventName = "Bo qua lenh mo may";
                    details = "May dang su dung, khong gui lenh mo them.";
                    break;
                case "command.dispatch.remapped_pc":
                    eventName = "Chuyen huong lenh";
                    details = $"Lenh: {TranslateCommandType(ReadJsonString(json, "commandType"))} - Muc tieu moi: {ShortId(ReadJsonString(json, "targetPcId"))}";
                    break;
                case "command.ack.not_found":
                    eventName = "Phan hoi lenh khong ton tai";
                    details = $"Ma lenh: {ShortId(ReadJsonString(json, "commandId"))}";
                    break;
                case "command.ack.agent_mismatch":
                    eventName = "Phan hoi sai may tram";
                    details = $"Ma lenh: {ShortId(ReadJsonString(json, "commandId"))}";
                    break;
                case "session.transferred":
                    eventName = "Chuyen phien may";
                    details = $"Ma phien: {ShortId(ReadJsonString(json, "sessionId"))} - Tu may: {ShortId(ReadJsonString(json, "fromPcId"))}";
                    break;
                case "session.preserved.offline_guest":
                    eventName = "Giu phien khach (offline)";
                    details = $"Ma phien: {ShortId(ReadJsonString(json, "sessionId"))}";
                    break;
                case "pc.registered":
                    eventName = "Dang ky may tram";
                    details =
                        $"Agent: {ReadJsonStringOrDefault(json, "agentId", "-")} - " +
                        $"Trang thai: {TranslateStatus(ReadJsonString(json, "status"))}";
                    break;
                case "pc.screenshot.requested":
                    eventName = "Yeu cau chup man hinh";
                    details = $"Request: {ShortId(ReadJsonString(json, "requestId"))}";
                    break;
                case "pc.screenshot.captured":
                    eventName = "Da nhan anh man hinh";
                    details =
                        $"Request: {ShortId(ReadJsonString(json, "requestId"))} - " +
                        $"Kich thuoc: {ReadJsonInt(json, "width")}x{ReadJsonInt(json, "height")}";
                    break;
                case "admin.notify.sent":
                    eventName = "Gui thong bao";
                    details = $"Noi dung: {ReadJsonStringOrDefault(json, "message", "-")}";
                    break;
                default:
                    eventName = eventType;
                    details = BuildCompactPayloadSummary(json);
                    break;
            }
        }
        catch
        {
            var rawJson = JsonSerializer.Serialize(item.Payload);
            details = rawJson.Length > 220 ? rawJson[..220] + "..." : rawJson;
        }

        return (eventName, details);
    }

    private static string TranslateStatus(string status)
    {
        return status switch
        {
            "ONLINE" => "Sẵn sàng",
            "IN_USE" => "Đang sử dụng",
            "LOCKED" => "Sẵn sàng",
            "OFFLINE" => "Ngoại tuyến",
            _ => status
        };
    }

    private static string ReadJsonString(JsonElement json, string key) => json.TryGetProperty(key, out var prop) ? prop.GetString() ?? "" : "";

    private static string ReadJsonStringOrDefault(JsonElement json, string key, string fallback)
    {
        var value = ReadJsonString(json, key).Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static decimal ReadJsonDecimal(JsonElement json, string key)
    {
        if (!json.TryGetProperty(key, out var prop)) return 0;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var val)) return val;
        if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var parsed)) return parsed;
        return 0;
    }

    private static bool ReadJsonBool(JsonElement json, string key) => json.TryGetProperty(key, out var prop) && (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False) ? prop.GetBoolean() : false;

    private static int ReadJsonArrayCount(JsonElement json, string key)
    {
        if (!json.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return prop.GetArrayLength();
    }

    private static int ReadJsonInt(JsonElement json, string key)
    {
        if (!json.TryGetProperty(key, out var prop)) return 0;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val)) return val;
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed)) return parsed;
        return 0;
    }

    private static string TranslateCommandType(string commandType)
    {
        return commandType.ToUpperInvariant() switch
        {
            "OPEN" => "Mo may",
            "LOCK" => "Khoa may",
            "SHUTDOWN" => "Tat may",
            "RESTART" => "Khoi dong lai",
            "PAUSE" => "Tam dung",
            "RESUME" => "Tiep tuc",
            "CLOSE_APPS" => "Dong ung dung",
            _ => string.IsNullOrWhiteSpace(commandType) ? "-" : commandType
        };
    }

    private static string ShortId(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "-";
        }

        return trimmed.Length <= 8 ? trimmed : trimmed[..8];
    }

    private static string BuildCompactPayloadSummary(JsonElement json)
    {
        var parts = new List<string>();
        var propertyCount = 0;

        foreach (var prop in json.EnumerateObject())
        {
            propertyCount++;
            if (parts.Count >= 5)
            {
                continue;
            }

            var valueText = prop.Value.ValueKind switch
            {
                JsonValueKind.String => TrimPayloadText(prop.Value.GetString() ?? string.Empty, 40),
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                JsonValueKind.Array => $"{prop.Value.GetArrayLength()} muc",
                JsonValueKind.Object => "{...}",
                _ => prop.Value.GetRawText(),
            };

            parts.Add($"{prop.Name}: {valueText}");
        }

        if (parts.Count == 0)
        {
            return "-";
        }

        var summary = string.Join(" | ", parts);
        if (propertyCount > 5)
        {
            summary += " | ...";
        }

        return summary.Length > 260 ? summary[..260] + "..." : summary;
    }

    private static string TrimPayloadText(string value, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..maxLength] + "...";
    }

    private void ProcessMemberTransferNotifications(IReadOnlyList<SystemEventItem> items)
    {
        var transferEvents = items
            .Where(item =>
                string.Equals(item.EventType, "member.balance.transferred", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Source, "CLIENT", StringComparison.OrdinalIgnoreCase))
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .OrderBy(item => ParseDateLocal(item.CreatedAt) ?? DateTime.MinValue)
            .ToList();

        if (!_memberTransferNotificationsInitialized)
        {
            foreach (var item in transferEvents)
            {
                _memberTransferNotifiedEventIds.Add(item.Id);
            }

            _memberTransferNotificationsInitialized = true;
            return;
        }

        foreach (var item in transferEvents)
        {
            if (!_memberTransferNotifiedEventIds.Add(item.Id))
            {
                continue;
            }

            ShowMemberTransferPopup(item);
            
            // Refresh members list so the balance update is reflected in the UI immediately
            _ = RefreshMembersAsync(forceRefresh: true);
        }
    }

    private void ShowMemberTransferPopup(SystemEventItem item)
    {
        var sourceUsername = ReadPayloadString(item.Payload, "sourceUsername");
        var targetUsername = ReadPayloadString(item.Payload, "targetUsername");
        var amount = ReadPayloadMoney(item.Payload, "amount");

        if (string.IsNullOrWhiteSpace(sourceUsername))
        {
            sourceUsername = I18n.MemberTransferUnknownUser;
        }

        if (string.IsNullOrWhiteSpace(targetUsername))
        {
            targetUsername = I18n.MemberTransferUnknownUser;
        }

        var pcText = BuildMachineText(item);
        var atText = FormatDateTime(item.CreatedAt);
        var content =
            $"Hội viên {sourceUsername} vừa chuyển {amount:N0} VND cho {targetUsername}.\n" +
            $"Máy trạm: {pcText}\n" +
            $"Thời gian: {atText}";

        MessageBox.Show(
            this,
            content,
            I18n.MemberTransferPopupTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        AppendServiceLog(
            $"[{DateTime.Now:HH:mm:ss}] Thông báo chuyển tiền: {sourceUsername} -> {targetUsername} ({amount:N0} VND) tại {pcText}");
    }

    private static string BuildMachineText(SystemEventItem item)
    {
        var hasPcName = !string.IsNullOrWhiteSpace(item.PcName);
        var hasAgentId = !string.IsNullOrWhiteSpace(item.AgentId);

        if (hasPcName && hasAgentId)
        {
            return $"{item.PcName} ({item.AgentId})";
        }

        if (hasPcName)
        {
            return item.PcName!;
        }

        if (hasAgentId)
        {
            return item.AgentId!;
        }

        return I18n.MemberTransferUnknownMachine;
    }

    private static string ReadPayloadString(object? payload, string key)
    {
        if (payload is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!json.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return value.GetString()?.Trim() ?? string.Empty;
    }

    private static decimal ReadPayloadMoney(object? payload, string key)
    {
        if (payload is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return 0m;
        }

        if (!json.TryGetProperty(key, out var value))
        {
            return 0m;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var amount))
        {
            return amount;
        }

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0m;
    }

    private void InitializeSystemLogMachineFilter()
    {
        if (SystemLogMachineFilterComboBox is null)
        {
            return;
        }

        _isUpdatingSystemLogMachineFilter = true;
        SystemLogMachineFilterComboBox.Items.Clear();
        SystemLogMachineFilterComboBox.Items.Add(new ComboBoxItem
        {
            Content = "Tất cả máy",
            Tag = "__all__",
            IsSelected = true
        });
        SystemLogMachineFilterComboBox.SelectedIndex = 0;
        _isUpdatingSystemLogMachineFilter = false;

        MachineTimelineInfoTextBlock.Text = "Chưa có dữ liệu timeline.";
    }

    private void UpdateSystemLogMachineFilterOptions(IReadOnlyList<SystemEventItem> items)
    {
        if (SystemLogMachineFilterComboBox is null)
        {
            return;
        }

        var currentKey = GetSelectedSystemLogMachineKey();
        var machineOptions = items
            .Where(item => !string.IsNullOrWhiteSpace(item.PcName) || !string.IsNullOrWhiteSpace(item.AgentId))
            .GroupBy(BuildSystemLogMachineKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Key = group.Key,
                Label = BuildSystemLogMachineLabel(group.First())
            })
            .OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _isUpdatingSystemLogMachineFilter = true;
        SystemLogMachineFilterComboBox.Items.Clear();
        SystemLogMachineFilterComboBox.Items.Add(new ComboBoxItem
        {
            Content = "Tất cả máy",
            Tag = "__all__",
        });

        foreach (var option in machineOptions)
        {
            SystemLogMachineFilterComboBox.Items.Add(new ComboBoxItem
            {
                Content = option.Label,
                Tag = option.Key
            });
        }

        var selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(currentKey) && !string.Equals(currentKey, "__all__", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1; i < SystemLogMachineFilterComboBox.Items.Count; i++)
            {
                if (SystemLogMachineFilterComboBox.Items[i] is ComboBoxItem item &&
                    item.Tag is string tag &&
                    string.Equals(tag, currentKey, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        SystemLogMachineFilterComboBox.SelectedIndex = selectedIndex;
        _isUpdatingSystemLogMachineFilter = false;
    }

    private string GetSelectedSystemLogMachineKey()
    {
        if (SystemLogMachineFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag;
        }

        return "__all__";
    }

    private string GetSelectedSystemLogMachineLabel()
    {
        if (SystemLogMachineFilterComboBox.SelectedItem is ComboBoxItem item && item.Content is string text)
        {
            return text;
        }

        return "Tất cả máy";
    }

    private static string BuildSystemLogMachineKey(SystemEventItem item)
    {
        var pc = (item.PcName ?? string.Empty).Trim();
        var agent = (item.AgentId ?? string.Empty).Trim();
        return $"{pc}|{agent}";
    }

    private static string BuildSystemLogMachineLabel(SystemEventItem item)
    {
        var pc = (item.PcName ?? string.Empty).Trim();
        var agent = (item.AgentId ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(pc) && !string.IsNullOrWhiteSpace(agent))
        {
            return $"{pc} ({agent})";
        }

        if (!string.IsNullOrWhiteSpace(pc))
        {
            return pc;
        }

        return string.IsNullOrWhiteSpace(agent) ? "-" : agent;
    }

    private void ApplySystemLogMachineFilterAndTimeline(int? originalTotal = null)
    {
        var selectedKey = GetSelectedSystemLogMachineKey();
        var selectedLabel = GetSelectedSystemLogMachineLabel();

        var filtered = string.Equals(selectedKey, "__all__", StringComparison.OrdinalIgnoreCase)
            ? _latestSystemEventItems
            : _latestSystemEventItems
                .Where(item => string.Equals(BuildSystemLogMachineKey(item), selectedKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var sortedDesc = filtered
            .OrderByDescending(item => ParseDateLocal(item.CreatedAt) ?? DateTime.MinValue)
            .ToList();

        _systemLogRows.Clear();
        foreach (var row in sortedDesc.Select(ToSystemLogRow))
        {
            _systemLogRows.Add(row);
        }

        RenderMachineTimeline(sortedDesc);

        var sourceTotal = originalTotal ?? _latestSystemEventItems.Count;
        SystemLogInfoTextBlock.Text =
            $"{I18n.LogsCountPrefix}: {sourceTotal} | Hiển thị: {sortedDesc.Count} | Bộ lọc: {selectedLabel} - {I18n.UpdatedAtPrefix} {DateTime.Now:HH:mm:ss}";
    }

    private void RenderMachineTimeline(IReadOnlyList<SystemEventItem> filteredEvents)
    {
        _machineTimelineRows.Clear();

        var timelineItems = filteredEvents
            .Take(20)
            .OrderBy(item => ParseDateLocal(item.CreatedAt) ?? DateTime.MinValue)
            .ToList();

        foreach (var item in timelineItems)
        {
            var (eventName, details) = TranslateSystemEvent(item);
            _machineTimelineRows.Add(new MachineTimelineRow
            {
                TimeText = FormatDateTime(item.CreatedAt),
                EventText = eventName,
                DetailsText = details,
            });
        }

        if (_machineTimelineRows.Count == 0)
        {
            MachineTimelineInfoTextBlock.Text = "Không có hoạt động theo bộ lọc đang chọn.";
            return;
        }

        MachineTimelineInfoTextBlock.Text = $"{_machineTimelineRows.Count} sự kiện gần nhất theo thứ tự thời gian.";
    }

    private async Task RefreshTransactionLogsAsync()
    {
        try
        {
            var period = GetSelectedRevenuePeriod();
            var anchorDate = GetSelectedRevenueAnchorDate();
            var (rangeStart, rangeEndExclusive) = GetReportRange(period, anchorDate);
            var periodDate = anchorDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var sessionsUrl = BuildApiUrl(
                $"/sessions?status=CLOSED&endedFrom={Uri.EscapeDataString(ToApiDateTime(rangeStart))}&endedTo={Uri.EscapeDataString(ToApiDateTime(rangeEndExclusive))}");
            var revenueUrl = BuildApiUrl(
                $"/reports/revenue/summary?period={Uri.EscapeDataString(period)}&date={Uri.EscapeDataString(periodDate)}");
            var eventsUrl = BuildApiUrl("/reports/events/system?limit=500");

            var sessionsTask = _httpClient.GetFromJsonAsync<SessionsListResponse>(sessionsUrl, JsonOptions());
            var revenueTask = _httpClient.GetFromJsonAsync<RevenueSummaryResponse>(revenueUrl, JsonOptions());
            var eventsTask = _httpClient.GetFromJsonAsync<SystemEventsResponse>(eventsUrl, JsonOptions());

            await Task.WhenAll(sessionsTask!, revenueTask!, eventsTask!);

            var sessionsResponse = await sessionsTask!;
            var revenueResponse = await revenueTask!;
            var eventsResponse = await eventsTask!;

            if (sessionsResponse is null || revenueResponse is null)
            {
                return;
            }

            var sessionRows = sessionsResponse.Items.Select(ToSessionLogRow).ToList();

            var financialRows = new List<SessionLogRow>();
            if (eventsResponse?.Items != null)
            {
                foreach (var evt in eventsResponse.Items)
                {
                    var eventType = evt.EventType?.Trim().ToLowerInvariant() ?? string.Empty;

                    // 1. Nạp tiền trực tiếp từ Admin
                    if (eventType == "member.topup" && evt.Payload is JsonElement json)
                    {
                        var atStr = ReadJsonString(json, "at");
                        var createdAt = ParseDateLocal(string.IsNullOrWhiteSpace(atStr) ? evt.CreatedAt : atStr) ?? DateTime.MinValue;
                        if (createdAt >= rangeStart && createdAt < rangeEndExclusive)
                        {
                            var username = ReadJsonString(json, "username");
                            var amount = ReadJsonDecimal(json, "amount");
                            var note = ReadJsonString(json, "note");
                            var createdBy = ReadJsonString(json, "createdBy");
                            if (string.IsNullOrWhiteSpace(createdBy)) createdBy = "admin";

                            financialRows.Add(new SessionLogRow
                            {
                                TransactionType = "TOPUP",
                                TypeLabel = "Nạp tiền hội viên",
                                TypeIcon = "💵",
                                TypeBg = "#ECFDF5",
                                TypeFg = "#047857",
                                TypeBorder = "#A7F3D0",
                                PcName = $"Hội viên {username}",
                                AgentId = username,
                                StartedAtText = createdAt != DateTime.MinValue ? createdAt.ToString("dd-MM-yyyy HH:mm:ss") : "-",
                                EndedAtText = "-",
                                BillableMinutesText = "-",
                                AmountRaw = amount,
                                AmountText = amount.ToString("N0", CultureInfo.InvariantCulture),
                                Status = "COMPLETED",
                                ClosedReason = string.IsNullOrWhiteSpace(note) ? $"Nạp tiền ({createdBy})" : $"{note} ({createdBy})",
                                CreatedAt = createdAt,
                            });
                        }
                    }
                    // 2. Yêu cầu nạp tiền từ Máy trạm (Client)
                    else if (eventType == "member.topup.requested" && evt.Payload is JsonElement topupReqJson)
                    {
                        var reqStatus = ReadJsonString(topupReqJson, "status")?.Trim().ToUpperInvariant() ?? "PENDING";
                        var approvedAtStr = ReadJsonString(topupReqJson, "approvedAt");
                        var requestedAtStr = ReadJsonString(topupReqJson, "requestedAt");
                        var dateStr = !string.IsNullOrWhiteSpace(approvedAtStr) ? approvedAtStr : (!string.IsNullOrWhiteSpace(requestedAtStr) ? requestedAtStr : evt.CreatedAt);
                        var createdAt = ParseDateLocal(dateStr) ?? DateTime.MinValue;

                        if (createdAt >= rangeStart && createdAt < rangeEndExclusive)
                        {
                            var username = ReadJsonString(topupReqJson, "username");
                            var agentId = ReadJsonString(topupReqJson, "agentId");
                            var pcName = ReadJsonString(topupReqJson, "pcName");
                            var amount = ReadJsonDecimal(topupReqJson, "amount");
                            var approvedBy = ReadJsonString(topupReqJson, "approvedBy");
                            var note = ReadJsonString(topupReqJson, "note");
                            var rejectNote = ReadJsonString(topupReqJson, "rejectNote");

                            var pcTarget = !string.IsNullOrWhiteSpace(agentId) 
                                ? $"{agentId} (Hội viên {username})" 
                                : (!string.IsNullOrWhiteSpace(pcName) ? $"{pcName} (Hội viên {username})" : $"Hội viên {username}");

                            if (reqStatus == "APPROVED")
                            {
                                financialRows.Add(new SessionLogRow
                                {
                                    TransactionType = "TOPUP",
                                    TypeLabel = "Nạp tại máy trạm",
                                    TypeIcon = "💻",
                                    TypeBg = "#ECFDF5",
                                    TypeFg = "#047857",
                                    TypeBorder = "#A7F3D0",
                                    PcName = pcTarget,
                                    AgentId = !string.IsNullOrWhiteSpace(agentId) ? agentId : username,
                                    StartedAtText = createdAt != DateTime.MinValue ? createdAt.ToString("dd-MM-yyyy HH:mm:ss") : "-",
                                    EndedAtText = "-",
                                    BillableMinutesText = "-",
                                    AmountRaw = amount,
                                    AmountText = amount.ToString("N0", CultureInfo.InvariantCulture),
                                    Status = "COMPLETED",
                                    ClosedReason = string.IsNullOrWhiteSpace(note) 
                                        ? $"Yêu cầu từ máy trạm (Duyệt: {(!string.IsNullOrWhiteSpace(approvedBy) ? approvedBy : "admin")})" 
                                        : $"{note} (Duyệt: {(!string.IsNullOrWhiteSpace(approvedBy) ? approvedBy : "admin")})",
                                    CreatedAt = createdAt,
                                });
                            }
                            else if (reqStatus == "REJECTED")
                            {
                                financialRows.Add(new SessionLogRow
                                {
                                    TransactionType = "TOPUP_REJECTED",
                                    TypeLabel = "Từ chối nạp máy",
                                    TypeIcon = "❌",
                                    TypeBg = "#FEF2F2",
                                    TypeFg = "#DC2626",
                                    TypeBorder = "#FECACA",
                                    PcName = pcTarget,
                                    AgentId = !string.IsNullOrWhiteSpace(agentId) ? agentId : username,
                                    StartedAtText = createdAt != DateTime.MinValue ? createdAt.ToString("dd-MM-yyyy HH:mm:ss") : "-",
                                    EndedAtText = "-",
                                    BillableMinutesText = "-",
                                    AmountRaw = 0,
                                    AmountText = amount.ToString("N0", CultureInfo.InvariantCulture),
                                    Status = "REJECTED",
                                    ClosedReason = string.IsNullOrWhiteSpace(rejectNote) ? $"Từ chối bởi {approvedBy}" : rejectNote,
                                    CreatedAt = createdAt,
                                });
                            }
                            else
                            {
                                financialRows.Add(new SessionLogRow
                                {
                                    TransactionType = "TOPUP_PENDING",
                                    TypeLabel = "Chờ duyệt nạp",
                                    TypeIcon = "⏳",
                                    TypeBg = "#FEF3C7",
                                    TypeFg = "#D97706",
                                    TypeBorder = "#FDE68A",
                                    PcName = pcTarget,
                                    AgentId = !string.IsNullOrWhiteSpace(agentId) ? agentId : username,
                                    StartedAtText = createdAt != DateTime.MinValue ? createdAt.ToString("dd-MM-yyyy HH:mm:ss") : "-",
                                    EndedAtText = "-",
                                    BillableMinutesText = "-",
                                    AmountRaw = amount,
                                    AmountText = amount.ToString("N0", CultureInfo.InvariantCulture),
                                    Status = "PENDING",
                                    ClosedReason = "Yêu cầu nạp tiền đang chờ thu ngân duyệt",
                                    CreatedAt = createdAt,
                                });
                            }
                        }
                    }
                    // 3. Yêu cầu rút tiền từ Máy trạm (Client)
                    else if (eventType == "member.withdraw.requested" && evt.Payload is JsonElement withdrawJson)
                    {
                        var wStatus = ReadJsonString(withdrawJson, "status")?.Trim().ToUpperInvariant() ?? "PENDING";
                        var approvedAtStr = ReadJsonString(withdrawJson, "approvedAt");
                        var requestedAtStr = ReadJsonString(withdrawJson, "requestedAt");
                        var dateStr = !string.IsNullOrWhiteSpace(approvedAtStr) ? approvedAtStr : (!string.IsNullOrWhiteSpace(requestedAtStr) ? requestedAtStr : evt.CreatedAt);
                        var createdAt = ParseDateLocal(dateStr) ?? DateTime.MinValue;

                        if (createdAt >= rangeStart && createdAt < rangeEndExclusive)
                        {
                            var username = ReadJsonString(withdrawJson, "username");
                            var agentId = ReadJsonString(withdrawJson, "agentId");
                            var pcName = ReadJsonString(withdrawJson, "pcName");
                            var amount = ReadJsonDecimal(withdrawJson, "amount");
                            var approvedBy = ReadJsonString(withdrawJson, "approvedBy");
                            var note = ReadJsonString(withdrawJson, "note");

                            var pcTarget = !string.IsNullOrWhiteSpace(agentId) 
                                ? $"{agentId} (Hội viên {username})" 
                                : (!string.IsNullOrWhiteSpace(pcName) ? $"{pcName} (Hội viên {username})" : $"Hội viên {username}");

                            if (wStatus == "APPROVED")
                            {
                                financialRows.Add(new SessionLogRow
                                {
                                    TransactionType = "WITHDRAW",
                                    TypeLabel = "Rút tiền tại máy",
                                    TypeIcon = "💸",
                                    TypeBg = "#FEF2F2",
                                    TypeFg = "#DC2626",
                                    TypeBorder = "#FECACA",
                                    PcName = pcTarget,
                                    AgentId = !string.IsNullOrWhiteSpace(agentId) ? agentId : username,
                                    StartedAtText = createdAt != DateTime.MinValue ? createdAt.ToString("dd-MM-yyyy HH:mm:ss") : "-",
                                    EndedAtText = "-",
                                    BillableMinutesText = "-",
                                    AmountRaw = -amount,
                                    AmountText = amount.ToString("N0", CultureInfo.InvariantCulture),
                                    Status = "COMPLETED",
                                    ClosedReason = string.IsNullOrWhiteSpace(note) ? $"Rút tiền tại máy (Duyệt: {approvedBy})" : note,
                                    CreatedAt = createdAt,
                                });
                            }
                        }
                    }
                    // 4. Điều chỉnh số dư
                    else if (eventType == "member.balance.adjusted" && evt.Payload is JsonElement adjJson)
                    {
                        var atStr = ReadJsonString(adjJson, "at");
                        var createdAt = ParseDateLocal(string.IsNullOrWhiteSpace(atStr) ? evt.CreatedAt : atStr) ?? DateTime.MinValue;
                        if (createdAt >= rangeStart && createdAt < rangeEndExclusive)
                        {
                            var username = ReadJsonString(adjJson, "username");
                            var amountDelta = ReadJsonDecimal(adjJson, "amountDelta");
                            var note = ReadJsonString(adjJson, "note");
                            var createdBy = ReadJsonString(adjJson, "createdBy");

                            financialRows.Add(new SessionLogRow
                            {
                                TransactionType = "ADJUST",
                                TypeLabel = amountDelta >= 0 ? "Cộng tiền" : "Trừ tiền",
                                TypeIcon = amountDelta >= 0 ? "➕" : "➖",
                                TypeBg = amountDelta >= 0 ? "#EFF6FF" : "#FEF2F2",
                                TypeFg = amountDelta >= 0 ? "#2563EB" : "#DC2626",
                                TypeBorder = amountDelta >= 0 ? "#BFDBFE" : "#FECACA",
                                PcName = $"Hội viên {username}",
                                AgentId = username,
                                StartedAtText = createdAt != DateTime.MinValue ? createdAt.ToString("dd-MM-yyyy HH:mm:ss") : "-",
                                EndedAtText = "-",
                                BillableMinutesText = "-",
                                AmountRaw = amountDelta,
                                AmountText = Math.Abs(amountDelta).ToString("N0", CultureInfo.InvariantCulture),
                                Status = "COMPLETED",
                                ClosedReason = string.IsNullOrWhiteSpace(note) ? "Điều chỉnh số dư" : note,
                                CreatedAt = createdAt,
                            });
                        }
                    }
                    // 5. Chuyển tiền hội viên
                    else if (eventType == "member.balance.transferred" && evt.Payload is JsonElement transJson)
                    {
                        var transAtStr = ReadJsonString(transJson, "transferredAt");
                        var createdAt = ParseDateLocal(string.IsNullOrWhiteSpace(transAtStr) ? evt.CreatedAt : transAtStr) ?? DateTime.MinValue;
                        if (createdAt >= rangeStart && createdAt < rangeEndExclusive)
                        {
                            var sourceUsername = ReadJsonString(transJson, "sourceUsername");
                            var targetUsername = ReadJsonString(transJson, "targetUsername");
                            var amount = ReadJsonDecimal(transJson, "amount");
                            var note = ReadJsonString(transJson, "note");

                            financialRows.Add(new SessionLogRow
                            {
                                TransactionType = "TRANSFER",
                                TypeLabel = "Chuyển tiền hội viên",
                                TypeIcon = "🔄",
                                TypeBg = "#F3E8FF",
                                TypeFg = "#7E22CE",
                                TypeBorder = "#E9D5FF",
                                PcName = $"{sourceUsername} ➔ {targetUsername}",
                                AgentId = sourceUsername,
                                StartedAtText = createdAt != DateTime.MinValue ? createdAt.ToString("dd-MM-yyyy HH:mm:ss") : "-",
                                EndedAtText = "-",
                                BillableMinutesText = "-",
                                AmountRaw = amount,
                                AmountText = amount.ToString("N0", CultureInfo.InvariantCulture),
                                Status = "COMPLETED",
                                ClosedReason = string.IsNullOrWhiteSpace(note) ? $"Chuyển số dư sang {targetUsername}" : note,
                                CreatedAt = createdAt,
                            });
                        }
                    }
                }
            }

            var allRows = sessionRows.Concat(financialRows)
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            _sessionLogRows.Clear();
            foreach (var row in allRows)
            {
                _sessionLogRows.Add(row);
            }

            var totalTopupAmount = allRows.Where(x => x.TransactionType == "TOPUP").Sum(x => x.AmountRaw);
            var totalSessionAmount = allRows.Where(x => x.TransactionType == "SESSION").Sum(x => x.AmountRaw);
            var totalServiceAmount = revenueResponse.ServiceAmount;
            var totalGrand = totalTopupAmount + totalSessionAmount + totalServiceAmount;

            TransactionSummaryTextBlock.Text =
                $"{revenueResponse.PeriodLabel}: {totalGrand:N0} VND " +
                $"(Nạp hội viên: {totalTopupAmount:N0} + Giờ chơi: {totalSessionAmount:N0} + Dịch vụ: {totalServiceAmount:N0}) - " +
                $"Tổng: {allRows.Count} giao dịch";

            var rangeEndDisplay = rangeEndExclusive.AddSeconds(-1);
            TransactionsInfoTextBlock.Text =
                $"{I18n.UpdatedAtPrefix} lúc {DateTime.Now:HH:mm:ss} (Realtime) - " +
                $"Khoảng: {rangeStart:dd-MM-yyyy HH:mm} đến {rangeEndDisplay:dd-MM-yyyy HH:mm}";
        }
        catch
        {
            // Ignore temporary errors.
        }
    }

    private static SessionLogRow ToSessionLogRow(SessionItem item)
    {
        var endedAt = ParseDateLocal(item.EndedAt);
        var startedAt = ParseDateLocal(item.StartedAt);
        var createdAt = endedAt ?? startedAt ?? DateTime.MinValue;

        return new SessionLogRow
        {
            TransactionType = "SESSION",
            TypeLabel = "Phiên máy",
            TypeIcon = "🖥",
            TypeBg = "#EFF6FF",
            TypeFg = "#2563EB",
            TypeBorder = "#BFDBFE",
            PcName = string.IsNullOrWhiteSpace(item.PcName) ? item.AgentId : item.PcName,
            AgentId = item.AgentId,
            StartedAtText = FormatDateTime(item.StartedAt),
            EndedAtText = FormatDateTime(item.EndedAt),
            BillableMinutesText = item.BillableMinutes.HasValue ? $"{item.BillableMinutes.Value}p" : "-",
            AmountRaw = item.Amount ?? 0,
            AmountText = (item.Amount ?? 0).ToString("N0", CultureInfo.InvariantCulture),
            Status = item.Status,
            ClosedReason = string.IsNullOrWhiteSpace(item.ClosedReason) ? "-" : item.ClosedReason,
            CreatedAt = createdAt,
        };
    }

    private int GetSelectedSystemLogLimit()
    {
        if (SystemLogLimitComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var limit))
        {
            return limit;
        }

        return 100;
    }

    private async void RefreshSystemLogsButton_Click(object sender, RoutedEventArgs e) => await RefreshSystemLogsAsync(forceRefresh: true);

    private async void ClearSystemLogsButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "Ban co chac muon xoa log an toan?\nChi xoa nhom log van hanh, khong xoa log tai chinh/thanh toan.",
            "Xoa log an toan",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            using var response = await _httpClient.DeleteAsync(BuildApiUrl("/reports/events/system"));
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show(
                    this,
                    $"Xoa log an toan that bai ({(int)response.StatusCode}).",
                    "Server Admin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<ClearSystemEventsResponse>(JsonOptions());
            var deletedCount = payload?.DeletedCount ?? 0;

            InvalidateSystemLogsCache();
            _systemLogRows.Clear();
            SystemLogInfoTextBlock.Text = $"Da xoa {deletedCount} dong log an toan - Cap nhat luc {DateTime.Now:HH:mm:ss}";
            await RefreshSystemLogsAsync(forceRefresh: true);

            MessageBox.Show(
                this,
                $"Da xoa {deletedCount} dong log an toan.\nLog quan trong van duoc giu lai.",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Khong the xoa log an toan: {ex.Message}",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void SystemLogsTimer_Tick(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            return;
        }

        if (IsSystemLogsTabActive())
        {
            await RefreshSystemLogsAsync();
        }
        else if (IsTransactionLogsTabActive())
        {
            await RefreshTransactionLogsAsync();
        }
    }

    private async void SystemLogLimitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await RefreshSystemLogsAsync(forceRefresh: true);
    }

    private void SystemLogMachineFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isUpdatingSystemLogMachineFilter)
        {
            return;
        }

        ApplySystemLogMachineFilterAndTimeline();
    }

    private async void QuickRefreshAllDataButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllDataAsync();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã chạy tác vụ nhanh: tải lại toàn bộ dữ liệu");
    }

    private async void QuickRefreshMachinesButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshMachinesAsync();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã chạy tác vụ nhanh: làm mới danh sách máy trạm");
    }

    private async void QuickRefreshSystemLogsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshSystemLogsAsync(forceRefresh: true);
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã chạy tác vụ nhanh: làm mới nhật ký hệ thống");
    }

    private void QuickGoToMachinesTabButton_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedItem = WorkstationsTab;
    }

    private void QuickGoToSystemLogsTabButton_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedItem = LogsTabItem;
        LogsTabControl.SelectedItem = SystemLogsTabItem;
    }

    private async void RefreshTransactionLogsButton_Click(object sender, RoutedEventArgs e) => await RefreshTransactionLogsAsync();

    private async void RevenuePeriodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !_transactionReportInitialized)
        {
            return;
        }

        await RefreshTransactionLogsAsync();
    }

    private async void RevenueAnchorDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !_transactionReportInitialized)
        {
            return;
        }

        await RefreshTransactionLogsAsync();
    }

    private string GetSelectedRevenuePeriod()
    {
        if (RevenuePeriodComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string value &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim().ToLowerInvariant();
        }

        return "day";
    }

    private DateTime GetSelectedRevenueAnchorDate()
    {
        return RevenueAnchorDatePicker.SelectedDate?.Date ?? DateTime.Today;
    }

    private static string ToApiDateTime(DateTime value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static (DateTime Start, DateTime EndExclusive) GetReportRange(string period, DateTime anchorDate)
    {
        var startOfDay = new DateTime(
            anchorDate.Year,
            anchorDate.Month,
            anchorDate.Day,
            0,
            0,
            0,
            DateTimeKind.Local);

        if (period == "week")
        {
            var dayOfWeek = (int)startOfDay.DayOfWeek;
            var offset = dayOfWeek == 0 ? -6 : 1 - dayOfWeek; // Monday start.
            var start = startOfDay.AddDays(offset);
            return (start, start.AddDays(7));
        }

        if (period == "month")
        {
            var start = new DateTime(
                anchorDate.Year,
                anchorDate.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Local);
            return (start, start.AddMonths(1));
        }

        return (startOfDay, startOfDay.AddDays(1));
    }

}


