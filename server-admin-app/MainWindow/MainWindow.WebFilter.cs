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
        "21sextury.com",
        "3hentai.net",
        "4tube.com",
        "7mmtv.tv",
        "admireme.vip",
        "adulttime.com",
        "alohatube.com",
        "anon-v.com",
        "anyporn.com",
        "asmhentai.com",
        "avgle.com",
        "babes.com",
        "badjojo.com",
        "bangbros.com",
        "beeg.com",
        "blacked.com",
        "bongacams.com",
        "bravotube.net",
        "brazzers.com",
        "bunkr.is",
        "bunkr.site",
        "cam4.com",
        "camcaps.to",
        "cammodeldirectory.com",
        "camslib.com",
        "camsoda.com",
        "camwhores.porn",
        "camwhores.tv",
        "chaturbate.com",
        "checkervn.com",
        "cliphunter.com",
        "clips4sale.com",
        "clipsex.com",
        "congdongmassage.com",
        "coomer.party",
        "coomer.su",
        "cumlouder.com",
        "cyberdrop.me",
        "daftsex.com",
        "dammy88.com",
        "danbooru.donmai.us",
        "deeper.com",
        "definethick.com",
        "digitalplayground.com",
        "dirtyship.com",
        "drtuber.com",
        "e-hentai.org",
        "e621.net",
        "empflix.com",
        "eporner.com",
        "erome.com",
        "evilangel.com",
        "exhentai.org",
        "f95zone.to",
        "fakehub.com",
        "fansly.com",
        "fapdu.com",
        "fapellasuite.com",
        "fapello.com",
        "flirt4free.com",
        "freejavbt.com",
        "freeomovie.to",
        "fuq.com",
        "gaigoi.org",
        "gaigoivn.net",
        "gelbooru.com",
        "girlsway.com",
        "gotporn.com",
        "hamtruyen.vn",
        "hanime.tv",
        "hayxx.com",
        "hclips.com",
        "heavy-r.com",
        "hentai2read.com",
        "hentaiera.com",
        "hentaifox.com",
        "hentaihaven.xxx",
        "hentaimama.io",
        "hentaiplay.net",
        "hentaivn.moe",
        "hentaivn.net",
        "hentaivn.tv",
        "hitomi.la",
        "hotmovs.com",
        "hpjav.tv",
        "hqporner.com",
        "hustler.com",
        "imlive.com",
        "iwantclips.com",
        "jable.tv",
        "javbus.com",
        "javcl.com",
        "javdb.com",
        "javfinder.la",
        "javguru.com",
        "javhd.com",
        "javhihi.com",
        "javlibrary.com",
        "javmenu.com",
        "javmost.com",
        "javseen.com",
        "javsub.co",
        "javtiful.com",
        "jerkmate.com",
        "jizzbunker.com",
        "keezmovies.com",
        "kemono.party",
        "kemono.su",
        "kink.com",
        "lauxanh.org",
        "lauxanh.us",
        "livejasmin.com",
        "loyalfans.com",
        "luscious.net",
        "madthumbs.com",
        "manyvids.com",
        "metart.com",
        "missav.ai",
        "missav.com",
        "missav.ws",
        "mofos.com",
        "motherless.com",
        "multporn.net",
        "myfreecams.com",
        "naughtyamerica.com",
        "netflav.com",
        "nhentai.net",
        "nubiles.net",
        "nude.com",
        "nuvid.com",
        "onlyfans.com",
        "paradisehill.cc",
        "penthouse.com",
        "phimhot69.com",
        "phimlon.com",
        "phimsex.com",
        "phimsex.net",
        "phimsex3.com",
        "phimsex99.com",
        "phimsexchina.com",
        "phimsexhay.org",
        "phimsexhd.org",
        "phimsexkorea.com",
        "phimsexloanluan.org",
        "phimsexmoi.net",
        "phimsexmy.com",
        "phimsexnhatban.org",
        "phimsexsub.com",
        "phimsexvn.org",
        "phimxxx.com",
        "playboy.com",
        "porn.com",
        "porndiq.com",
        "porndoe.com",
        "porndude.com",
        "pornez.cam",
        "porngo.com",
        "pornhat.com",
        "pornhd.com",
        "pornhub.com",
        "pornhubselect.com",
        "pornone.com",
        "pornoxo.com",
        "pornpics.com",
        "porntrex.com",
        "porntube.com",
        "private.com",
        "proporn.com",
        "puretaboo.com",
        "pururin.io",
        "realitykings.com",
        "rec-tube.com",
        "redtube.com",
        "rk.com",
        "rule34.xxx",
        "rule34video.com",
        "sex.com",
        "sexchauau.com",
        "sexnhat.me",
        "sexnhat.pro",
        "sexviet.tv",
        "sexvietsub.com",
        "sexvl.com",
        "sexvl.net",
        "simpcity.su",
        "simply-hentai.com",
        "slayed.com",
        "slutload.com",
        "spankbang.com",
        "streamate.com",
        "streamporn.to",
        "stripchat.com",
        "subnhanh.xxx",
        "sunporno.com",
        "supjav.com",
        "sweet-sinner.com",
        "teamskeet.com",
        "theporndude.com",
        "thiendia.com",
        "thienvadia.com",
        "thotsbay.to",
        "thumbzilla.com",
        "tktube.com",
        "tnaflix.com",
        "tokyomotion.net",
        "topvlxx.com",
        "truyentranh18.net",
        "tsumino.com",
        "tube8.com",
        "tubepornclassic.com",
        "tuberel.com",
        "tubev.sex",
        "tushy.com",
        "twistys.com",
        "txxx.com",
        "underhentai.net",
        "upornia.com",
        "viptube.com",
        "vixen.com",
        "vlxx.com",
        "vlxx.moe",
        "vlxx.sex",
        "vlxx.tv",
        "vnbom.com",
        "vrsmash.com",
        "webcamrips.com",
        "wicked.com",
        "xfreehd.com",
        "xhamster.com",
        "xnxx.com",
        "xnxx.tv",
        "xozilla.com",
        "xvideos.com",
        "xvideos2.com",
        "xvideovn.com",
        "xxvn.com",
        "xxvn.net",
        "yespornplease.com",
        "youjizz.com",
        "youporn.com",
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
        var selectedItems = WebFilterDomainsDataGrid.SelectedItems.Cast<string>().ToList();
        if (selectedItems.Count == 0)
        {
            WebFilterStatusTextBlock.Text = "Vui lòng chọn domain cần xóa.";
            WebFilterStatusTextBlock.Foreground = Brushes.DimGray;
            return;
        }

        foreach (var item in selectedItems)
        {
            _webFilterDomainRows.Remove(item);
        }
        MarkWebFilterPendingChange();
    }

    private void ClearAllWebFilterDomainsButton_Click(object sender, RoutedEventArgs e)
    {
        _webFilterDomainRows.Clear();
        MarkWebFilterPendingChange();
    }

    private void ResetDefaultWebFilterDomainsButton_Click(object sender, RoutedEventArgs e)
    {
        var existing = _webFilterDomainRows.ToList();
        var allDomains = existing.Concat(DefaultBlockedDomains)
                     .Select(NormalizeDomainInput)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                     .ToList();

        _webFilterDomainRows.Clear();
        foreach (var domain in allDomains)
        {
            _webFilterDomainRows.Add(domain!);
        }

        WebFilterStatusTextBlock.Text = $"Đã nạp danh sách mẫu ({_webFilterDomainRows.Count} domain). Bấm \"Lưu cấu hình\" để áp dụng.";
        WebFilterStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
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

    private void EditWebFilterDomainButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string domain)
        {
            WebFilterDomainTextBox.Text = domain;
            _webFilterDomainRows.Remove(domain);
            MarkWebFilterPendingChange();
        }
    }

    private void DeleteWebFilterDomainButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string domain)
        {
            _webFilterDomainRows.Remove(domain);
            MarkWebFilterPendingChange();
        }
    }
}
