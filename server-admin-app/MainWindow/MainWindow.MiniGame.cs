using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class MainWindow
{
    private static readonly IReadOnlyList<LoyaltySpinSettingRow> DefaultSpinSettings =
    [
        new() { Minutes = 0, Chance = 28m, Label = "0p" },
        new() { Minutes = 1, Chance = 18m, Label = "1p" },
        new() { Minutes = 2, Chance = 15m, Label = "2p" },
        new() { Minutes = 4, Chance = 12m, Label = "4p" },
        new() { Minutes = 6, Chance = 10m, Label = "6p" },
        new() { Minutes = 8, Chance = 7m, Label = "8p" },
        new() { Minutes = 10, Chance = 5m, Label = "10p" },
        new() { Minutes = 15, Chance = 3m, Label = "15p" },
        new() { Minutes = 20, Chance = 1.5m, Label = "20p" },
        new() { Minutes = 30, Chance = 0.5m, Label = "30p" },
    ];

    private void InitializeMiniGameTab()
    {
        MiniGameSpinSettingsDataGrid.ItemsSource = _loyaltySpinSettingRows;
    }

    private async void RefreshMiniGameSpinSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshMiniGameSpinSettingsAsync();
    }

    private async Task RefreshMiniGameSpinSettingsAsync()
    {
        try
        {
            _isLoadingLoyaltySpinSettings = true;
            var response = await _httpClient.GetFromJsonAsync<LoyaltySpinSettingsResponse>(
                BuildApiUrl("/members/loyalty/spin-settings"),
                JsonOptions());

            var sourceItems = response?.Items is { Count: > 0 }
                ? response.Items
                : DefaultSpinSettings.Select(item => new LoyaltySpinSettingItem
                {
                    Minutes = item.Minutes,
                    Chance = item.Chance,
                    Label = item.Label
                }).ToList();

            _loyaltySpinSettingRows.Clear();
            foreach (var item in sourceItems.OrderBy(x => x.Minutes))
            {
                _loyaltySpinSettingRows.Add(new LoyaltySpinSettingRow
                {
                    Minutes = item.Minutes,
                    Chance = item.Chance,
                    Label = string.IsNullOrWhiteSpace(item.Label) ? $"{item.Minutes}p" : item.Label
                });
            }

            var total = _loyaltySpinSettingRows.Sum(x => x.Chance);
            var totalText = total.ToString("0.####", CultureInfo.InvariantCulture);
            var updatedAtText = FormatDateTime(response?.UpdatedAt);
            MiniGameSpinStatusTextBlock.Text = $"Đã tải cấu hình vòng quay. Tổng hiện tại: {totalText}% (cập nhật: {updatedAtText}).";
            MiniGameSpinStatusTextBlock.Foreground = IsChanceTotalValid(total)
                ? Brushes.DarkGreen
                : Brushes.DarkGoldenrod;
        }
        catch
        {
            MiniGameSpinStatusTextBlock.Text = "Không kết nối được backend để tải cấu hình mini game.";
            MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
        }
        finally
        {
            _isLoadingLoyaltySpinSettings = false;
        }
    }

    private async void SaveMiniGameSpinSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveMiniGameSpinSettingsAsync();
    }

    private async Task SaveMiniGameSpinSettingsAsync()
    {
        if (_loyaltySpinSettingRows.Count == 0)
        {
            MiniGameSpinStatusTextBlock.Text = "Không có dữ liệu để lưu.";
            MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        var total = _loyaltySpinSettingRows.Sum(x => x.Chance);
        if (!IsChanceTotalValid(total))
        {
            MiniGameSpinStatusTextBlock.Text =
                $"Tổng tỉ lệ phải bằng 100%. Hiện tại: {total.ToString("0.####", CultureInfo.InvariantCulture)}%.";
            MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        try
        {
            using var response = await _httpClient.PatchAsJsonAsync(
                BuildApiUrl("/members/loyalty/spin-settings"),
                new
                {
                    items = _loyaltySpinSettingRows.Select(row => new
                    {
                        minutes = row.Minutes,
                        chance = row.Chance,
                    }).ToList(),
                    updatedBy = "admin.desktop",
                });

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                MiniGameSpinStatusTextBlock.Text = string.IsNullOrWhiteSpace(error)
                    ? $"Lưu tỉ lệ thất bại ({(int)response.StatusCode})."
                    : $"Lưu thất bại: {error}";
                MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltySpinSettingsResponse>(JsonOptions());
            var totalChance = payload?.TotalChance ?? total;
            MiniGameSpinStatusTextBlock.Text =
                $"Đã lưu tỉ lệ vòng quay thành công. Tổng: {totalChance.ToString("0.####", CultureInfo.InvariantCulture)}%.";
            MiniGameSpinStatusTextBlock.Foreground = Brushes.DarkGreen;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã lưu cấu hình mini game vòng quay.");
        }
        catch
        {
            MiniGameSpinStatusTextBlock.Text = "Không kết nối được backend khi lưu cấu hình mini game.";
            MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
        }
    }

    private void ResetMiniGameSpinDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        _loyaltySpinSettingRows.Clear();
        foreach (var item in DefaultSpinSettings)
        {
            _loyaltySpinSettingRows.Add(new LoyaltySpinSettingRow
            {
                Minutes = item.Minutes,
                Chance = item.Chance,
                Label = item.Label
            });
        }

        MiniGameSpinStatusTextBlock.Text = "Đã đưa về tỉ lệ mặc định. Bấm \"Lưu tỉ lệ\" để áp dụng lên backend.";
        MiniGameSpinStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
    }

    private void MiniGameSpinSettingsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (!_loyaltySpinSettingsInitialized || _isLoadingLoyaltySpinSettings)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            var total = _loyaltySpinSettingRows.Sum(x => x.Chance);
            if (IsChanceTotalValid(total))
            {
                MiniGameSpinStatusTextBlock.Text =
                    $"Đã chỉnh tỉ lệ. Tổng hiện tại: {total.ToString("0.####", CultureInfo.InvariantCulture)}%. Bấm \"Lưu tỉ lệ\" để áp dụng.";
                MiniGameSpinStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
            }
            else
            {
                MiniGameSpinStatusTextBlock.Text =
                    $"Tổng tỉ lệ đang là {total.ToString("0.####", CultureInfo.InvariantCulture)}%. Cần chỉnh về đúng 100%.";
                MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static bool IsChanceTotalValid(decimal totalChance)
    {
        return Math.Abs(totalChance - 100m) <= 0.0001m;
    }
}
