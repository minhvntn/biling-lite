using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

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

    private void UpdateServerIpDisplay()
    {
        if (ServerIpTextBlock is null)
        {
            return;
        }

        var backendHostText = string.Empty;
        if (Uri.TryCreate(_settings.BackendApiBaseUrl, UriKind.Absolute, out var backendUri) &&
            !string.IsNullOrWhiteSpace(backendUri.Host))
        {
            backendHostText = backendUri.IsDefaultPort
                ? backendUri.Host
                : $"{backendUri.Host}:{backendUri.Port}";
        }

        var lanIps = Dns.GetHostAddresses(Dns.GetHostName())
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            .Select(ip => ip.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ip => ip)
            .ToList();

        var ipText = lanIps.Count == 0 ? "-" : string.Join(", ", lanIps);
        ServerIpTextBlock.Text = string.IsNullOrWhiteSpace(backendHostText)
            ? $"Server IP: {ipText}"
            : $"Server IP: {ipText} | API: {backendHostText}";
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString
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

            UpdateServerIpDisplay();
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
                ReadyAutoShutdownStatusTextBlock.Text = "Khong tai duoc cai dat tu tat may tram.";
                ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
                LockScreenBackgroundStatusTextBlock.Text = "Khong tai duoc cai dat nen lock screen.";
                LockScreenBackgroundStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            _readyAutoShutdownMinutes = Math.Clamp(response.ReadyAutoShutdownMinutes, 1, 240);
            _lockScreenBackgroundMode = NormalizeLockScreenBackgroundMode(response.LockScreenBackgroundMode);
            _lockScreenBackgroundUrl = (response.LockScreenBackgroundUrl ?? string.Empty).Trim();

            ReadyAutoShutdownMinutesTextBox.Text = _readyAutoShutdownMinutes.ToString(CultureInfo.InvariantCulture);
            SetLockScreenBackgroundModeUi(_lockScreenBackgroundMode);
            LockScreenBackgroundUrlTextBox.Text = _lockScreenBackgroundUrl;

            ReadyAutoShutdownStatusTextBlock.Text =
                $"Dang bat: may San sang khong login qua {_readyAutoShutdownMinutes} phut se tu tat.";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.DarkGreen;
            LockScreenBackgroundStatusTextBlock.Text =
                DescribeLockScreenBackground(_lockScreenBackgroundMode, _lockScreenBackgroundUrl);
            LockScreenBackgroundStatusTextBlock.Foreground = Brushes.DarkGreen;
        }
        catch
        {
            ReadyAutoShutdownStatusTextBlock.Text = "Khong ket noi duoc backend de doc cai dat tu tat.";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
            LockScreenBackgroundStatusTextBlock.Text = "Khong ket noi duoc backend de doc cai dat nen lock screen.";
            LockScreenBackgroundStatusTextBlock.Foreground = Brushes.Firebrick;
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
            ReadyAutoShutdownStatusTextBlock.Text = "So phut tu tat khong hop le (1 - 240).";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        if (!TryGetLockScreenBackgroundSettings(out var mode, out var url, out var error))
        {
            LockScreenBackgroundStatusTextBlock.Text = error;
            LockScreenBackgroundStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        try
        {
            var effectiveUrl = url;
            if (mode != "none" && TryResolveLocalLockScreenFile(url, out var localFilePath))
            {
                LockScreenBackgroundStatusTextBlock.Text = "Dang upload media lock screen len server...";
                LockScreenBackgroundStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                effectiveUrl = await UploadLockScreenMediaAsync(mode, localFilePath);
            }

            using var response = await _httpClient.PatchAsJsonAsync(
                BuildApiUrl("/pricing/client-settings"),
                new
                {
                    readyAutoShutdownMinutes = minutes,
                    lockScreenBackgroundMode = mode,
                    lockScreenBackgroundUrl = effectiveUrl,
                });

            if (!response.IsSuccessStatusCode)
            {
                ReadyAutoShutdownStatusTextBlock.Text =
                    $"Luu cai dat tu tat that bai ({(int)response.StatusCode}).";
                ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
                LockScreenBackgroundStatusTextBlock.Text =
                    $"Luu cai dat nen lock screen that bai ({(int)response.StatusCode}).";
                LockScreenBackgroundStatusTextBlock.Foreground = Brushes.Firebrick;
                AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Luu cai dat runtime client that bai");
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<ClientRuntimeSettingsResponse>(JsonOptions());
            _readyAutoShutdownMinutes = Math.Clamp(payload?.ReadyAutoShutdownMinutes ?? minutes, 1, 240);
            _lockScreenBackgroundMode = NormalizeLockScreenBackgroundMode(payload?.LockScreenBackgroundMode ?? mode);
            _lockScreenBackgroundUrl = (payload?.LockScreenBackgroundUrl ?? effectiveUrl).Trim();

            ReadyAutoShutdownMinutesTextBox.Text = _readyAutoShutdownMinutes.ToString(CultureInfo.InvariantCulture);
            SetLockScreenBackgroundModeUi(_lockScreenBackgroundMode);
            LockScreenBackgroundUrlTextBox.Text = _lockScreenBackgroundUrl;

            ReadyAutoShutdownStatusTextBlock.Text =
                $"Da luu: may San sang khong login qua {_readyAutoShutdownMinutes} phut se tu tat.";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.DarkGreen;
            LockScreenBackgroundStatusTextBlock.Text =
                $"Da luu: {DescribeLockScreenBackground(_lockScreenBackgroundMode, _lockScreenBackgroundUrl)}";
            LockScreenBackgroundStatusTextBlock.Foreground = Brushes.DarkGreen;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Da luu runtime client: auto-shutdown={_readyAutoShutdownMinutes}m, lockscreen={_lockScreenBackgroundMode}");
        }
        catch
        {
            ReadyAutoShutdownStatusTextBlock.Text = "Khong ket noi duoc backend khi luu cai dat tu tat.";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
            LockScreenBackgroundStatusTextBlock.Text = "Khong ket noi duoc backend khi luu nen lock screen.";
            LockScreenBackgroundStatusTextBlock.Foreground = Brushes.Firebrick;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Loi ket noi khi luu runtime client");
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
                $"Da thay doi thanh {minutes} phut. Bam \"Luu cai dat\" de ap dung.";
            ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
            return;
        }

        ReadyAutoShutdownStatusTextBlock.Text = "So phut tu tat khong hop le (1 - 240).";
        ReadyAutoShutdownStatusTextBlock.Foreground = Brushes.Firebrick;
    }

    private void LockScreenBackgroundModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_readyShutdownSettingsInitialized || _isLoadingReadyShutdownSettings)
        {
            return;
        }

        LockScreenBackgroundStatusTextBlock.Text = "Da thay doi loai nen lock screen. Bam \"Luu cai dat\" de ap dung.";
        LockScreenBackgroundStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
    }

    private void LockScreenBackgroundUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_readyShutdownSettingsInitialized || _isLoadingReadyShutdownSettings)
        {
            return;
        }

        LockScreenBackgroundStatusTextBlock.Text = "Da thay doi duong dan nen lock screen. Bam \"Luu cai dat\" de ap dung.";
        LockScreenBackgroundStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
    }

    private void BrowseLockScreenBackgroundFileButton_Click(object sender, RoutedEventArgs e)
    {
        var mode = GetLockScreenBackgroundModeFromUi();
        if (mode == "none")
        {
            LockScreenBackgroundStatusTextBlock.Text = "Hay chon loai nen image/video truoc khi chon file.";
            LockScreenBackgroundStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            Filter = mode == "video"
                ? "Video files|*.mp4;*.webm;*.avi;*.mkv;*.mov|All files|*.*"
                : "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*",
            Title = mode == "video" ? "Chon video nen lock screen" : "Chon anh nen lock screen",
        };

        var current = (LockScreenBackgroundUrlTextBox.Text ?? string.Empty).Trim();
        try
        {
            if (!string.IsNullOrWhiteSpace(current))
            {
                var expanded = Environment.ExpandEnvironmentVariables(current);
                if (File.Exists(expanded))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(expanded);
                    dialog.FileName = Path.GetFileName(expanded);
                }
            }
        }
        catch
        {
            // Ignore invalid current path.
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        LockScreenBackgroundUrlTextBox.Text = dialog.FileName;
    }

    private bool TryGetLockScreenBackgroundSettings(
        out string mode,
        out string url,
        out string error)
    {
        mode = GetLockScreenBackgroundModeFromUi();
        url = (LockScreenBackgroundUrlTextBox.Text ?? string.Empty).Trim();
        error = string.Empty;

        if (mode == "none")
        {
            url = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "Vui long nhap duong dan file khi chon nen image/video.";
            return false;
        }

        if (url.Length > 2048)
        {
            error = "Duong dan nen lock screen qua dai (toi da 2048 ky tu).";
            return false;
        }

        if (TryResolveLocalLockScreenFile(url, out _) || IsHttpOrHttpsUrl(url))
        {
            return true;
        }

        error = "Chi ho tro file local/UNC ton tai, hoac URL http/https hop le.";
        return false;
    }

    private static bool IsHttpOrHttpsUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveLocalLockScreenFile(string raw, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || IsHttpOrHttpsUrl(raw))
        {
            return false;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(raw);
            fullPath = Path.GetFullPath(expanded);
            return File.Exists(fullPath);
        }
        catch
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private async Task<string> UploadLockScreenMediaAsync(string mode, string localFilePath)
    {
        using var content = new MultipartFormDataContent();
        using var stream = File.OpenRead(localFilePath);
        using var fileContent = new StreamContent(stream);

        var extension = Path.GetExtension(localFilePath).ToLowerInvariant();
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            mode == "video" ? GuessVideoContentType(extension) : GuessImageContentType(extension));
        content.Add(fileContent, "file", Path.GetFileName(localFilePath));
        content.Add(new StringContent(mode), "mode");

        using var uploadResponse = await _httpClient.PostAsync(
            BuildApiUrl("/pricing/client-settings/lock-screen-media"),
            content);

        if (!uploadResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Upload media lock screen that bai ({(int)uploadResponse.StatusCode})");
        }

        var payload = await uploadResponse.Content.ReadFromJsonAsync<ClientRuntimeSettingsResponse>(JsonOptions());
        var mediaUrl = (payload?.LockScreenBackgroundUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            throw new InvalidOperationException("Backend khong tra ve URL media lock screen.");
        }

        return mediaUrl;
    }

    private static string GuessImageContentType(string extension)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }

    private static string GuessVideoContentType(string extension)
    {
        return extension switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            _ => "application/octet-stream",
        };
    }

    private void SetLockScreenBackgroundModeUi(string mode)
    {
        var normalized = NormalizeLockScreenBackgroundMode(mode);
        foreach (var item in LockScreenBackgroundModeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                LockScreenBackgroundModeComboBox.SelectedItem = item;
                return;
            }
        }

        if (LockScreenBackgroundModeComboBox.Items.Count > 0)
        {
            LockScreenBackgroundModeComboBox.SelectedIndex = 0;
        }
    }

    private string GetLockScreenBackgroundModeFromUi()
    {
        if (LockScreenBackgroundModeComboBox.SelectedItem is ComboBoxItem item)
        {
            return NormalizeLockScreenBackgroundMode(item.Tag?.ToString());
        }

        return "none";
    }

    private static string NormalizeLockScreenBackgroundMode(string? mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "image" or "video" ? normalized : "none";
    }

    private static string DescribeLockScreenBackground(string mode, string url)
    {
        var normalized = NormalizeLockScreenBackgroundMode(mode);
        if (normalized == "none")
        {
            return "Khong dung nen media cho lock screen.";
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return normalized == "image"
                ? "Nen image dang bat (chua co duong dan file)."
                : "Nen video dang bat (chua co duong dan file).";
        }

        return normalized == "image"
            ? $"Nen image file: {url}"
            : $"Nen video file: {url}";
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


    private async Task LoadGuestLoginSettingsAsync()
    {
        try
        {
            _isLoadingGuestLoginSettings = true;
            var settings = await _httpClient.GetFromJsonAsync<Dictionary<string, string>>(
                BuildApiUrl("/settings"),
                JsonOptions());

            if (settings != null && settings.TryGetValue("GUEST_LOGIN_ENABLED", out var val))
            {
                GuestLoginEnabledCheckBox.IsChecked = val == "true";
            }
            else
            {
                GuestLoginEnabledCheckBox.IsChecked = true; // Default to enabled
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load guest login settings: {ex.Message}");
        }
        finally
        {
            _isLoadingGuestLoginSettings = false;
            _guestLoginSettingsInitialized = true;
        }
    }

    private async Task SaveGuestLoginSettingsAsync()
    {
        try
        {
            var isEnabled = GuestLoginEnabledCheckBox.IsChecked == true;
            await _httpClient.PostAsJsonAsync(BuildApiUrl("/settings"), new
            {
                key = "GUEST_LOGIN_ENABLED",
                value = isEnabled ? "true" : "false"
            });
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] \u0110\u00e3 l\u01b0u c\u00e0i \u0111\u1eb7t Kh\u00e1ch v\u00e3ng lai: {(isEnabled ? "B\u1eacT" : "T\u1eaeT")}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"L\u1ed7i khi l\u01b0u c\u00e0i \u0111\u1eb7t kh\u00e1ch: {ex.Message}");
        }
    }

    private void GuestLoginEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_guestLoginSettingsInitialized || _isLoadingGuestLoginSettings) return;
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Tr\u1ea1ng th\u00e1i Kh\u00e1ch v\u00e3ng lai thay \u0111\u1edbi. B\u1ea5m \"L\u01b0u c\u00e0i \u0111\u1eb7t\" \u0111\u1ec3 \u00e1p d\u1ee5ng.");
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
        await SaveGuestLoginSettingsAsync();
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

    private async void OpenAdminCredentialPopupButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowChangeAdminCredentialPopupAsync();
    }

    private async Task ShowChangeAdminCredentialPopupAsync()
    {
        var currentUsername = "admin";
        try
        {
            var settings = await _httpClient.GetFromJsonAsync<Dictionary<string, string>>(
                BuildApiUrl("/settings"),
                JsonOptions());
            if (settings is not null &&
                settings.TryGetValue("AGENT_ADMIN_USERNAME", out var username) &&
                !string.IsNullOrWhiteSpace(username))
            {
                currentUsername = username.Trim();
            }
        }
        catch
        {
            // Keep default value if loading current username fails.
        }

        var dialog = new Window
        {
            Title = "Doi tai khoan Admin may tram",
            Width = 470,
            Height = 280,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
            Owner = this,
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "Cap nhat tai khoan dang nhap Admin may tram",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(titleText, 0);
        root.Children.Add(titleText);

        var usernameLabel = new TextBlock
        {
            Text = "Username:",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(usernameLabel, 1);
        root.Children.Add(usernameLabel);

        var usernameBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = currentUsername,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(usernameBox, 2);
        root.Children.Add(usernameBox);

        var passwordLabel = new TextBlock
        {
            Text = "Password moi:",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(passwordLabel, 3);
        root.Children.Add(passwordLabel);

        var passwordBox = new PasswordBox
        {
            Height = 32,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(passwordBox, 4);
        root.Children.Add(passwordBox);

        var hintText = new TextBlock
        {
            Text = "Mat khau toi thieu 4 ky tu.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(hintText, 5);
        root.Children.Add(hintText);

        var errorText = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(errorText, 6);
        root.Children.Add(errorText);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var saveButton = new Button
        {
            Content = "Luu",
            Width = 92,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        var cancelButton = new Button
        {
            Content = "Huy",
            Width = 92,
            IsCancel = true,
        };
        actions.Children.Add(saveButton);
        actions.Children.Add(cancelButton);
        Grid.SetRow(actions, 7);
        root.Children.Add(actions);

        saveButton.Click += async (_, _) =>
        {
            var newUsername = usernameBox.Text.Trim();
            var newPassword = passwordBox.Password;

            if (string.IsNullOrWhiteSpace(newUsername))
            {
                errorText.Text = "Vui long nhap username.";
                return;
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                errorText.Text = "Vui long nhap password.";
                return;
            }

            if (newPassword.Length < 4)
            {
                errorText.Text = "Mat khau moi phai co it nhat 4 ky tu.";
                return;
            }

            try
            {
                saveButton.IsEnabled = false;
                cancelButton.IsEnabled = false;
                errorText.Text = string.Empty;

                using var resp = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl("/settings/agent-admin/update-credentials"),
                    new
                    {
                        username = newUsername,
                        password = newPassword,
                    });

                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    errorText.Text = string.IsNullOrWhiteSpace(body)
                        ? $"Cap nhat that bai ({(int)resp.StatusCode})."
                        : body;
                    saveButton.IsEnabled = true;
                    cancelButton.IsEnabled = true;
                    return;
                }

                AdminCredentialStatusTextBlock.Text = $"Da cap nhat tai khoan admin: {newUsername}";
                AdminCredentialStatusTextBlock.Foreground = Brushes.DarkGreen;
                AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Da doi tai khoan admin may tram -> {newUsername}");
                dialog.DialogResult = true;
                dialog.Close();
            }
            catch (Exception ex)
            {
                errorText.Text = $"Khong ket noi duoc server: {ex.Message}";
                saveButton.IsEnabled = true;
                cancelButton.IsEnabled = true;
            }
        };

        dialog.Content = root;
        dialog.ShowDialog();
    }
}



