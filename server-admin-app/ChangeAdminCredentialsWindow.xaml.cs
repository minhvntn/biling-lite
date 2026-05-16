using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class ChangeAdminCredentialsWindow : Window
{
    private readonly HttpClient _httpClient;
    private readonly Func<string, string> _buildApiUrl;
    private readonly Func<System.Text.Json.JsonSerializerOptions> _jsonOptions;

    public ChangeAdminCredentialsWindow(HttpClient httpClient, Func<string, string> buildApiUrl, Func<System.Text.Json.JsonSerializerOptions> jsonOptions)
    {
        InitializeComponent();
        _httpClient = httpClient;
        _buildApiUrl = buildApiUrl;
        _jsonOptions = jsonOptions;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameTextBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            StatusTextBlock.Text = "Vui lòng nhập đầy đủ Username và Mật khẩu.";
            StatusTextBlock.Foreground = Brushes.Firebrick;
            return;
        }

        try
        {
            SaveButton.IsEnabled = false;
            StatusTextBlock.Text = "Đang lưu...";
            StatusTextBlock.Foreground = Brushes.DimGray;

            var response = await _httpClient.PostAsJsonAsync(_buildApiUrl("/auth/admin/update-admin"), new
            {
                username,
                password,
                fullName = "Super Administrator"
            }, _jsonOptions());

            if (response.IsSuccessStatusCode)
            {
                MessageBox.Show("Thay đổi tài khoản Admin thành công! Vui lòng dùng tài khoản mới cho lần đăng nhập sau.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
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
