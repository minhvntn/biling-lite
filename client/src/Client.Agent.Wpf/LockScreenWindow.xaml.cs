using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Client.Agent.Wpf;

public partial class LockScreenWindow : Window
{
    private bool _allowClose;
    private bool _isAuthenticating;
    private string _backgroundMode = "none";
    private string _backgroundSource = string.Empty;

    public LockScreenWindow()
    {
        InitializeComponent();
    }

    public void SetGuestLoginEnabled(bool isEnabled)
    {
        GuestTabItem.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!isEnabled && LoginModeTabControl.SelectedItem == GuestTabItem)
        {
            LoginModeTabControl.SelectedItem = MemberTabItem;
        }
    }

    public void SetCurrentServerUrl(string serverUrl)
    {
        ServerIpTextBox.Text = (serverUrl ?? string.Empty).Trim();
    }

    public void ApplyBackgroundConfiguration(string? mode, string? source)
    {
        _backgroundMode = NormalizeBackgroundMode(mode);
        _backgroundSource = (source ?? string.Empty).Trim();

        if (_backgroundMode == "none" || string.IsNullOrWhiteSpace(_backgroundSource))
        {
            ClearBackgroundMedia();
            return;
        }

        if (_backgroundMode == "image")
        {
            TryApplyImageBackground(_backgroundSource);
            return;
        }

        TryApplyVideoBackground(_backgroundSource);
    }

    public void PrepareForLock()
    {
        _isAuthenticating = false;
        UsernameTextBox.IsEnabled = true;
        PasswordBox.IsEnabled = true;
        LoginButton.IsEnabled = true;
        GuestLoginButton.IsEnabled = true;
        SaveServerButton.IsEnabled = true;
        ServerIpTextBox.IsEnabled = true;
        LoginButton.Content = "Đăng nhập";
        GuestLoginButton.Content = "Sử dụng Khách vãng lai";
        SaveServerButton.Content = "Lưu và kết nối lại";
        LoginModeTabControl.SelectedItem = MemberTabItem;

        PasswordBox.Password = string.Empty;
        ErrorTextBlock.Text = string.Empty;
        ServerSetupStatusTextBlock.Text = string.Empty;

        if (_backgroundMode == "video" &&
            !string.IsNullOrWhiteSpace(_backgroundSource) &&
            BackgroundVideoElement.Source is not null)
        {
            try
            {
                BackgroundVideoElement.Position = TimeSpan.Zero;
                BackgroundVideoElement.Play();
            }
            catch
            {
            }
        }

        Show();
        Activate();
        UsernameTextBox.Focus();
    }

    public void AllowShutdown()
    {
        _allowClose = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        // Prevent users from closing the lock overlay directly.
        e.Cancel = true;
        Hide();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await SubmitLoginAsync();
    }

    private async void GuestLoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isAuthenticating)
        {
            return;
        }

        if (Application.Current is not App app)
        {
            ErrorTextBlock.Text = "Ứng dụng chưa sẵn sàng.";
            return;
        }

        try
        {
            SetBusy(true, true);
            ErrorTextBlock.Text = string.Empty;

            var result = await app.TryUnlockAsGuestAsync();
            if (result.Success)
            {
                UsernameTextBox.Text = string.Empty;
                PasswordBox.Password = string.Empty;
                ErrorTextBlock.Text = string.Empty;
                return;
            }

            ErrorTextBlock.Text = result.Message ?? "Không thể đăng nhập khách vãng lai.";
        }
        catch (Exception ex)
        {
            ErrorTextBlock.Text = $"Lỗi hệ thống: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SubmitLoginAsync();
    }

    private async void SaveServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isAuthenticating)
        {
            return;
        }

        if (Application.Current is not App app)
        {
            ServerSetupStatusTextBlock.Foreground =
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Firebrick);
            ServerSetupStatusTextBlock.Text = "Ứng dụng chưa sẵn sàng.";
            return;
        }

        try
        {
            SetBusy(true);
            ErrorTextBlock.Text = string.Empty;
            ServerSetupStatusTextBlock.Text = string.Empty;

            var result = await app.UpdateServerEndpointFromLockScreenAsync(ServerIpTextBox.Text.Trim());
            if (result.Success)
            {
                ServerSetupStatusTextBlock.Foreground =
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkGreen);
                ServerSetupStatusTextBlock.Text = result.Message;
                return;
            }

            ServerSetupStatusTextBlock.Foreground =
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Firebrick);
            ServerSetupStatusTextBlock.Text = result.Message ?? "Cập nhật server thất bại.";
        }
        catch (Exception ex)
        {
            ServerSetupStatusTextBlock.Foreground =
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Firebrick);
            ServerSetupStatusTextBlock.Text = $"Lỗi hệ thống: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SubmitLoginAsync()
    {
        if (_isAuthenticating)
        {
            return;
        }

        var username = UsernameTextBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            ErrorTextBlock.Text = "Vui lòng nhập tên đăng nhập và mật khẩu.";
            return;
        }

        if (Application.Current is not App app)
        {
            ErrorTextBlock.Text = "Ứng dụng chưa sẵn sàng.";
            return;
        }

        try
        {
            SetBusy(true);
            ErrorTextBlock.Text = string.Empty;

            var result = await app.TryUnlockFromLockScreenAsync(username, password);
            if (result.Success)
            {
                UsernameTextBox.Text = string.Empty;
                PasswordBox.Password = string.Empty;
                ErrorTextBlock.Text = string.Empty;
                return;
            }

            ErrorTextBlock.Text = result.Message ?? "Tên đăng nhập hoặc mật khẩu không đúng.";
        }
        catch (Exception ex)
        {
            ErrorTextBlock.Text = $"Lỗi kết nối: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy, bool isGuest = false)
    {
        _isAuthenticating = isBusy;
        UsernameTextBox.IsEnabled = !isBusy;
        PasswordBox.IsEnabled = !isBusy;
        LoginButton.IsEnabled = !isBusy;
        GuestLoginButton.IsEnabled = !isBusy;
        SaveServerButton.IsEnabled = !isBusy;
        ServerIpTextBox.IsEnabled = !isBusy;

        if (isBusy)
        {
            if (isGuest)
            {
                GuestLoginButton.Content = "Đang xử lý...";
            }
            else
            {
                LoginButton.Content = "Đang kiểm tra...";
            }

            SaveServerButton.Content = "Đang cập nhật...";
        }
        else
        {
            LoginButton.Content = "Đăng nhập";
            GuestLoginButton.Content = "Sử dụng Khách vãng lai";
            SaveServerButton.Content = "Lưu và kết nối lại";
        }
    }

    private static string NormalizeBackgroundMode(string? mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "image" or "video" ? normalized : "none";
    }

    private void ClearBackgroundMedia()
    {
        try
        {
            BackgroundVideoElement.Stop();
        }
        catch
        {
        }

        BackgroundVideoElement.Source = null;
        BackgroundVideoElement.Visibility = Visibility.Collapsed;
        BackgroundImageElement.Source = null;
        BackgroundImageElement.Visibility = Visibility.Collapsed;
    }

    private void TryApplyImageBackground(string source)
    {
        try
        {
            var uri = BuildBackgroundUri(source);
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();

            BackgroundImageElement.Source = image;
            BackgroundImageElement.Visibility = Visibility.Visible;

            BackgroundVideoElement.Stop();
            BackgroundVideoElement.Source = null;
            BackgroundVideoElement.Visibility = Visibility.Collapsed;
        }
        catch
        {
            ClearBackgroundMedia();
        }
    }

    private void TryApplyVideoBackground(string source)
    {
        try
        {
            var uri = BuildBackgroundUri(source);
            BackgroundImageElement.Source = null;
            BackgroundImageElement.Visibility = Visibility.Collapsed;

            BackgroundVideoElement.Source = uri;
            BackgroundVideoElement.Visibility = Visibility.Visible;
            BackgroundVideoElement.Position = TimeSpan.Zero;
            BackgroundVideoElement.Play();
        }
        catch
        {
            ClearBackgroundMedia();
        }
    }

    private static Uri BuildBackgroundUri(string source)
    {
        var raw = (source ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("Background source is empty.", nameof(source));
        }

        if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        var expanded = Environment.ExpandEnvironmentVariables(raw);
        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, expanded));
        }

        return new Uri(expanded, UriKind.Absolute);
    }

    private void BackgroundVideoElement_MediaEnded(object sender, RoutedEventArgs e)
    {
        try
        {
            BackgroundVideoElement.Position = TimeSpan.Zero;
            BackgroundVideoElement.Play();
        }
        catch
        {
        }
    }

    private void BackgroundVideoElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        ClearBackgroundMedia();
    }
}


