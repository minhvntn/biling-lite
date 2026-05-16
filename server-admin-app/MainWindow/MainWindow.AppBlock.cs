using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class MainWindow
{
    private bool _appBlockSettingsInitialized;
    private bool _isLoadingAppBlockSettings;

    private readonly ObservableCollection<string> _appBlockAvailableRows = new();
    private readonly ObservableCollection<string> _appBlockBlockedRows = new();

    // Master list of all known blockable applications (display name => process/key)
    private static readonly List<AppBlockEntry> AllBlockableApps = new()
    {
        new("Task Manager (taskmgr.exe)", "taskmgr"),
        new("Command Prompt (cmd.exe)", "cmd"),
        new("PowerShell (powershell.exe)", "powershell"),
        new("Run Dialog (Win+R)", "win_run"),
        new("Windows Sleep / Hibernate", "win_sleep"),
        new("Registry Editor (regedit.exe)", "regedit"),
        new("Control Panel", "control"),
        new("Windows Settings", "ms_settings"),
        new("Device Manager (devmgmt.msc)", "devmgmt"),
        new("Disk Management (diskmgmt.msc)", "diskmgmt"),
        new("Computer Management (compmgmt.msc)", "compmgmt"),
        new("Group Policy Editor (gpedit.msc)", "gpedit"),
        new("System Configuration (msconfig.exe)", "msconfig"),
        new("Event Viewer (eventvwr.msc)", "eventvwr"),
        new("Windows Explorer (explorer.exe)", "explorer"),
        new("Microsoft Edge", "msedge"),
        new("Notepad", "notepad"),
        new("Remote Desktop (mstsc.exe)", "mstsc"),
        new("Snipping Tool (SnippingTool.exe)", "snippingtool"),
        new("Windows Terminal", "wt"),
        new("Resource Monitor (resmon.exe)", "resmon"),
        new("Performance Monitor (perfmon.exe)", "perfmon"),
        new("Network Connections (ncpa.cpl)", "ncpa"),
        new("Firewall Settings (wf.msc)", "wf"),
        new("Disk Cleanup (cleanmgr.exe)", "cleanmgr"),
        new("System Restore", "rstrui"),
        new("Windows Update", "win_update"),
        new("USB / Removable Drives", "usb_block"),
        new("Screenshot (Print Screen)", "printscreen"),
    };

    private void InitializeAppBlockTab()
    {
        AppBlockAvailableListBox.ItemsSource = _appBlockAvailableRows;
        AppBlockBlockedListBox.ItemsSource = _appBlockBlockedRows;
    }

    private async Task RefreshAppBlockSettingsAsync()
    {
        try
        {
            _isLoadingAppBlockSettings = true;
            var response = await _httpClient.GetFromJsonAsync<JsonElement>(
                BuildApiUrl("/settings"), JsonOptions());

            if (response.ValueKind == JsonValueKind.Undefined)
            {
                AppBlockStatusTextBlock.Text = "Không tải được cấu hình chặn ứng dụng.";
                AppBlockStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            // Read enabled state
            var enabled = false;
            if (response.TryGetProperty("APP_BLOCK_ENABLED", out var enabledProp))
            {
                enabled = enabledProp.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            }
            AppBlockEnabledCheckBox.IsChecked = enabled;

            // Read blocked list
            var blockedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (response.TryGetProperty("APP_BLOCK_LIST", out var listProp))
            {
                var listJson = listProp.GetString();
                if (!string.IsNullOrEmpty(listJson))
                {
                    try
                    {
                        var keys = JsonSerializer.Deserialize<List<string>>(listJson);
                        if (keys != null)
                        {
                            foreach (var k in keys) blockedKeys.Add(k);
                        }
                    }
                    catch { }
                }
            }

            // Populate the two lists
            _appBlockAvailableRows.Clear();
            _appBlockBlockedRows.Clear();

            foreach (var app in AllBlockableApps)
            {
                if (blockedKeys.Contains(app.Key))
                {
                    _appBlockBlockedRows.Add(app.DisplayName);
                }
                else
                {
                    _appBlockAvailableRows.Add(app.DisplayName);
                }
            }

            AppBlockStatusTextBlock.Text =
                $"Đã tải cấu hình chặn ứng dụng ({_appBlockBlockedRows.Count} ứng dụng đang bị chặn).";
            AppBlockStatusTextBlock.Foreground = Brushes.DarkGreen;
        }
        catch (Exception ex)
        {
            AppBlockStatusTextBlock.Text = $"Lỗi kết nối: {ex.Message}";
            AppBlockStatusTextBlock.Foreground = Brushes.Firebrick;
        }
        finally
        {
            _isLoadingAppBlockSettings = false;
        }
    }

    private async Task SaveAppBlockSettingsAsync()
    {
        try
        {
            var enabled = AppBlockEnabledCheckBox.IsChecked == true;

            // Collect blocked keys from the blocked list display names
            var blockedKeys = new List<string>();
            foreach (var displayName in _appBlockBlockedRows)
            {
                var entry = AllBlockableApps.FirstOrDefault(a => a.DisplayName == displayName);
                if (entry != null) blockedKeys.Add(entry.Key);
            }

            var listJson = JsonSerializer.Serialize(blockedKeys);

            // Save enabled
            var r1 = await _httpClient.PostAsJsonAsync(BuildApiUrl("/settings"),
                new { key = "APP_BLOCK_ENABLED", value = enabled.ToString().ToLower() }, JsonOptions());
            if (!r1.IsSuccessStatusCode)
            {
                AppBlockStatusTextBlock.Text = "Lỗi lưu cấu hình chặn ứng dụng (enabled).";
                AppBlockStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            // Save list
            var r2 = await _httpClient.PostAsJsonAsync(BuildApiUrl("/settings"),
                new { key = "APP_BLOCK_LIST", value = listJson }, JsonOptions());
            if (!r2.IsSuccessStatusCode)
            {
                AppBlockStatusTextBlock.Text = "Lỗi lưu cấu hình chặn ứng dụng (list).";
                AppBlockStatusTextBlock.Foreground = Brushes.Firebrick;
                return;
            }

            AppBlockStatusTextBlock.Text =
                $"Đã lưu thành công! {blockedKeys.Count} ứng dụng đang bị chặn.";
            AppBlockStatusTextBlock.Foreground = Brushes.DarkGreen;
        }
        catch (Exception ex)
        {
            AppBlockStatusTextBlock.Text = $"Lỗi lưu cấu hình: {ex.Message}";
            AppBlockStatusTextBlock.Foreground = Brushes.Firebrick;
        }
    }

    private void MarkAppBlockPendingChange()
    {
        if (!_appBlockSettingsInitialized || _isLoadingAppBlockSettings) return;
        AppBlockStatusTextBlock.Text = "Đã thay đổi cấu hình. Bấm \"Lưu áp dụng\" để cập nhật xuống máy trạm.";
        AppBlockStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
    }

    private void AppBlockAddButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = AppBlockAvailableListBox.SelectedItems.Cast<string>().ToList();
        if (selectedItems.Count == 0)
        {
            AppBlockStatusTextBlock.Text = "Vui lòng chọn ứng dụng cần chặn ở danh sách bên trái.";
            AppBlockStatusTextBlock.Foreground = Brushes.DimGray;
            return;
        }

        foreach (var item in selectedItems)
        {
            _appBlockAvailableRows.Remove(item);
            _appBlockBlockedRows.Add(item);
        }
        MarkAppBlockPendingChange();
    }

    private void AppBlockRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = AppBlockBlockedListBox.SelectedItems.Cast<string>().ToList();
        if (selectedItems.Count == 0)
        {
            AppBlockStatusTextBlock.Text = "Vui lòng chọn ứng dụng cần gỡ chặn ở danh sách bên phải.";
            AppBlockStatusTextBlock.Foreground = Brushes.DimGray;
            return;
        }

        foreach (var item in selectedItems)
        {
            _appBlockBlockedRows.Remove(item);
            _appBlockAvailableRows.Add(item);
        }
        MarkAppBlockPendingChange();
    }

    private void AppBlockEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        MarkAppBlockPendingChange();
    }

    private async void AppBlockReloadButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshAppBlockSettingsAsync();

    private async void AppBlockSaveButton_Click(object sender, RoutedEventArgs e) =>
        await SaveAppBlockSettingsAsync();
}

public record AppBlockEntry(string DisplayName, string Key);
