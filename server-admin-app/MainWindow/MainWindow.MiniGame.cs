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

    private bool _isSyncingAutoInputs;
    private bool _isUpdatingHorseRaceInputs;

    private void InitializeMiniGameTab()
    {
        MiniGameSpinSettingsDataGrid.ItemsSource = _loyaltySpinSettingRows;
        UpdateSpinTotalDisplay();
        // Trigger fetch of horse race settings and lucky envelope settings without blocking initialization
        _ = RefreshHorseRaceSettingsAsync();
        _ = RefreshLuckyEnvelopeSettingsAsync();
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
            UpdateSpinTotalDisplay();
        }
        catch
        {
            MiniGameSpinStatusTextBlock.Text = "Không kết nối được backend để tải cấu hình mini game.";
            MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
            UpdateSpinTotalDisplay();
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
            UpdateSpinTotalDisplay();
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
            UpdateSpinTotalDisplay();
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

        UpdateSpinTotalDisplay();
        MiniGameSpinStatusTextBlock.Text = "Đã đưa về tỉ lệ mặc định. Bấm \"Lưu tỉ lệ\" để áp dụng lên máy chủ.";
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
            UpdateSpinTotalDisplay();
            if (TryValidateMiniGameSpinRows(out var validationMessage))
            {
                var total = _loyaltySpinSettingRows.Sum(x => x.Chance);
                MiniGameSpinStatusTextBlock.Text =
                    $"Đã chỉnh tỉ lệ. Tổng hiện tại: {total.ToString("0.####", CultureInfo.InvariantCulture)}%. Bấm \"Lưu tỉ lệ\" để áp dụng.";
                MiniGameSpinStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
            }
            else
            {
                MiniGameSpinStatusTextBlock.Text = validationMessage;
                MiniGameSpinStatusTextBlock.Foreground = Brushes.Firebrick;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void UpdateSpinTotalDisplay()
    {
        var total = _loyaltySpinSettingRows.Sum(x => x.Chance);
        var isValid = IsChanceTotalValid(total);

        if (SpinTotalPercentTextBlock != null)
        {
            SpinTotalPercentTextBlock.Text = $"{total:0.####}%";
            SpinTotalPercentTextBlock.Foreground = isValid
                ? new SolidColorBrush(Color.FromRgb(22, 163, 74))
                : new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }

        if (MiniGameSpinTotalProgressBar != null)
        {
            MiniGameSpinTotalProgressBar.Value = (double)Math.Clamp(total, 0m, 100m);
            MiniGameSpinTotalProgressBar.Foreground = isValid
                ? new SolidColorBrush(Color.FromRgb(22, 163, 74))
                : new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }

        if (SpinTotalBadgeBorder != null && SpinTotalBadgeTextBlock != null)
        {
            if (isValid)
            {
                SpinTotalBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
                SpinTotalBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208));
                SpinTotalBadgeTextBlock.Text = "✓ Tổng: 100.0% (Hợp lệ)";
                SpinTotalBadgeTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            }
            else
            {
                var diff = 100m - total;
                var diffText = diff > 0 ? $"Thiếu {diff:0.####}%" : $"Thừa {Math.Abs(diff):0.####}%";
                SpinTotalBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                SpinTotalBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                SpinTotalBadgeTextBlock.Text = $"⚠️ {diffText} (Chưa đủ 100%)";
                SpinTotalBadgeTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            }
        }
    }

    private void AutoMinChanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingAutoInputs || AutoMinChanceTextBox == null) return;
        _isSyncingAutoInputs = true;
        try
        {
            AutoMinChanceTextBox.Text = e.NewValue.ToString("0.#", CultureInfo.InvariantCulture);
        }
        finally
        {
            _isSyncingAutoInputs = false;
        }
    }

    private void AutoMinChanceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingAutoInputs || AutoMinChanceSlider == null) return;
        if (decimal.TryParse(AutoMinChanceTextBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            _isSyncingAutoInputs = true;
            try
            {
                AutoMinChanceSlider.Value = (double)Math.Clamp(val, 1m, 90m);
            }
            finally
            {
                _isSyncingAutoInputs = false;
            }
        }
    }

    private void AutoJackpotChanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingAutoInputs || AutoJackpotChanceTextBox == null) return;
        _isSyncingAutoInputs = true;
        try
        {
            AutoJackpotChanceTextBox.Text = e.NewValue.ToString("0.##", CultureInfo.InvariantCulture);
        }
        finally
        {
            _isSyncingAutoInputs = false;
        }
    }

    private void AutoJackpotChanceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingAutoInputs || AutoJackpotChanceSlider == null) return;
        if (decimal.TryParse(AutoJackpotChanceTextBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            _isSyncingAutoInputs = true;
            try
            {
                AutoJackpotChanceSlider.Value = (double)Math.Clamp(val, 0.01m, 20m);
            }
            finally
            {
                _isSyncingAutoInputs = false;
            }
        }
    }

    private void ApplyAutoCalcButton_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(AutoMinChanceTextBox?.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var baseChance0p))
        {
            baseChance0p = 28m;
        }

        if (!decimal.TryParse(AutoJackpotChanceTextBox?.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var jackpotChance))
        {
            jackpotChance = 0.5m;
        }

        bool isExponential = AutoCurveTypeComboBox?.SelectedIndex != 1;
        ApplyAutoSpinCalculation(baseChance0p, jackpotChance, isExponential);
    }

    private void ApplyPresetEasyButton_Click(object sender, RoutedEventArgs e)
    {
        _isSyncingAutoInputs = true;
        try
        {
            if (AutoMinChanceTextBox != null) AutoMinChanceTextBox.Text = "18";
            if (AutoMinChanceSlider != null) AutoMinChanceSlider.Value = 18;
            if (AutoJackpotChanceTextBox != null) AutoJackpotChanceTextBox.Text = "1.0";
            if (AutoJackpotChanceSlider != null) AutoJackpotChanceSlider.Value = 1.0;
        }
        finally
        {
            _isSyncingAutoInputs = false;
        }
        ApplyAutoSpinCalculation(18m, 1.0m, true);
    }

    private void ApplyPresetNormalButton_Click(object sender, RoutedEventArgs e)
    {
        _isSyncingAutoInputs = true;
        try
        {
            if (AutoMinChanceTextBox != null) AutoMinChanceTextBox.Text = "28";
            if (AutoMinChanceSlider != null) AutoMinChanceSlider.Value = 28;
            if (AutoJackpotChanceTextBox != null) AutoJackpotChanceTextBox.Text = "0.5";
            if (AutoJackpotChanceSlider != null) AutoJackpotChanceSlider.Value = 0.5;
        }
        finally
        {
            _isSyncingAutoInputs = false;
        }
        ApplyAutoSpinCalculation(28m, 0.5m, true);
    }

    private void ApplyPresetHardButton_Click(object sender, RoutedEventArgs e)
    {
        _isSyncingAutoInputs = true;
        try
        {
            if (AutoMinChanceTextBox != null) AutoMinChanceTextBox.Text = "38";
            if (AutoMinChanceSlider != null) AutoMinChanceSlider.Value = 38;
            if (AutoJackpotChanceTextBox != null) AutoJackpotChanceTextBox.Text = "0.2";
            if (AutoJackpotChanceSlider != null) AutoJackpotChanceSlider.Value = 0.2;
        }
        finally
        {
            _isSyncingAutoInputs = false;
        }
        ApplyAutoSpinCalculation(38m, 0.2m, true);
    }

    private void ApplyAutoSpinCalculation(decimal baseChance0p, decimal jackpotChance, bool isExponential)
    {
        if (_loyaltySpinSettingRows.Count == 0) return;

        var sortedRows = _loyaltySpinSettingRows.OrderBy(x => x.Minutes).ToList();
        int n = sortedRows.Count;
        if (n <= 1)
        {
            sortedRows[0].Chance = 100m;
            UpdateSpinTotalDisplay();
            return;
        }

        baseChance0p = Math.Clamp(baseChance0p, 1m, 90m);
        jackpotChance = Math.Clamp(jackpotChance, 0.01m, 20m);

        if (baseChance0p + jackpotChance >= 99.5m)
        {
            baseChance0p = 50m;
            jackpotChance = 0.5m;
        }

        decimal targetMid = 100m - baseChance0p - jackpotChance;
        int midCount = n - 2;

        double[] weights = new double[midCount];
        double c0 = (double)baseChance0p;
        double cn = (double)jackpotChance;

        for (int i = 0; i < midCount; i++)
        {
            double t = (double)(i + 1) / (n - 1);
            if (isExponential)
            {
                weights[i] = Math.Pow(cn / c0, t);
            }
            else
            {
                weights[i] = Math.Max(0.01, c0 - (t * (c0 - cn)));
            }
        }

        double sumWeights = weights.Sum();
        if (sumWeights <= 0.00001) sumWeights = 1.0;

        sortedRows[0].Chance = Math.Round(baseChance0p, 1);
        decimal currentSum = sortedRows[0].Chance;

        for (int i = 0; i < midCount; i++)
        {
            var val = Math.Round((decimal)(weights[i] / sumWeights * (double)targetMid), 1);
            if (val < 0.1m) val = 0.1m;
            sortedRows[i + 1].Chance = val;
            currentSum += val;
        }

        sortedRows[n - 1].Chance = Math.Round(jackpotChance, 1);
        currentSum += sortedRows[n - 1].Chance;

        decimal diff = 100.0m - currentSum;
        if (diff != 0m && midCount > 0)
        {
            sortedRows[1].Chance = Math.Round(sortedRows[1].Chance + diff, 1);
        }
        else if (diff != 0m)
        {
            sortedRows[0].Chance = Math.Round(sortedRows[0].Chance + diff, 1);
        }

        UpdateSpinTotalDisplay();
        MiniGameSpinStatusTextBlock.Text = $"Đã tự động tính tỉ lệ (Mốc 0p: {baseChance0p}%, Mốc {sortedRows[n - 1].Minutes}p: {jackpotChance}%). Tổng: {_loyaltySpinSettingRows.Sum(x => x.Chance):0.####}%. Bấm \"Lưu tỉ lệ\" để áp dụng.";
        MiniGameSpinStatusTextBlock.Foreground = Brushes.DarkGreen;
    }

    private void BalanceSpinChancesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loyaltySpinSettingRows.Count == 0) return;

        var total = _loyaltySpinSettingRows.Sum(x => x.Chance);
        if (IsChanceTotalValid(total))
        {
            MiniGameSpinStatusTextBlock.Text = "Tổng tỉ lệ đã chuẩn 100.0%, không cần bù trừ.";
            MiniGameSpinStatusTextBlock.Foreground = Brushes.DarkGreen;
            return;
        }

        var zeroRow = _loyaltySpinSettingRows.FirstOrDefault(x => x.Minutes == 0) ?? _loyaltySpinSettingRows.First();
        var diff = 100.0m - total;
        var newChance = zeroRow.Chance + diff;

        if (newChance >= 0m)
        {
            zeroRow.Chance = Math.Round(newChance, 4);
        }
        else
        {
            var factor = 100.0m / total;
            decimal sum = 0m;
            foreach (var row in _loyaltySpinSettingRows)
            {
                row.Chance = Math.Round(row.Chance * factor, 1);
                sum += row.Chance;
            }
            var rem = 100.0m - sum;
            zeroRow.Chance = Math.Round(zeroRow.Chance + rem, 1);
        }

        UpdateSpinTotalDisplay();
        MiniGameSpinStatusTextBlock.Text = $"Đã tự động bù trừ về 100.0% (Điều chỉnh mốc {zeroRow.Minutes}p). Bấm \"Lưu tỉ lệ\" để áp dụng.";
        MiniGameSpinStatusTextBlock.Foreground = Brushes.DarkGreen;
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

    private class HorseRaceSettingsResponse
    {
        public double Top1Multiplier { get; set; }
        public double Top2Multiplier { get; set; }
        public double Top3Multiplier { get; set; }
    }

    private async void RefreshHorseRaceSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshHorseRaceSettingsAsync();
    }

    private async Task RefreshHorseRaceSettingsAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<HorseRaceSettingsResponse>(
                BuildApiUrl("/members/loyalty/horse-race-settings"),
                JsonOptions());

            if (response != null)
            {
                SetHorseRaceValues(response.Top1Multiplier, response.Top2Multiplier, response.Top3Multiplier);
                
                HorseRaceStatusTextBlock.Text = "Đã tải cấu hình đua ngựa.";
                HorseRaceStatusTextBlock.Foreground = Brushes.DarkGreen;
            }
        }
        catch
        {
            HorseRaceStatusTextBlock.Text = "Không kết nối được backend để tải cấu hình đua ngựa.";
            HorseRaceStatusTextBlock.Foreground = Brushes.Firebrick;
        }
    }

    private async void SaveHorseRaceSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(HorseRaceTop1TextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var t1) ||
            !double.TryParse(HorseRaceTop2TextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var t2) ||
            !double.TryParse(HorseRaceTop3TextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var t3))
        {
            HorseRaceStatusTextBlock.Text = "Định dạng số không hợp lệ. Vui lòng nhập số hợp lệ.";
            HorseRaceStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        try
        {
            using var response = await _httpClient.PatchAsJsonAsync(
                BuildApiUrl("/members/loyalty/horse-race-settings"),
                new
                {
                    top1Multiplier = t1,
                    top2Multiplier = t2,
                    top3Multiplier = t3
                });

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                HorseRaceStatusTextBlock.Text = $"Lưu thất bại: {error}";
                HorseRaceStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            HorseRaceStatusTextBlock.Text = "Đã lưu cấu hình đua ngựa thành công.";
            HorseRaceStatusTextBlock.Foreground = Brushes.DarkGreen;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã lưu cấu hình đua ngựa.");
        }
        catch
        {
            HorseRaceStatusTextBlock.Text = "Không kết nối được backend khi lưu cấu hình.";
            HorseRaceStatusTextBlock.Foreground = Brushes.Firebrick;
        }
    }

    private void ResetHorseRaceDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        SetHorseRaceValues(3.0, 2.25, 1.5);
        HorseRaceStatusTextBlock.Text = "Đã đưa về mặc định (x3.0 / x2.25 / x1.5). Bấm \"Lưu cấu hình\" để áp dụng.";
        HorseRaceStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
    }

    private void HorseRaceTop1Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingHorseRaceInputs || HorseRaceTop1TextBox == null) return;
        _isUpdatingHorseRaceInputs = true;
        try
        {
            HorseRaceTop1TextBox.Text = Math.Round(e.NewValue, 2).ToString("0.##", CultureInfo.InvariantCulture);
            UpdateHorseRaceSimulator();
            if (HorseRaceStatusTextBlock != null)
            {
                HorseRaceStatusTextBlock.Text = "Đã thay đổi. Bấm \"Lưu cấu hình\" để áp dụng.";
                HorseRaceStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
            }
        }
        finally
        {
            _isUpdatingHorseRaceInputs = false;
        }
    }

    private void HorseRaceTop2Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingHorseRaceInputs || HorseRaceTop2TextBox == null) return;
        _isUpdatingHorseRaceInputs = true;
        try
        {
            HorseRaceTop2TextBox.Text = Math.Round(e.NewValue, 2).ToString("0.##", CultureInfo.InvariantCulture);
            UpdateHorseRaceSimulator();
            if (HorseRaceStatusTextBlock != null)
            {
                HorseRaceStatusTextBlock.Text = "Đã thay đổi. Bấm \"Lưu cấu hình\" để áp dụng.";
                HorseRaceStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
            }
        }
        finally
        {
            _isUpdatingHorseRaceInputs = false;
        }
    }

    private void HorseRaceTop3Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingHorseRaceInputs || HorseRaceTop3TextBox == null) return;
        _isUpdatingHorseRaceInputs = true;
        try
        {
            HorseRaceTop3TextBox.Text = Math.Round(e.NewValue, 2).ToString("0.##", CultureInfo.InvariantCulture);
            UpdateHorseRaceSimulator();
            if (HorseRaceStatusTextBlock != null)
            {
                HorseRaceStatusTextBlock.Text = "Đã thay đổi. Bấm \"Lưu cấu hình\" để áp dụng.";
                HorseRaceStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
            }
        }
        finally
        {
            _isUpdatingHorseRaceInputs = false;
        }
    }

    private void HorseRaceSimBetTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateHorseRaceSimulator();
    }

    private void UpdateHorseRaceSimulator()
    {
        if (HorseRaceSimTop1PayoutTextBlock == null || HorseRaceSimTop2PayoutTextBlock == null || HorseRaceSimTop3PayoutTextBlock == null)
            return;

        decimal bet = 10000m;
        if (HorseRaceSimBetTextBox != null && decimal.TryParse(HorseRaceSimBetTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedBet))
        {
            bet = Math.Max(0, parsedBet);
        }

        double.TryParse(HorseRaceTop1TextBox?.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var t1);
        double.TryParse(HorseRaceTop2TextBox?.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var t2);
        double.TryParse(HorseRaceTop3TextBox?.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var t3);

        decimal p1 = bet * (decimal)Math.Max(0, t1);
        decimal p2 = bet * (decimal)Math.Max(0, t2);
        decimal p3 = bet * (decimal)Math.Max(0, t3);

        HorseRaceSimTop1PayoutTextBlock.Text = $"{p1:N0} VNĐ";
        HorseRaceSimTop2PayoutTextBlock.Text = $"{p2:N0} VNĐ";
        HorseRaceSimTop3PayoutTextBlock.Text = $"{p3:N0} VNĐ";
    }

    private void ApplyHorseRacePresetHigh_Click(object sender, RoutedEventArgs e)
    {
        SetHorseRaceValues(4.0, 2.5, 1.8);
        if (HorseRaceStatusTextBlock != null)
        {
            HorseRaceStatusTextBlock.Text = "Đã chọn cấu hình \"Thưởng lớn\" (x4.0 / x2.5 / x1.8). Bấm \"Lưu cấu hình\" để áp dụng.";
            HorseRaceStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
        }
    }

    private void ApplyHorseRacePresetStandard_Click(object sender, RoutedEventArgs e)
    {
        SetHorseRaceValues(3.0, 2.25, 1.5);
        if (HorseRaceStatusTextBlock != null)
        {
            HorseRaceStatusTextBlock.Text = "Đã chọn cấu hình \"Tiêu chuẩn\" (x3.0 / x2.25 / x1.5). Bấm \"Lưu cấu hình\" để áp dụng.";
            HorseRaceStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
        }
    }

    private void ApplyHorseRacePresetSafe_Click(object sender, RoutedEventArgs e)
    {
        SetHorseRaceValues(2.0, 1.5, 1.2);
        if (HorseRaceStatusTextBlock != null)
        {
            HorseRaceStatusTextBlock.Text = "Đã chọn cấu hình \"An toàn\" (x2.0 / x1.5 / x1.2). Bấm \"Lưu cấu hình\" để áp dụng.";
            HorseRaceStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
        }
    }

    private void SetHorseRaceValues(double top1, double top2, double top3)
    {
        _isUpdatingHorseRaceInputs = true;
        try
        {
            if (HorseRaceTop1TextBox != null) HorseRaceTop1TextBox.Text = top1.ToString("0.##", CultureInfo.InvariantCulture);
            if (HorseRaceTop2TextBox != null) HorseRaceTop2TextBox.Text = top2.ToString("0.##", CultureInfo.InvariantCulture);
            if (HorseRaceTop3TextBox != null) HorseRaceTop3TextBox.Text = top3.ToString("0.##", CultureInfo.InvariantCulture);

            if (HorseRaceTop1Slider != null) HorseRaceTop1Slider.Value = Math.Clamp(top1, HorseRaceTop1Slider.Minimum, HorseRaceTop1Slider.Maximum);
            if (HorseRaceTop2Slider != null) HorseRaceTop2Slider.Value = Math.Clamp(top2, HorseRaceTop2Slider.Minimum, HorseRaceTop2Slider.Maximum);
            if (HorseRaceTop3Slider != null) HorseRaceTop3Slider.Value = Math.Clamp(top3, HorseRaceTop3Slider.Minimum, HorseRaceTop3Slider.Maximum);

            UpdateHorseRaceSimulator();
        }
        finally
        {
            _isUpdatingHorseRaceInputs = false;
        }
    }

    private void HorseRaceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingHorseRaceInputs) return;

        _isUpdatingHorseRaceInputs = true;
        try
        {
            if (sender == HorseRaceTop1TextBox && double.TryParse(HorseRaceTop1TextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var t1))
            {
                if (HorseRaceTop1Slider != null) HorseRaceTop1Slider.Value = Math.Clamp(t1, HorseRaceTop1Slider.Minimum, HorseRaceTop1Slider.Maximum);
            }
            else if (sender == HorseRaceTop2TextBox && double.TryParse(HorseRaceTop2TextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var t2))
            {
                if (HorseRaceTop2Slider != null) HorseRaceTop2Slider.Value = Math.Clamp(t2, HorseRaceTop2Slider.Minimum, HorseRaceTop2Slider.Maximum);
            }
            else if (sender == HorseRaceTop3TextBox && double.TryParse(HorseRaceTop3TextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var t3))
            {
                if (HorseRaceTop3Slider != null) HorseRaceTop3Slider.Value = Math.Clamp(t3, HorseRaceTop3Slider.Minimum, HorseRaceTop3Slider.Maximum);
            }

            UpdateHorseRaceSimulator();

            if (HorseRaceStatusTextBlock != null)
            {
                HorseRaceStatusTextBlock.Text = "Đã thay đổi. Bấm \"Lưu cấu hình\" để áp dụng.";
                HorseRaceStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
            }
        }
        finally
        {
            _isUpdatingHorseRaceInputs = false;
        }
    }

    private async void RefreshLuckyEnvelopeSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLuckyEnvelopeSettingsAsync();
    }

    private async Task RefreshLuckyEnvelopeSettingsAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<LuckyEnvelopeSettingsResponse>(
                BuildApiUrl("/members/loyalty/lucky-envelope/settings"),
                JsonOptions());

            if (response != null)
            {
                LuckyEnvelopeEnabledCheckBox.IsChecked = response.Enabled;

                if (!string.IsNullOrWhiteSpace(response.StartDate) && DateTime.TryParse(response.StartDate, out var startDate))
                {
                    LuckyEnvelopeStartDatePicker.SelectedDate = startDate.ToLocalTime();
                }
                else
                {
                    LuckyEnvelopeStartDatePicker.SelectedDate = null;
                }

                if (!string.IsNullOrWhiteSpace(response.EndDate) && DateTime.TryParse(response.EndDate, out var endDate))
                {
                    LuckyEnvelopeEndDatePicker.SelectedDate = endDate.ToLocalTime();
                }
                else
                {
                    LuckyEnvelopeEndDatePicker.SelectedDate = null;
                }

                LuckyEnvelopePointCostTextBox.Text = response.PointCost.ToString(CultureInfo.InvariantCulture);
                LuckyEnvelopeMinMinutesTextBox.Text = response.MinMinutes.ToString(CultureInfo.InvariantCulture);
                LuckyEnvelopeMaxMinutesTextBox.Text = response.MaxMinutes.ToString(CultureInfo.InvariantCulture);
                LuckyEnvelopeMaxClaimsTextBox.Text = response.MaxClaims.ToString(CultureInfo.InvariantCulture);

                var activeStatus = response.IsActive ? "Đang diễn ra" : (response.Reason ?? "Đang đóng");
                LuckyEnvelopeStatusTextBlock.Text = $"Đã tải cấu hình Lì Xì Tết. Trạng thái: {activeStatus}.";
                LuckyEnvelopeStatusTextBlock.Foreground = response.IsActive ? Brushes.DarkGreen : Brushes.DarkGoldenrod;
            }
        }
        catch
        {
            LuckyEnvelopeStatusTextBlock.Text = "Không kết nối được backend để tải cấu hình Lì Xì Tết.";
            LuckyEnvelopeStatusTextBlock.Foreground = Brushes.Firebrick;
        }
    }

    private async void SaveLuckyEnvelopeSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LuckyEnvelopePointCostTextBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var pointCost) || pointCost < 1)
        {
            LuckyEnvelopeStatusTextBlock.Text = "Số điểm đổi phải là số nguyên >= 1.";
            LuckyEnvelopeStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        if (!int.TryParse(LuckyEnvelopeMinMinutesTextBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var minMinutes) || minMinutes < 1)
        {
            LuckyEnvelopeStatusTextBlock.Text = "Phút tối thiểu phải là số nguyên >= 1.";
            LuckyEnvelopeStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        if (!int.TryParse(LuckyEnvelopeMaxMinutesTextBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var maxMinutes) || maxMinutes < minMinutes)
        {
            LuckyEnvelopeStatusTextBlock.Text = "Phút tối đa phải lớn hơn hoặc bằng phút tối thiểu.";
            LuckyEnvelopeStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        if (!int.TryParse(LuckyEnvelopeMaxClaimsTextBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var maxClaims) || maxClaims < 0)
        {
            LuckyEnvelopeStatusTextBlock.Text = "Giới hạn số lần nhận phải là số nguyên >= 0 (0 = không giới hạn).";
            LuckyEnvelopeStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        var startDate = LuckyEnvelopeStartDatePicker.SelectedDate?.ToUniversalTime().ToString("o");
        var endDate = LuckyEnvelopeEndDatePicker.SelectedDate?.Date.AddDays(1).AddTicks(-1).ToUniversalTime().ToString("o");

        if (LuckyEnvelopeStartDatePicker.SelectedDate != null &&
            LuckyEnvelopeEndDatePicker.SelectedDate != null &&
            LuckyEnvelopeStartDatePicker.SelectedDate > LuckyEnvelopeEndDatePicker.SelectedDate)
        {
            LuckyEnvelopeStatusTextBlock.Text = "Ngày bắt đầu không được lớn hơn ngày kết thúc.";
            LuckyEnvelopeStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        try
        {
            var payload = new
            {
                enabled = LuckyEnvelopeEnabledCheckBox.IsChecked == true,
                startDate,
                endDate,
                pointCost,
                minMinutes,
                maxMinutes,
                maxClaims,
            };

            using var response = await _httpClient.PutAsJsonAsync(
                BuildApiUrl("/members/loyalty/lucky-envelope/settings"),
                payload);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                LuckyEnvelopeStatusTextBlock.Text = $"Lưu thất bại: {error}";
                LuckyEnvelopeStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            LuckyEnvelopeStatusTextBlock.Text = "Đã lưu cấu hình Lì Xì Tết thành công.";
            LuckyEnvelopeStatusTextBlock.Foreground = Brushes.DarkGreen;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã lưu cấu hình Sự kiện Lì Xì Tết.");
            await RefreshLuckyEnvelopeSettingsAsync();
        }
        catch
        {
            LuckyEnvelopeStatusTextBlock.Text = "Không kết nối được backend khi lưu cấu hình.";
            LuckyEnvelopeStatusTextBlock.Foreground = Brushes.Firebrick;
        }
    }
}

public class LuckyEnvelopeSettingsResponse
{
    public bool Enabled { get; set; }
    public bool IsActive { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public int PointCost { get; set; }
    public int MinMinutes { get; set; }
    public int MaxMinutes { get; set; }
    public int MaxClaims { get; set; }
    public string? Reason { get; set; }
    public string? UpdatedAt { get; set; }
}
