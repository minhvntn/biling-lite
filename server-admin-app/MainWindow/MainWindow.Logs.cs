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
            "LOCKED" => "Đang khóa",
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

            var sessionsTask = _httpClient.GetFromJsonAsync<SessionsListResponse>(sessionsUrl, JsonOptions());
            var revenueTask = _httpClient.GetFromJsonAsync<RevenueSummaryResponse>(revenueUrl, JsonOptions());
            await Task.WhenAll(sessionsTask!, revenueTask!);

            var sessionsResponse = await sessionsTask!;
            var revenueResponse = await revenueTask!;

            if (sessionsResponse is null || revenueResponse is null)
            {
                return;
            }

            var rows = sessionsResponse.Items.Select(ToSessionLogRow).ToList();
            _sessionLogRows.Clear();
            foreach (var row in rows)
            {
                _sessionLogRows.Add(row);
            }

            TransactionSummaryTextBlock.Text =
                $"{revenueResponse.PeriodLabel}: {revenueResponse.TotalAmount:N0} VND " +
                $"(Giờ chơi: {revenueResponse.SessionAmount:N0} + Dịch vụ: {revenueResponse.ServiceAmount:N0}) - " +
                $"{I18n.SessionCountPrefix}: {revenueResponse.ClosedSessions} - Đơn dịch vụ: {revenueResponse.ServiceOrders}";

            var rangeEndDisplay = rangeEndExclusive.AddSeconds(-1);
            TransactionsInfoTextBlock.Text =
                $"{I18n.UpdatedAtPrefix} lúc {DateTime.Now:HH:mm:ss} - " +
                $"Khoảng: {rangeStart:dd-MM-yyyy HH:mm} đến {rangeEndDisplay:dd-MM-yyyy HH:mm}";
        }
        catch
        {
            // Ignore temporary errors.
        }
    }

    private static SessionLogRow ToSessionLogRow(SessionItem item)
    {
        return new SessionLogRow
        {
            PcName = item.PcName,
            AgentId = item.AgentId,
            StartedAtText = FormatDateTime(item.StartedAt),
            EndedAtText = FormatDateTime(item.EndedAt),
            BillableMinutesText = item.BillableMinutes?.ToString() ?? "-",
            AmountRaw = item.Amount ?? 0,
            AmountText = (item.Amount ?? 0).ToString("N0", CultureInfo.InvariantCulture),
            Status = item.Status,
            ClosedReason = string.IsNullOrWhiteSpace(item.ClosedReason) ? "-" : item.ClosedReason,
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
        if (!IsSystemLogsTabActive() || WindowState == WindowState.Minimized)
        {
            return;
        }

        await RefreshSystemLogsAsync();
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


