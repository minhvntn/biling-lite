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
    private static readonly string[] DefaultBlockedDomains =
    {
        "xvideos.com",
        "xnxx.com",
        "pornhub.com",
        "xhamster.com",
        "redtube.com",
        "youporn.com",
        "tube8.com",
        "beeg.com",
        "spankbang.com",
        "sex.com",
        "brazzers.com",
        "bangbros.com",
        "youjizz.com",
        "hclips.com",
        "tnaflix.com",
        "sunporno.com",
        "porn.com",
        "pornpics.com",
        "rule34.xxx",
        "hentaihaven.xxx",
        "nhentai.net",
        "f95zone.to",
        "motherless.com",
        "cam4.com",
        "chaturbate.com",
        "bongacams.com",
        "stripchat.com",
        "livejasmin.com",
        "xnxx.tv",
        "xvideos2.com",
    };

    private async Task RefreshWebFilterSettingsAsync()
    {
        try
        {
            _isLoadingWebFilterSettings = true;
            var response = await _httpClient.GetFromJsonAsync<WebFilterSettingsResponse>(
                BuildApiUrl("/web-filter/settings"),
                JsonOptions());

            if (response is null)
            {
                WebFilterStatusTextBlock.Text = "Không tải được cấu hình khống chế website.";
                WebFilterStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            WebFilterEnabledCheckBox.IsChecked = response.Enabled;
            var domains = response.BlockedDomains
                .Select(NormalizeDomainInput)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _webFilterDomainRows.Clear();
            foreach (var domain in domains)
            {
                _webFilterDomainRows.Add(domain!);
            }

            WebFilterStatusTextBlock.Text =
                $"Đã tải {domains.Count} domain chặn. Cập nhật: {FormatDateTime(response.UpdatedAt)}.";
            WebFilterStatusTextBlock.Foreground = Brushes.DarkGreen;
        }
        catch
        {
            WebFilterStatusTextBlock.Text = "Không kết nối được backend để đọc cấu hình khống chế website.";
            WebFilterStatusTextBlock.Foreground = Brushes.Firebrick;
        }
        finally
        {
            _isLoadingWebFilterSettings = false;
        }
    }

    private async Task SaveWebFilterSettingsAsync()
    {
        var domains = GetNormalizedDomainsFromUi();
        var enabled = WebFilterEnabledCheckBox.IsChecked == true;

        try
        {
            using var response = await _httpClient.PutAsJsonAsync(
                BuildApiUrl("/web-filter/settings"),
                new
                {
                    enabled,
                    blockedDomains = domains,
                    updatedBy = "admin.desktop",
                });

            if (!response.IsSuccessStatusCode)
            {
                WebFilterStatusTextBlock.Text =
                    $"Lưu cấu hình khống chế website thất bại ({(int)response.StatusCode}).";
                WebFilterStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<WebFilterSettingsResponse>(JsonOptions());
            var savedDomains = payload?.BlockedDomains ?? domains;
            _webFilterDomainRows.Clear();
            foreach (var domain in savedDomains
                         .Select(NormalizeDomainInput)
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                _webFilterDomainRows.Add(domain!);
            }

            var updatedAtText = payload is null ? DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss") : FormatDateTime(payload.UpdatedAt);
            WebFilterStatusTextBlock.Text =
                $"Đã lưu cấu hình khống chế website. Tổng domain chặn: {_webFilterDomainRows.Count}. Cập nhật: {updatedAtText}.";
            WebFilterStatusTextBlock.Foreground = Brushes.DarkGreen;
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Đã lưu cấu hình khống chế website ({_webFilterDomainRows.Count} domain)");
        }
        catch (Exception ex)
        {
            WebFilterStatusTextBlock.Text = $"Lỗi lưu cấu hình khống chế website: {ex.Message}";
            WebFilterStatusTextBlock.Foreground = Brushes.Firebrick;
        }
    }

    private List<string> GetNormalizedDomainsFromUi()
    {
        return _webFilterDomainRows
            .Select(NormalizeDomainInput)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => x!)
            .ToList();
    }

    private static string? NormalizeDomainInput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim().ToLowerInvariant();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            value = value[7..];
        }
        else if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = value[8..];
        }

        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }

        if (value.StartsWith("*.", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        var splitIndex = value.IndexOfAny(new[] { '/', '?', '#', ':', ' ' });
        if (splitIndex >= 0)
        {
            value = value[..splitIndex];
        }

        value = value.Trim('.').Trim();
        if (value.Length is < 3 or > 90)
        {
            return null;
        }

        if (!value.Contains('.'))
        {
            return null;
        }

        foreach (var character in value)
        {
            if ((character is >= 'a' and <= 'z') ||
                (character is >= '0' and <= '9') ||
                character is '.' or '-')
            {
                continue;
            }

            return null;
        }

        if (value.StartsWith('-') || value.EndsWith('-'))
        {
            return null;
        }

        return value;
    }

    private void MarkWebFilterPendingChange()
    {
        if (!_webFilterSettingsInitialized || _isLoadingWebFilterSettings)
        {
            return;
        }

        WebFilterStatusTextBlock.Text = "Đã thay đổi cấu hình. Bấm \"Lưu áp dụng\" để cập nhật xuống máy trạm.";
        WebFilterStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
    }

    private void WebFilterEnabledCheckBox_Checked(object sender, RoutedEventArgs e) => MarkWebFilterPendingChange();

    private void WebFilterEnabledCheckBox_Unchecked(object sender, RoutedEventArgs e) => MarkWebFilterPendingChange();

    private void AddWebFilterDomainButton_Click(object sender, RoutedEventArgs e)
    {
        var normalized = NormalizeDomainInput(WebFilterDomainTextBox.Text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            WebFilterStatusTextBlock.Text = "Domain không hợp lệ. Ví dụ đúng: badsite.com";
            WebFilterStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        if (_webFilterDomainRows.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            WebFilterStatusTextBlock.Text = $"Domain \"{normalized}\" đã có trong danh sách.";
            WebFilterStatusTextBlock.Foreground = Brushes.DimGray;
            return;
        }

        _webFilterDomainRows.Add(normalized);
        var sorted = _webFilterDomainRows
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _webFilterDomainRows.Clear();
        foreach (var domain in sorted)
        {
            _webFilterDomainRows.Add(domain);
        }

        WebFilterDomainTextBox.Text = string.Empty;
        MarkWebFilterPendingChange();
    }

    private void RemoveSelectedWebFilterDomainButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebFilterDomainsListBox.SelectedItem is not string selected)
        {
            WebFilterStatusTextBlock.Text = "Vui lòng chọn domain cần xóa.";
            WebFilterStatusTextBlock.Foreground = Brushes.DimGray;
            return;
        }

        _webFilterDomainRows.Remove(selected);
        MarkWebFilterPendingChange();
    }

    private void ClearAllWebFilterDomainsButton_Click(object sender, RoutedEventArgs e)
    {
        _webFilterDomainRows.Clear();
        MarkWebFilterPendingChange();
    }

    private void ResetDefaultWebFilterDomainsButton_Click(object sender, RoutedEventArgs e)
    {
        _webFilterDomainRows.Clear();
        foreach (var domain in DefaultBlockedDomains
                     .Select(NormalizeDomainInput)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            _webFilterDomainRows.Add(domain!);
        }

        MarkWebFilterPendingChange();
    }

    private async void SaveWebFilterSettingsButton_Click(object sender, RoutedEventArgs e) => await SaveWebFilterSettingsAsync();

    private async void ReloadWebFilterSettingsButton_Click(object sender, RoutedEventArgs e) => await RefreshWebFilterSettingsAsync();

    private void WebFilterDomainTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        AddWebFilterDomainButton_Click(sender, e);
        e.Handled = true;
    }
}

