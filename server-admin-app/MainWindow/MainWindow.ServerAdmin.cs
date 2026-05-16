using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class MainWindow
{
    private readonly ObservableCollection<ServerUserRow> _serverUserRows = new();
    private bool _serverUsersInitialized;

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

    private void ChangeAdminCredentialsButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new ChangeAdminCredentialsWindow(_httpClient, BuildApiUrl, JsonOptions)
        {
            Owner = this
        };
        if (win.ShowDialog() == true)
        {
            _ = LoadServerUsersAsync();
        }
    }
}
