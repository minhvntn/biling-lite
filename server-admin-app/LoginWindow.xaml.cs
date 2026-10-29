using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace Server.Admin.App;

public partial class LoginWindow : Window
{
    private readonly HttpClient _httpClient;
    
    public bool UserChoseLiteMode { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("X-API-KEY", "SmbSecureLANKey2026");

        Loaded += (s, e) =>
        {
            if (Owner != null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            UsernameTextBox.Focus();
        };
    }

    private string BuildLoginUrl()
    {
        var baseAddress = "http://localhost:39090/api/v1";
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

        if (!baseAddress.EndsWith("/")) baseAddress += "/";
        var baseUri = new Uri(baseAddress);
        if (!baseUri.AbsolutePath.Contains("/api/v1"))
        {
            return new Uri(baseUri, "api/v1/auth/admin/login").ToString();
        }
        return new Uri(baseUri, "auth/admin/login").ToString();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await DoLoginAsync();
    }

    private void SkipToLiteButton_Click(object sender, RoutedEventArgs e)
    {
        UserChoseLiteMode = true;
        try
        {
            this.DialogResult = false;
        }
        catch
        {
            this.Close();
        }
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
            var loginUrl = BuildLoginUrl();
            var response = await _httpClient.PostAsJsonAsync(loginUrl, new { username, password });
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (result.TryGetProperty("success", out var success) && success.GetBoolean())
                {
                    var userObj = result.GetProperty("user");
                    var userRole = userObj.TryGetProperty("role", out var r) ? r.GetString() : "ADMIN";
                    var userFullName = userObj.TryGetProperty("fullName", out var f) ? f.GetString() : username;
                    var userLoginName = userObj.TryGetProperty("username", out var u) ? u.GetString() : username;
                    
                    AdminSession.CurrentUserRole = userRole;
                    AdminSession.CurrentUserFullName = userFullName;
                    AdminSession.CurrentUsername = userLoginName;

                    UserChoseLiteMode = false;
                    try
                    {
                        this.DialogResult = true;
                    }
                    catch
                    {
                        this.Close();
                    }
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
        if (SkipToLiteButton != null) SkipToLiteButton.IsEnabled = !isLoading;
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
    public static string? CurrentUsername { get; set; }

    public static bool IsLoggedIn => !string.IsNullOrWhiteSpace(CurrentUserRole);

    public static void Clear()
    {
        CurrentUserRole = null;
        CurrentUserFullName = null;
        CurrentUsername = null;
    }
}
