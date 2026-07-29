using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class ResetStaffPasswordWindow : Window
{
    private readonly ServerUserRow _user;
    private readonly HttpClient _httpClient;
    private readonly Func<string, string> _buildApiUrl;
    private readonly Func<JsonSerializerOptions> _jsonOptions;

    public ResetStaffPasswordWindow(ServerUserRow user, HttpClient httpClient, Func<string, string> buildApiUrl, Func<JsonSerializerOptions> jsonOptions)
    {
        InitializeComponent();
        _user = user;
        _httpClient = httpClient;
        _buildApiUrl = buildApiUrl;
        _jsonOptions = jsonOptions;

        HeaderTextBlock.Text = $"Đổi mật khẩu: {_user.Username} ({_user.Role})";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var newPassword = NewPasswordBox.Password;
        var confirmPassword = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            StatusTextBlock.Text = "Vui lòng nhập mật khẩu mới.";
            StatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        if (newPassword != confirmPassword)
        {
            StatusTextBlock.Text = "Xác nhận mật khẩu mới không khớp.";
            StatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        try
        {
            SaveButton.IsEnabled = false;
            StatusTextBlock.Text = "Đang lưu...";
            StatusTextBlock.Foreground = Brushes.DimGray;

            var response = await _httpClient.PutAsJsonAsync(_buildApiUrl($"/auth/admin/users/{_user.Id}/password"), new
            {
                password = newPassword
            }, _jsonOptions());

            if (response.IsSuccessStatusCode)
            {
                MessageBox.Show($"Đã thay đổi mật khẩu cho tài khoản '{_user.Username}' thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                StatusTextBlock.Text = $"Lỗi: {error}";
                StatusTextBlock.Foreground = Brushes.Firebrick;
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Lỗi kết nối: {ex.Message}";
            StatusTextBlock.Foreground = Brushes.Firebrick;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
