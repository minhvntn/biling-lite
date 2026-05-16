using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace Server.Admin.App;

public partial class LoginWindow : Window
{
    private readonly HttpClient _httpClient;
    
    public LoginWindow()
    {
        InitializeComponent();
        
        // Setup HttpClient with baseUrl
        var baseAddress = "http://localhost:9000"; // Default
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AdminShellSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (settings != null && !string.IsNullOrWhiteSpace(settings.BackendApiBaseUrl))
                {
                    baseAddress = settings.BackendApiBaseUrl.TrimEnd('/');
                }
            }
        }
        catch { }

        _httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await DoLoginAsync();
    }

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await DoLoginAsync();
        }
    }

    private async Task DoLoginAsync()
    {
        var username = UsernameTextBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("Vui lòng nhập tên đăng nhập và mật khẩu.");
            return;
        }

        SetLoading(true);

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/auth/admin/login", new { username, password });
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (result.TryGetProperty("success", out var success) && success.GetBoolean())
                {
                    // Optionally save token/user info somewhere
                    var userRole = result.GetProperty("user").GetProperty("role").GetString();
                    var userFullName = result.GetProperty("user").GetProperty("fullName").GetString();
                    
                    // We could store user info in a static context or pass it to MainWindow
                    AdminSession.CurrentUserRole = userRole;
                    AdminSession.CurrentUserFullName = userFullName;

                    var mainWindow = new MainWindow();
                    mainWindow.Show();
                    this.Close();
                }
                else
                {
                    ShowError("Đăng nhập thất bại.");
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ShowError("Sai tên đăng nhập hoặc mật khẩu.");
            }
            else
            {
                ShowError($"Lỗi kết nối máy chủ ({response.StatusCode}).");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Lỗi kết nối: {ex.Message}");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void SetLoading(bool isLoading)
    {
        LoginButton.IsEnabled = !isLoading;
        UsernameTextBox.IsEnabled = !isLoading;
        PasswordBox.IsEnabled = !isLoading;
        LoadingTextBlock.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        if (isLoading)
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;
        }
    }
}

public static class AdminSession
{
    public static string? CurrentUserRole { get; set; }
    public static string? CurrentUserFullName { get; set; }
}
