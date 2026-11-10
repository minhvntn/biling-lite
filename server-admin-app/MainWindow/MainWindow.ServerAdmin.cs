using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace Server.Admin.App;

public partial class MainWindow
{
    private readonly ObservableCollection<ServerUserRow> _serverUserRows = new();
    private readonly ObservableCollection<DatabaseStorageTableRow> _dbStorageTableRows = new();
    private readonly ObservableCollection<DatabaseStorageHistoryRow> _dbStorageHistoryRows = new();

    private async Task LoadServerUsersAsync()
    {
        try
        {
            StaffActionStatusTextBlock.Text = "Đang tải danh sách...";
            StaffActionStatusTextBlock.Foreground = Brushes.DimGray;

            var response = await _httpClient.GetAsync(BuildApiUrl("/auth/admin/users"));
            if (response.IsSuccessStatusCode)
            {
                var users = await response.Content.ReadFromJsonAsync<List<ServerUserRow>>();
                _serverUserRows.Clear();
                if (users != null)
                {
                    foreach (var u in users)
                    {
                        _serverUserRows.Add(u);
                    }
                }
                ServerUsersDataGrid.ItemsSource = _serverUserRows;
                StaffActionStatusTextBlock.Text = $"Đã tải {_serverUserRows.Count} tài khoản.";
                if (ServerTotalUsersTextBlock != null) ServerTotalUsersTextBlock.Text = $"{_serverUserRows.Count} Tài khoản";
                var adminCount = _serverUserRows.Count(x => x.IsAdmin);
                var staffCount = _serverUserRows.Count(x => !x.IsAdmin);
                if (ServerAdminCountTextBlock != null) ServerAdminCountTextBlock.Text = $"{adminCount} Admin";
                if (ServerStaffCountTextBlock != null) ServerStaffCountTextBlock.Text = $"{staffCount} Nhân viên";
                StaffActionStatusTextBlock.Foreground = Brushes.DarkGreen;
            }
            else
            {
                StaffActionStatusTextBlock.Text = "Không thể tải danh sách tài khoản.";
                StaffActionStatusTextBlock.Foreground = Brushes.Firebrick;
            }
        }
        catch (Exception ex)
        {
            StaffActionStatusTextBlock.Text = $"Lỗi: {ex.Message}";
            StaffActionStatusTextBlock.Foreground = Brushes.Firebrick;
        }
    }

    private async Task LoadDatabaseStorageStatsAsync()
    {
        try
        {
            DbStorageSizeTextBlock.Text = "...";
            if (DbStorageDatabaseNameTextBlock != null) DbStorageDatabaseNameTextBlock.Text = "Database: đang tải...";
            DbStorageUpdatedAtTextBlock.Text = "Đang tải dữ liệu...";
            DbStorageDeltaTextBlock.Text = "-";
            if (DbStorageTopTableTextBlock != null) DbStorageTopTableTextBlock.Text = "...";

            var response = await _httpClient.GetFromJsonAsync<DatabaseStorageStatsResponse>(
                BuildApiUrl("/reports/database/storage?days=14"),
                JsonOptions());
            if (response is null)
            {
                return;
            }

            // 1. Hero KPI Cards
            DbStorageSizeTextBlock.Text = response.SizePretty;
            if (DbStorageDatabaseNameTextBlock != null)
            {
                DbStorageDatabaseNameTextBlock.Text = $"Database: {response.DatabaseName}";
            }
            DbStorageUpdatedAtTextBlock.Text = FormatDateTime(response.ServerTime);

            var deltaPrefix = response.DayDeltaBytes > 0 ? "+" : string.Empty;
            DbStorageDeltaTextBlock.Text =
                $"{deltaPrefix}{FormatBytes(response.DayDeltaBytes)} ({deltaPrefix}{response.DayDeltaPercent:0.##}%)";

            if (DbStorageDeltaPillBorder != null)
            {
                if (response.DayDeltaBytes > 0)
                {
                    DbStorageDeltaPillBorder.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                    DbStorageDeltaPillBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                    DbStorageDeltaTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                }
                else if (response.DayDeltaBytes < 0)
                {
                    DbStorageDeltaPillBorder.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                    DbStorageDeltaPillBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
                    DbStorageDeltaTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                }
                else
                {
                    DbStorageDeltaPillBorder.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                    DbStorageDeltaPillBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                    DbStorageDeltaTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                }
            }

            // Top table & breakdown calculation
            var topTable = response.TopTables.FirstOrDefault();
            if (DbStorageTopTableTextBlock != null)
            {
                DbStorageTopTableTextBlock.Text = topTable != null
                    ? $"{topTable.TableName} ({FormatBytes(topTable.TotalBytes)})"
                    : "-";
            }

            long totalTableData = response.TopTables.Sum(x => x.TableBytes);
            long totalIndexData = response.TopTables.Sum(x => x.IndexToastBytes);
            long combinedTopData = totalTableData + totalIndexData;
            double dataPercent = combinedTopData > 0 ? (double)totalTableData / combinedTopData * 100 : 0;
            double indexPercent = combinedTopData > 0 ? (double)totalIndexData / combinedTopData * 100 : 0;

            if (DbStorageDataProgressBar != null)
            {
                DbStorageDataProgressBar.Value = dataPercent;
            }
            if (DbStorageBreakdownTextBlock != null)
            {
                DbStorageBreakdownTextBlock.Text =
                    $"Data: {FormatBytes(totalTableData)} ({dataPercent:0.#}%)  •  Index/Toast: {FormatBytes(totalIndexData)} ({indexPercent:0.#}%)";
            }

            // 2. Top Tables DataGrid Rows
            _dbStorageTableRows.Clear();
            foreach (var row in response.TopTables)
            {
                row.DisplayTotal = FormatBytes(row.TotalBytes);
                row.DisplayData = FormatBytes(row.TableBytes);
                row.DisplayIndex = FormatBytes(row.IndexToastBytes);
                row.PercentOfTotal = response.SizeBytes > 0 ? (double)row.TotalBytes / response.SizeBytes * 100 : 0;
                _dbStorageTableRows.Add(row);
            }
            DbStorageTablesDataGrid.ItemsSource = _dbStorageTableRows;

            // 3. History Rows & Delta
            _dbStorageHistoryRows.Clear();
            var chronological = response.History.OrderBy(x => x.Date).ToList();
            var maxHistoryBytes = chronological.Count > 0 ? chronological.Max(x => x.SizeBytes) : 1;
            var minHistoryBytes = chronological.Count > 0 ? chronological.Min(x => x.SizeBytes) : 0;

            var historyItems = new List<DatabaseStorageHistoryRow>();
            for (int i = 0; i < chronological.Count; i++)
            {
                var cur = chronological[i];
                var prev = i > 0 ? chronological[i - 1] : null;
                long delta = prev != null ? cur.SizeBytes - prev.SizeBytes : 0;

                string deltaText = "-";
                string deltaColor = "#64748B";
                if (prev != null)
                {
                    if (delta > 0)
                    {
                        deltaText = $"+{FormatBytes(delta)}";
                        deltaColor = "#DC2626";
                    }
                    else if (delta < 0)
                    {
                        deltaText = FormatBytes(delta);
                        deltaColor = "#059669";
                    }
                    else
                    {
                        deltaText = "0 B";
                        deltaColor = "#64748B";
                    }
                }

                double heightPercent = maxHistoryBytes > 0
                    ? Math.Max(25, (double)cur.SizeBytes / maxHistoryBytes * 100)
                    : 50;

                historyItems.Add(new DatabaseStorageHistoryRow
                {
                    Date = cur.Date,
                    SizeBytes = cur.SizeBytes,
                    SizePretty = string.IsNullOrWhiteSpace(cur.SizePretty) ? FormatBytes(cur.SizeBytes) : cur.SizePretty,
                    DisplayDate = FormatDateOnly(cur.Date),
                    DeltaText = deltaText,
                    DeltaColor = deltaColor,
                    BarHeightPercent = heightPercent
                });
            }

            foreach (var row in historyItems.OrderByDescending(x => x.Date))
            {
                _dbStorageHistoryRows.Add(row);
            }
            DbStorageHistoryDataGrid.ItemsSource = _dbStorageHistoryRows;
        }
        catch (Exception ex)
        {
            DbStorageSizeTextBlock.Text = "Lỗi kết nối";
            DbStorageUpdatedAtTextBlock.Text = "Không tải được";
            DbStorageDeltaTextBlock.Text = $"Lỗi: {ex.Message}";
            DbStorageDeltaTextBlock.Foreground = Brushes.Firebrick;
        }
    }

    private static string FormatDateOnly(string dateStr)
    {
        if (DateTime.TryParse(dateStr, out var d))
        {
            return d.ToString("dd/MM/yyyy");
        }
        return dateStr;
    }

    private async void CreateStaffButton_Click(object sender, RoutedEventArgs e)
    {
        var username = NewStaffUsernameTextBox.Text.Trim();
        var password = NewStaffPasswordBox.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            StaffActionStatusTextBlock.Text = "Vui lòng nhập đầy đủ thông tin (Tên đăng nhập, Mật khẩu).";
            StaffActionStatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync(BuildApiUrl("/auth/admin/users"), new
            {
                username,
                password
            }, JsonOptions());

            if (response.IsSuccessStatusCode)
            {
                StaffActionStatusTextBlock.Text = "Tạo tài khoản STAFF thành công.";
                StaffActionStatusTextBlock.Foreground = Brushes.DarkGreen;
                NewStaffUsernameTextBox.Text = "";
                NewStaffPasswordBox.Password = "";
                await LoadServerUsersAsync();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                StaffActionStatusTextBlock.Text = $"Lỗi tạo tài khoản: {error}";
                StaffActionStatusTextBlock.Foreground = Brushes.Firebrick;
            }
        }
        catch (Exception ex)
        {
            StaffActionStatusTextBlock.Text = $"Lỗi: {ex.Message}";
            StaffActionStatusTextBlock.Foreground = Brushes.Firebrick;
        }
    }

    private async void DeleteStaffButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            if (MessageBox.Show("Bạn có chắc chắn muốn xóa tài khoản này?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    var response = await _httpClient.DeleteAsync(BuildApiUrl($"/auth/admin/users/{id}"));
                    if (response.IsSuccessStatusCode)
                    {
                        StaffActionStatusTextBlock.Text = "Đã xóa tài khoản.";
                        StaffActionStatusTextBlock.Foreground = Brushes.DarkGreen;
                        await LoadServerUsersAsync();
                    }
                    else
                    {
                        StaffActionStatusTextBlock.Text = "Xóa tài khoản thất bại (Không thể xóa ADMIN).";
                        StaffActionStatusTextBlock.Foreground = Brushes.Firebrick;
                    }
                }
                catch (Exception ex)
                {
                    StaffActionStatusTextBlock.Text = $"Lỗi: {ex.Message}";
                    StaffActionStatusTextBlock.Foreground = Brushes.Firebrick;
                }
            }
        }
    }

    private void ResetStaffPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ServerUserRow user)
        {
            var win = new ResetStaffPasswordWindow(user, _httpClient, BuildApiUrl, JsonOptions)
            {
                Owner = this
            };
            if (win.ShowModal(this) == true)
            {
                StaffActionStatusTextBlock.Text = $"Đã đổi mật khẩu cho tài khoản '{user.Username}'.";
                StaffActionStatusTextBlock.Foreground = Brushes.DarkGreen;
                _ = LoadServerUsersAsync();
            }
        }
    }

    private void ChangeAdminCredentialsButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new ChangeAdminCredentialsWindow(_httpClient, BuildApiUrl, JsonOptions)
        {
            Owner = this
        };
        if (win.ShowModal(this) == true)
        {
            _ = LoadServerUsersAsync();
        }
    }

    private async void RefreshServerUsersButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadServerUsersAsync();
    }

    private async void RefreshDatabaseStorageButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadDatabaseStorageStatsAsync();
    }

    private async void ClearSafeDatabaseLogsButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "Bạn có chắc chắn muốn dọn dẹp các nhóm log rác an toàn?\n\n" +
            "Hệ thống sẽ dọn sạch:\n" +
            "• Ảnh chụp màn hình máy trạm (pc.screenshot.captured)\n" +
            "• Lịch sử các lệnh điều khiển cũ đã hoàn tất\n" +
            "• Nhật ký trạng thái kết nối socket cũ\n" +
            "• Nhật ký mở game/ứng dụng cũ hơn 14 ngày\n\n" +
            "CAM KẾT AN TOÀN 100%:\n" +
            "✓ KHÔNG xóa tài khoản hay mật khẩu hội viên\n" +
            "✓ KHÔNG xóa số dư tài khoản hay lịch sử nạp/trừ tiền\n" +
            "✓ KHÔNG làm gián đoạn máy trạm đang chơi\n\n" +
            "Tiến hành dọn dẹp ngay bây giờ?",
            "Dọn dẹp log an toàn",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            using var response = await _httpClient.DeleteAsync(BuildApiUrl("/reports/events/system"));
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show(
                    this,
                    $"Dọn dẹp log thất bại ({(int)response.StatusCode}).",
                    "Server Admin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<ClearSystemEventsResponse>(JsonOptions());
            var deletedCount = payload?.DeletedCount ?? 0;

            MessageBox.Show(
                this,
                $"Đã dọn dẹp thành công {deletedCount:N0} bản ghi log rác.\n" +
                "Database đã được giải phóng dung lượng và tối ưu hóa an toàn!",
                "Dọn dẹp log hoàn tất",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await LoadDatabaseStorageStatsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Lỗi khi dọn dẹp log: {ex.Message}",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string FormatBytes(long bytes)
    {
        var abs = Math.Abs((double)bytes);
        string unit;
        double value;
        if (abs >= 1024 * 1024 * 1024)
        {
            unit = "GB";
            value = bytes / (1024d * 1024d * 1024d);
        }
        else if (abs >= 1024 * 1024)
        {
            unit = "MB";
            value = bytes / (1024d * 1024d);
        }
        else if (abs >= 1024)
        {
            unit = "KB";
            value = bytes / 1024d;
        }
        else
        {
            unit = "B";
            value = bytes;
        }

        return $"{value:0.##} {unit}";
    }

    private void AdminTabSubDataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is DependencyObject dep)
        {
            var scrollViewer = FindVisualParent<ScrollViewer>(dep);
            if (scrollViewer != null && e.Delta != 0)
            {
                double scrollDelta = (e.Delta / 120.0) * 85.0;
                double newOffset = scrollViewer.VerticalOffset - scrollDelta;
                if (newOffset < 0) newOffset = 0;
                if (newOffset > scrollViewer.ScrollableHeight) newOffset = scrollViewer.ScrollableHeight;
                scrollViewer.ScrollToVerticalOffset(newOffset);
                e.Handled = true;
            }
        }
    }
}
