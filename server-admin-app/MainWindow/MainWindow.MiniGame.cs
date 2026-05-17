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
            MiniGameSpinStatusTextBlock.Text = $"Da tai cau hinh vong quay. Tong hien tai: {totalText}% (cap nhat: {updatedAtText}).";
            MiniGameSpinStatusTextBlock.Foreground = IsChanceTotalValid(total)
                ? Brushes.DarkGreen
                : Brushes.DarkGoldenrod;
        }
        catch
        {
            MiniGameSpinStatusTextBlock.Text = "Khong ket noi duoc backend de tai cau hinh mini game.";
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
        if (!TryValidateMiniGameSpinRows(out var validationMessage))
        {
            MiniGameSpinStatusTextBlock.Text = validationMessage;
            MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        var total = _loyaltySpinSettingRows.Sum(x => x.Chance);

        try
        {
            using var response = await _httpClient.PatchAsJsonAsync(
                BuildApiUrl("/members/loyalty/spin-settings"),
                new
                {
                    items = _loyaltySpinSettingRows
                        .OrderBy(row => row.Minutes)
                        .Select(row => new
                        {
                            minutes = row.Minutes,
                            chance = row.Chance,
                        })
                        .ToList(),
                    updatedBy = "admin.desktop",
                });

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                MiniGameSpinStatusTextBlock.Text = string.IsNullOrWhiteSpace(error)
                    ? $"Luu ti le that bai ({(int)response.StatusCode})."
                    : $"Luu that bai: {error}";
                MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltySpinSettingsResponse>(JsonOptions());
            var totalChance = payload?.TotalChance ?? total;
            MiniGameSpinStatusTextBlock.Text =
                $"Da luu ti le vong quay thanh cong. Tong: {totalChance.ToString("0.####", CultureInfo.InvariantCulture)}%.";
            MiniGameSpinStatusTextBlock.Foreground = Brushes.DarkGreen;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Da luu cau hinh mini game vong quay.");
        }
        catch
        {
            MiniGameSpinStatusTextBlock.Text = "Khong ket noi duoc backend khi luu cau hinh mini game.";
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

        MiniGameSpinStatusTextBlock.Text = "Da dua ve ti le mac dinh. Bam \"Luu ti le\" de ap dung len backend.";
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
            if (TryValidateMiniGameSpinRows(out var validationMessage))
            {
                var total = _loyaltySpinSettingRows.Sum(x => x.Chance);
                MiniGameSpinStatusTextBlock.Text =
                    $"Da chinh ti le. Tong hien tai: {total.ToString("0.####", CultureInfo.InvariantCulture)}%. Bam \"Luu ti le\" de ap dung.";
                MiniGameSpinStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
            }
            else
            {
                MiniGameSpinStatusTextBlock.Text = validationMessage;
                MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private bool TryValidateMiniGameSpinRows(out string message)
    {
        if (_loyaltySpinSettingRows.Count == 0)
        {
            message = "Khong co du lieu de luu.";
            return false;
        }

        var invalidMinute = _loyaltySpinSettingRows.FirstOrDefault(x => x.Minutes < 0 || x.Minutes > 1000);
        if (invalidMinute is not null)
        {
            message = $"Moc thuong khong hop le: {invalidMinute.Minutes}. Chi cho phep tu 0 den 1000 phut.";
            return false;
        }

        var duplicateMinutes = _loyaltySpinSettingRows
            .GroupBy(x => x.Minutes)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateMinutes is not null)
        {
            message = $"Moc thuong {duplicateMinutes.Key} phut dang bi trung. Vui long chinh lai.";
            return false;
        }

        var invalidChance = _loyaltySpinSettingRows.FirstOrDefault(x => x.Chance < 0 || x.Chance > 100);
        if (invalidChance is not null)
        {
            message = "Co ti le khong hop le. Moi moc phai nam trong khoang 0% - 100%.";
            return false;
        }

        var total = _loyaltySpinSettingRows.Sum(x => x.Chance);
        if (!IsChanceTotalValid(total))
        {
            message = $"Tong ti le phai bang 100%. Hien tai: {total.ToString("0.####", CultureInfo.InvariantCulture)}%.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool IsChanceTotalValid(decimal totalChance)
    {
        return Math.Abs(totalChance - 100m) <= 0.0001m;
    }
}
