using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Server.Admin.App;

public partial class MainWindow
{
    private readonly ObservableCollection<LoyaltyRankRow> _loyaltyRankRows = new();

    private void InitializeLoyaltyRanksTab()
    {
        LoyaltyRanksDataGrid.ItemsSource = _loyaltyRankRows;
    }

    private async void RefreshLoyaltyRanksButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLoyaltyRanksAsync();
    }

    private async Task RefreshLoyaltyRanksAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<LoyaltyRankItem>>(
                BuildApiUrl("/members/loyalty/ranks"),
                JsonOptions());

            if (response is null)
            {
                return;
            }

            _loyaltyRankRows.Clear();
            foreach (var item in response.OrderBy(x => x.MinTopup))
            {
                _loyaltyRankRows.Add(new LoyaltyRankRow
                {
                    Id = item.Id,
                    RankName = item.RankName,
                    MinTopup = item.MinTopup,
                    BonusPercent = item.BonusPercent,
                    MinutesPerPoint = item.MinutesPerPoint,
                    MinTopupText = item.MinTopup.ToString("N0", CultureInfo.InvariantCulture)
                });
            }

            LoyaltyRanksInfoTextBlock.Text = $"[\u0110\u00e3 t\u1ea3i {response.Count} b\u1eadc h\u1ea1ng l\u00fac {DateTime.Now:HH:mm:ss}] S\u1eeda tr\u1ef1c ti\u1ebfp v\u00e0o b\u1ea3ng \u0111\u1ec3 c\u1eadp nh\u1eadt.";
            LoyaltyRanksInfoTextBlock.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kh\u00f4ng th\u1ec3 t\u1ea3i danh s\u00e1ch rank: {ex.Message}", "L\u1ed7i", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoyaltyRanksDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        var row = e.Row.Item as LoyaltyRankRow;
        if (row is null)
        {
            return;
        }

        // We need to wait a bit for the value to be committed to the object, 
        // or get it from the editing element.
        // For simplicity, we'll trigger the update after a short delay or in the next event loop.
        await Task.Delay(100);

        await UpdateLoyaltyRankAsync(row);
    }

    private async Task UpdateLoyaltyRankAsync(LoyaltyRankRow row)
    {
        try
        {
            // Parse MinTopupText back to decimal
            if (decimal.TryParse(row.MinTopupText.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var minTopup))
            {
                row.MinTopup = minTopup;
            }

            var payload = new
            {
                rankName = row.RankName,
                minTopup = row.MinTopup,
                bonusPercent = row.BonusPercent,
                minutesPerPoint = row.MinutesPerPoint
            };

            var response = await _httpClient.PatchAsJsonAsync(
                BuildApiUrl($"/members/loyalty/ranks/{row.Id}"),
                payload,
                JsonOptions());

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                MessageBox.Show($"C\u1eadp nh\u1eadt th\u1ea5t b\u1ea1i: {err}", "L\u1ed7i", MessageBoxButton.OK, MessageBoxImage.Warning);
                await RefreshLoyaltyRanksAsync(); // Revert
                return;
            }

            LoyaltyRanksInfoTextBlock.Text = $"[\u0110\u00e3 l\u01b0u {row.RankName} l\u00fac {DateTime.Now:HH:mm:ss}]";
            LoyaltyRanksInfoTextBlock.Foreground = System.Windows.Media.Brushes.Blue;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"L\u1ed7i khi c\u1eadp nh\u1eadt: {ex.Message}");
            await RefreshLoyaltyRanksAsync(); // Revert
        }
    }

    private async void RebuildLoyaltyRanksButton_Click(object sender, RoutedEventArgs e)
    {
        var input = RebuildMaxThresholdTextBox.Text.Replace(",", "").Trim();
        if (!decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxThreshold) || maxThreshold <= 0)
        {
            MessageBox.Show("Vui lòng nhập ngưỡng tối đa hợp lệ (ví dụ: 100,000,000).", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Hành động này sẽ XÓA TOÀN BỘ và TẠO MỚI lại 100 cấp bậc VIP dựa trên ngưỡng {maxThreshold:N0} VNĐ.\n\nBạn có chắc chắn muốn tiếp tục?",
            "Xác nhận thiết lập nhanh",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            LoyaltyRanksInfoTextBlock.Text = "Đang chia lại cấp bậc, vui lòng đợi...";
            LoyaltyRanksInfoTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;

            var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl("/members/loyalty/ranks/rebuild"),
                new { maxThreshold });

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                MessageBox.Show($"Thiết lập thất bại: {err}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("Đã chia lại 100 cấp bậc VIP thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshLoyaltyRanksAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi kết nối: {ex.Message}");
        }
    }
}

