using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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
    private async Task LoadWebsiteLogSettingsAsync()
    {
        try
        {
            _isLoadingWebsiteLogSettings = true;
            var response = await _httpClient.GetFromJsonAsync<WebsiteLogSettingsResponse>(
                BuildApiUrl("/website-logs/settings"),
                JsonOptions());

            if (response is null)
            {
                WebsiteLogsStatusTextBlock.Text = "Không tải được cài đặt nhật ký website.";
                WebsiteLogsStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            WebsiteLogsEnabledCheckBox.IsChecked = response.Enabled;
            WebsiteLogsStatusTextBlock.Text =
                $"Cài đặt hiện tại: {(response.Enabled ? "Đang bật" : "Đang tắt")} - cập nhật {FormatDateTime(response.UpdatedAt)} bởi {response.UpdatedBy}.";
            WebsiteLogsStatusTextBlock.Foreground = Brushes.DarkGreen;
        }
        catch
        {
            WebsiteLogsStatusTextBlock.Text = "Không kết nối được backend để đọc cài đặt nhật ký website.";
            WebsiteLogsStatusTextBlock.Foreground = Brushes.Firebrick;
        }
        finally
        {
            _isLoadingWebsiteLogSettings = false;
        }
    }

    private async Task SaveWebsiteLogSettingsAsync()
    {
        var enabled = WebsiteLogsEnabledCheckBox.IsChecked == true;
        try
        {
            using var response = await _httpClient.PutAsJsonAsync(
                BuildApiUrl("/website-logs/settings"),
                new
                {
                    enabled,
                    updatedBy = "admin.desktop",
                });

            if (!response.IsSuccessStatusCode)
            {
                WebsiteLogsStatusTextBlock.Text = $"Lưu cài đặt nhật ký website thất bại ({(int)response.StatusCode}).";
                WebsiteLogsStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<WebsiteLogSettingsResponse>(JsonOptions());
            var updatedAtText = payload is null ? DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss") : FormatDateTime(payload.UpdatedAt);
            WebsiteLogsStatusTextBlock.Text =
                $"Đã lưu cài đặt nhật ký website: {(enabled ? "Bật" : "Tắt")} - cập nhật {updatedAtText}.";
            WebsiteLogsStatusTextBlock.Foreground = Brushes.DarkGreen;
        }
        catch (Exception ex)
        {
            WebsiteLogsStatusTextBlock.Text = $"Lỗi lưu cài đặt nhật ký website: {ex.Message}";
            WebsiteLogsStatusTextBlock.Foreground = Brushes.Firebrick;
        }
    }

    private async Task RefreshWebsiteLogsAsync()
    {
        try
        {
            var limit = GetSelectedWebsiteLogLimit();
            var search = WebsiteLogSearchTextBox.Text.Trim();
            var pcId = GetSelectedWebsiteLogPcId();
            var (fromUtc, toUtc) = GetWebsiteLogRange();

            var queryPairs = new List<string>
            {
                $"limit={limit}",
            };

            if (!string.IsNullOrWhiteSpace(pcId))
            {
                queryPairs.Add($"pcId={Uri.EscapeDataString(pcId)}");
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                queryPairs.Add($"search={Uri.EscapeDataString(search)}");
            }

            if (fromUtc.HasValue)
            {
                queryPairs.Add(
                    $"from={Uri.EscapeDataString(fromUtc.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))}");
            }

            if (toUtc.HasValue)
            {
                queryPairs.Add(
                    $"to={Uri.EscapeDataString(toUtc.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))}");
            }

            var url = BuildApiUrl($"/website-logs?{string.Join("&", queryPairs)}");
            var response = await _httpClient.GetFromJsonAsync<WebsiteLogsResponse>(url, JsonOptions());
            if (response is null)
            {
                return;
            }

            _websiteLogRows.Clear();
            foreach (var row in response.Items.Select(ToWebsiteLogRow))
            {
                _websiteLogRows.Add(row);
            }

            WebsiteLogsInfoTextBlock.Text =
                $"Số dòng: {response.Total} - Cập nhật lúc {DateTime.Now:HH:mm:ss}";
        }
        catch
        {
            // Ignore temporary errors to keep UI responsive.
        }
    }

    private static WebsiteLogRow ToWebsiteLogRow(WebsiteLogItem item)
    {
        var visitedAt = !string.IsNullOrWhiteSpace(item.VisitedAt)
            ? item.VisitedAt
            : item.CreatedAt;

        return new WebsiteLogRow
        {
            Id = item.Id,
            PcName = string.IsNullOrWhiteSpace(item.PcName) ? "-" : item.PcName!,
            AgentId = string.IsNullOrWhiteSpace(item.AgentId) ? "-" : item.AgentId!,
            Domain = string.IsNullOrWhiteSpace(item.Domain) ? "-" : item.Domain,
            Url = string.IsNullOrWhiteSpace(item.Url) ? "-" : item.Url,
            Title = string.IsNullOrWhiteSpace(item.Title) ? "-" : item.Title,
            Browser = string.IsNullOrWhiteSpace(item.Browser) ? "-" : item.Browser,
            VisitedAtText = FormatDateTime(visitedAt),
        };
    }

    private void RefreshWebsiteLogMachineFilterOptions()
    {
        if (WebsiteLogPcFilterComboBox is null)
        {
            return;
        }

        var selectedPcId = GetSelectedWebsiteLogPcId();

        try
        {
            _isUpdatingWebsiteLogMachineFilters = true;
            WebsiteLogPcFilterComboBox.Items.Clear();

            WebsiteLogPcFilterComboBox.Items.Add(new ComboBoxItem
            {
                Content = "Tất cả máy",
                Tag = string.Empty,
            });

            foreach (var machine in _allMachineRows.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                WebsiteLogPcFilterComboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{machine.Name} ({machine.AgentId})",
                    Tag = machine.Id,
                });
            }

            ComboBoxItem? matched = null;
            if (!string.IsNullOrWhiteSpace(selectedPcId))
            {
                matched = WebsiteLogPcFilterComboBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), selectedPcId, StringComparison.OrdinalIgnoreCase));
            }

            WebsiteLogPcFilterComboBox.SelectedItem =
                matched ?? WebsiteLogPcFilterComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _isUpdatingWebsiteLogMachineFilters = false;
        }
    }

    private string? GetSelectedWebsiteLogPcId()
    {
        if (WebsiteLogPcFilterComboBox.SelectedItem is not ComboBoxItem item)
        {
            return null;
        }

        var value = item.Tag?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private int GetSelectedWebsiteLogLimit()
    {
        if (WebsiteLogLimitComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var limit))
        {
            return limit;
        }

        return 300;
    }

    private (DateTime? FromUtc, DateTime? ToUtc) GetWebsiteLogRange()
    {
        var fromDate = WebsiteLogFromDatePicker.SelectedDate?.Date;
        var toDate = WebsiteLogToDatePicker.SelectedDate?.Date;

        if (!fromDate.HasValue && !toDate.HasValue)
        {
            return (null, null);
        }

        if (fromDate.HasValue && !toDate.HasValue)
        {
            toDate = fromDate;
        }
        else if (!fromDate.HasValue && toDate.HasValue)
        {
            fromDate = toDate;
        }

        if (fromDate!.Value > toDate!.Value)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        var from = new DateTime(fromDate.Value.Year, fromDate.Value.Month, fromDate.Value.Day, 0, 0, 0, DateTimeKind.Local);
        var to = fromDate.Value == toDate.Value
            ? from.AddDays(1)
            : new DateTime(toDate.Value.Year, toDate.Value.Month, toDate.Value.Day, 0, 0, 0, DateTimeKind.Local).AddDays(1);

        return (from, to);
    }

    private void MarkWebsiteLogSettingsPendingChange()
    {
        if (!_websiteLogSettingsInitialized || _isLoadingWebsiteLogSettings)
        {
            return;
        }

        WebsiteLogsStatusTextBlock.Text = "Đã thay đổi cài đặt. Bấm \"Lưu áp dụng\" để cập nhật xuống máy trạm.";
        WebsiteLogsStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
    }

    private async void RefreshWebsiteLogsButton_Click(object sender, RoutedEventArgs e) => await RefreshWebsiteLogsAsync();

    private async void ReloadWebsiteLogSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadWebsiteLogSettingsAsync();
        await RefreshWebsiteLogsAsync();
    }

    private async void SaveWebsiteLogSettingsButton_Click(object sender, RoutedEventArgs e) => await SaveWebsiteLogSettingsAsync();

    private async void WebsiteLogPcFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !_websiteLogFiltersInitialized || _isUpdatingWebsiteLogMachineFilters)
        {
            return;
        }

        await RefreshWebsiteLogsAsync();
    }

    private async void WebsiteLogSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || !_websiteLogFiltersInitialized)
        {
            return;
        }

        await RefreshWebsiteLogsAsync();
    }

    private async void WebsiteLogFromDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !_websiteLogFiltersInitialized)
        {
            return;
        }

        await RefreshWebsiteLogsAsync();
    }

    private async void WebsiteLogToDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !_websiteLogFiltersInitialized)
        {
            return;
        }

        await RefreshWebsiteLogsAsync();
    }

    private async void WebsiteLogLimitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !_websiteLogFiltersInitialized)
        {
            return;
        }

        await RefreshWebsiteLogsAsync();
    }

    private void WebsiteLogsEnabledCheckBox_Checked(object sender, RoutedEventArgs e) => MarkWebsiteLogSettingsPendingChange();

    private void WebsiteLogsEnabledCheckBox_Unchecked(object sender, RoutedEventArgs e) => MarkWebsiteLogSettingsPendingChange();
}

