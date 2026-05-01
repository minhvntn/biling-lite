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
    private async Task RefreshSystemLogsAsync()
    {
        try
        {
            var limit = GetSelectedSystemLogLimit();
            var response = await _httpClient.GetFromJsonAsync<SystemEventsResponse>(
                BuildApiUrl($"/reports/events/system?limit={limit}"),
                JsonOptions());

            if (response is null)
            {
                return;
            }

            _systemLogRows.Clear();
            foreach (var item in response.Items.Select(ToSystemLogRow))
            {
                _systemLogRows.Add(item);
            }

            SystemLogInfoTextBlock.Text = $"{I18n.LogsCountPrefix}: {response.Total} - {I18n.UpdatedAtPrefix} {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            // Ignore temporary errors.
        }
    }

    private static SystemLogRow ToSystemLogRow(SystemEventItem item)
    {
        var payloadText = "-";
        if (item.Payload is not null)
        {
            var json = JsonSerializer.Serialize(item.Payload);
            payloadText = json.Length > 220 ? json[..220] + "..." : json;
        }

        var pcText = item.PcName is null
            ? "-"
            : string.IsNullOrWhiteSpace(item.AgentId)
                ? item.PcName
                : $"{item.PcName} ({item.AgentId})";

        return new SystemLogRow
        {
            CreatedAtText = FormatDateTime(item.CreatedAt),
            Source = item.Source,
            EventType = item.EventType,
            PcText = pcText,
            PayloadText = payloadText,
        };
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

    private async void RefreshSystemLogsButton_Click(object sender, RoutedEventArgs e) => await RefreshSystemLogsAsync();

    private async void SystemLogLimitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await RefreshSystemLogsAsync();
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

