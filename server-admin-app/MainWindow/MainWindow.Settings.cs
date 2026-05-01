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
    private async Task CheckBackendHealthAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync(BuildApiUrl("/pcs"));
            if (response.IsSuccessStatusCode)
            {
                SetHealthStatus(I18n.BackendOnline, "#16a34a");
                return;
            }

            SetHealthStatus($"Backend: {((int)response.StatusCode)}", "#ef4444");
        }
        catch
        {
            SetHealthStatus(I18n.BackendOffline, "#ef4444");
        }
    }

    private void SetHealthStatus(string text, string colorHex)
    {
        HealthTextBlock.Text = text;
        HealthIndicator.Fill = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private string BuildApiUrl(string path)
    {
        var baseUri = new Uri(_settings.BackendApiBaseUrl.TrimEnd('/') + "/");
        return new Uri(baseUri, path.TrimStart('/')).ToString();
    }

    private string BuildPageUrl(string path)
    {
        var baseUri = new Uri(_settings.AdminBaseUrl.TrimEnd('/') + "/");
        return new Uri(baseUri, path.TrimStart('/')).ToString();
    }

    private static AdminShellSettings LoadSettings()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path))
            {
                return new AdminShellSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AdminShellSettings>(json, JsonOptions()) ?? new AdminShellSettings();
        }
        catch
        {
            return new AdminShellSettings();
        }
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            var outputSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            File.WriteAllText(outputSettingsPath, json);

            var projectSettingsPath = Path.Combine(Environment.CurrentDirectory, "appsettings.json");
            if (!string.Equals(outputSettingsPath, projectSettingsPath, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(projectSettingsPath, json);
            }
        }
        catch
        {
            // Ignore save failures to keep app responsive.
        }
    }

    private static DateTime? ParseDateLocal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, out var parsed) ? parsed.ToLocalTime() : null;
    }

    private static string FormatDateTime(string? value)
    {
        var parsed = ParseDateLocal(value);
        return parsed?.ToString("dd-MM-yyyy HH:mm:ss") ?? "-";
    }

    private static string FormatUsed(int elapsedSeconds)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(elapsedSeconds / 60.0));
        if (minutes >= 60)
        {
            var hours = minutes / 60;
            var remain = minutes % 60;
            return remain == 0 ? $"{hours} gi\u1edd" : $"{hours} gi\u1edd {remain} ph\u00fat";
        }

        return $"{minutes} ph\u00fat";
    }

    private static decimal ParseMoney(string moneyText)
    {
        return decimal.TryParse(moneyText, out var value) ? value : 0m;
    }

    private static double ClampFontSize(double value)
    {
        return Math.Clamp(value, 10, 22);
    }

    private static double ClampMachineTableFontSize(double value)
    {
        return Math.Clamp(value, 11, 30);
    }

    private void ApplyUiFontSize(double value)
    {
        var clamped = ClampFontSize(value);
        FontSize = clamped;
        _settings.UiFontSize = clamped;
        if (FontSizeValueTextBlock is not null)
        {
            FontSizeValueTextBlock.Text = clamped.ToString("0");
        }
    }

    private void ApplyMachineTableFontSize(double value)
    {
        var clamped = ClampMachineTableFontSize(value);
        MachinesDataGrid.FontSize = clamped;
        MachinesDataGrid.RowHeight = Math.Max(24, clamped * 2.1);
        _settings.MachineTableFontSize = clamped;
        if (MachineTableFontSizeValueTextBlock is not null)
        {
            MachineTableFontSizeValueTextBlock.Text = clamped.ToString("0");
        }
    }

    private static double ClampMachineContextMenuPadding(double value)
    {
        return Math.Clamp(value, 6, 24);
    }

    private void ApplyMachineContextMenuPadding(double value)
    {
        var clamped = ClampMachineContextMenuPadding(value);
        _settings.MachineContextMenuItemPadding = clamped;
        Resources["MachineContextMenuItemPadding"] = new Thickness(clamped);
        if (MachineContextMenuPaddingValueTextBlock is not null)
        {
            MachineContextMenuPaddingValueTextBlock.Text = clamped.ToString("0");
        }
    }

    private static double ClampMachineContextMenuFontSize(double value)
    {
        return Math.Clamp(value, 10, 30);
    }

    private void ApplyMachineContextMenuFontSize(double value)
    {
        var clamped = ClampMachineContextMenuFontSize(value);
        _settings.MachineContextMenuFontSize = clamped;
        Resources["MachineContextMenuFontSize"] = clamped;
        if (MachineContextMenuFontSizeValueTextBlock is not null)
        {
            MachineContextMenuFontSizeValueTextBlock.Text = clamped.ToString("0");
        }
    }

    private async Task LoadClientRuntimeSettingsAsync()
    {
        try
        {
            _isLoadingReadyShutdownSettings = true;
            var response = await _httpClient.GetFromJsonAsync<ClientRuntimeSettingsResponse>(
                BuildApiUrl("/pricing/client-settings"),
                JsonOptions());

            if (response is null)
            {
                ReadyAutoShutdownStatusTextBlock.Text = "Không tải được cài đặt tự tắt máy trạm.";
                ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            _readyAutoShutdownMinutes = Math.Clamp(response.ReadyAutoShutdownMinutes, 1, 240);
            ReadyAutoShutdownMinutesTextBox.Text = _readyAutoShutdownMinutes.ToString(CultureInfo.InvariantCulture);
            ReadyAutoShutdownStatusTextBlock.Text =
                $"Đang bật: máy Sẵn sàng không login quá {_readyAutoShutdownMinutes} phút sẽ tự tắt.";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.DarkGreen;
        }
        catch
        {
            ReadyAutoShutdownStatusTextBlock.Text = "Không kết nối được backend để đọc cài đặt tự tắt.";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
        }
        finally
        {
            _isLoadingReadyShutdownSettings = false;
        }
    }

    private async Task SaveClientRuntimeSettingsAsync()
    {
        if (!TryParseReadyAutoShutdownMinutes(out var minutes))
        {
            ReadyAutoShutdownStatusTextBlock.Text = "Số phút tự tắt không hợp lệ (1 - 240).";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        try
        {
            using var response = await _httpClient.PatchAsJsonAsync(
                BuildApiUrl("/pricing/client-settings"),
                new
                {
                    readyAutoShutdownMinutes = minutes,
                });

            if (!response.IsSuccessStatusCode)
            {
                ReadyAutoShutdownStatusTextBlock.Text =
                    $"Lưu cài đặt tự tắt thất bại ({(int)response.StatusCode}).";
                ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
                AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lưu cài đặt tự tắt thất bại");
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<ClientRuntimeSettingsResponse>(JsonOptions());
            _readyAutoShutdownMinutes = Math.Clamp(payload?.ReadyAutoShutdownMinutes ?? minutes, 1, 240);
            ReadyAutoShutdownMinutesTextBox.Text = _readyAutoShutdownMinutes.ToString(CultureInfo.InvariantCulture);
            ReadyAutoShutdownStatusTextBlock.Text =
                $"Đã lưu: máy Sẵn sàng không login quá {_readyAutoShutdownMinutes} phút sẽ tự tắt.";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.DarkGreen;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã lưu tự tắt máy trạm: {_readyAutoShutdownMinutes} phút");
        }
        catch
        {
            ReadyAutoShutdownStatusTextBlock.Text = "Không kết nối được backend khi lưu cài đặt tự tắt.";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi kết nối khi lưu cài đặt tự tắt");
        }
    }

    private bool TryParseReadyAutoShutdownMinutes(out int minutes)
    {
        var raw = ReadyAutoShutdownMinutesTextBox.Text.Trim();
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes) ||
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out minutes))
        {
            if (minutes is >= 1 and <= 240)
            {
                return true;
            }
        }

        minutes = 0;
        return false;
    }

    private void ReadyAutoShutdownMinutesTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_readyShutdownSettingsInitialized || _isLoadingReadyShutdownSettings)
        {
            return;
        }

        if (TryParseReadyAutoShutdownMinutes(out var minutes))
        {
            ReadyAutoShutdownStatusTextBlock.Text =
                $"Đã thay đổi thành {minutes} phút. Bấm \"Lưu cài đặt\" để áp dụng.";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
            return;
        }

        ReadyAutoShutdownStatusTextBlock.Text = "Số phút tự tắt không hợp lệ (1 - 240).";
        ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
    }

    private async Task LoadLoyaltySettingsAsync()
    {
        try
        {
            _isLoadingLoyaltySettings = true;

            var response = await _httpClient.GetFromJsonAsync<LoyaltySettingsResponse>(
                BuildApiUrl("/members/loyalty/settings"),
                JsonOptions());

            if (response is null)
            {
                LoyaltySettingsStatusTextBlock.Text = "Không tải được cài đặt điểm tích lũy.";
                LoyaltySettingsStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            LoyaltyPointsEnabledCheckBox.IsChecked = response.Enabled;
            LoyaltySettingsStatusTextBlock.Text = response.Enabled
                ? $"Đang bật tích lũy: {response.MinutesPerPoint} phút = 1 điểm, 1 điểm = {response.PointsToMinutes} phút chơi."
                : "Đang tắt tích lũy điểm cho hội viên.";
            LoyaltySettingsStatusTextBlock.Foreground = response.Enabled
                ? Brushes.DarkGreen
                : Brushes.DimGray;
        }
        catch
        {
            LoyaltySettingsStatusTextBlock.Text = "Không kết nối được backend để đọc cài đặt điểm tích lũy.";
            LoyaltySettingsStatusTextBlock.Foreground = Brushes.Firebrick;
        }
        finally
        {
            _isLoadingLoyaltySettings = false;
        }
    }

    private async Task SaveLoyaltySettingsAsync()
    {
        var enabled = LoyaltyPointsEnabledCheckBox.IsChecked == true;

        try
        {
            using var response = await _httpClient.PatchAsJsonAsync(
                BuildApiUrl("/members/loyalty/settings"),
                new
                {
                    enabled,
                    updatedBy = "admin.desktop",
                });

            if (!response.IsSuccessStatusCode)
            {
                LoyaltySettingsStatusTextBlock.Text = $"Lưu cài đặt điểm thất bại ({(int)response.StatusCode}).";
                LoyaltySettingsStatusTextBlock.Foreground = Brushes.Firebrick;
                AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lưu cài đặt điểm tích lũy thất bại");
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<LoyaltySettingsResponse>(JsonOptions());
            var minutesPerPoint = payload?.MinutesPerPoint ?? 15;
            var pointsToMinutes = payload?.PointsToMinutes ?? 1;
            LoyaltySettingsStatusTextBlock.Text = enabled
                ? $"Đã bật tích lũy: {minutesPerPoint} phút = 1 điểm, 1 điểm = {pointsToMinutes} phút chơi."
                : "Đã tắt tích lũy điểm cho hội viên.";
            LoyaltySettingsStatusTextBlock.Foreground = enabled ? Brushes.DarkGreen : Brushes.DimGray;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã lưu cài đặt điểm tích lũy: {(enabled ? "BẬT" : "TẮT")}");
        }
        catch
        {
            LoyaltySettingsStatusTextBlock.Text = "Không kết nối được backend khi lưu cài đặt điểm tích lũy.";
            LoyaltySettingsStatusTextBlock.Foreground = Brushes.Firebrick;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi kết nối khi lưu cài đặt điểm tích lũy");
        }
    }

    private void LoyaltyPointsEnabledCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loyaltySettingsInitialized || _isLoadingLoyaltySettings)
        {
            return;
        }

        LoyaltySettingsStatusTextBlock.Text =
            "Đã thay đổi trạng thái tích lũy điểm. Bấm \"Lưu cài đặt\" để áp dụng lên backend.";
        LoyaltySettingsStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
    }

    private void LoyaltyPointsEnabledCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_loyaltySettingsInitialized || _isLoadingLoyaltySettings)
        {
            return;
        }

        LoyaltySettingsStatusTextBlock.Text =
            "Đã thay đổi trạng thái tích lũy điểm. Bấm \"Lưu cài đặt\" để áp dụng lên backend.";
        LoyaltySettingsStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
    }


    private void AppendServiceLog(string message)
    {
        var existing = ServiceOutputTextBox.Text;
        ServiceOutputTextBox.Text = string.IsNullOrWhiteSpace(existing)
            ? message
            : existing + Environment.NewLine + message;
        ServiceOutputTextBox.ScrollToEnd();
    }

    private async void HealthTimer_Tick(object? sender, EventArgs e) => await CheckBackendHealthAsync();

    private void OpenPcsWebButton_Click(object sender, RoutedEventArgs e) => OpenExternalPage("/pcs");

    private void OpenMembersWebButton_Click(object sender, RoutedEventArgs e) => OpenExternalPage("/members");

    private async void CheckBackendNowButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckBackendHealthAsync();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] {HealthTextBlock.Text}");
    }

    private async void RefreshAllDataButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllDataAsync();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] \u0110\u00e3 t\u1ea3i l\u1ea1i to\u00e0n b\u1ed9 d\u1eef li\u1ec7u");
    }

    private void OpenExternalPage(string path)
    {
        var url = BuildPageUrl(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });

        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] M\u1edf {url}");
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_fontSizeInitialized)
        {
            return;
        }

        ApplyUiFontSize(e.NewValue);
    }

    private void MachineTableFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_machineTableFontSizeInitialized)
        {
            return;
        }

        ApplyMachineTableFontSize(e.NewValue);
    }

    private void MachineContextMenuPaddingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_machineContextMenuPaddingInitialized)
        {
            return;
        }

        ApplyMachineContextMenuPadding(e.NewValue);
    }

    private void MachineContextMenuFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_machineContextMenuFontSizeInitialized)
        {
            return;
        }

        ApplyMachineContextMenuFontSize(e.NewValue);
    }

    private async void SaveFontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        await SaveClientRuntimeSettingsAsync();
        await SaveLoyaltySettingsAsync();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] \u0110\u00e3 l\u01b0u: c\u1ee1 ch\u1eef app = {_settings.UiFontSize:0}, b\u1ea3ng m\u00e1y = {_settings.MachineTableFontSize:0}, padding menu = {_settings.MachineContextMenuItemPadding:0}, ch\u1eef menu = {_settings.MachineContextMenuFontSize:0}, t\u1ef1 t\u1eaft = {_readyAutoShutdownMinutes} ph\u00fat");
    }

    private void ResetFontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        _fontSizeInitialized = false;
        FontSizeSlider.Value = 13;
        _fontSizeInitialized = true;
        ApplyUiFontSize(13);

        _machineTableFontSizeInitialized = false;
        MachineTableFontSizeSlider.Value = 14;
        _machineTableFontSizeInitialized = true;
        ApplyMachineTableFontSize(14);

        _machineContextMenuPaddingInitialized = false;
        MachineContextMenuPaddingSlider.Value = 12;
        _machineContextMenuPaddingInitialized = true;
        ApplyMachineContextMenuPadding(12);

        _machineContextMenuFontSizeInitialized = false;
        MachineContextMenuFontSizeSlider.Value = 14;
        _machineContextMenuFontSizeInitialized = true;
        ApplyMachineContextMenuFontSize(14);

        SaveSettings();
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] \u0110\u00e3 \u0111\u1eb7t l\u1ea1i c\u1ee1 ch\u1eef m\u1eb7c \u0111\u1ecbnh");
    }
}
