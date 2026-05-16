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
    private bool _manualUnlockMode;
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

    public void SetManualUnlockMode(bool enabled)
    {
        _manualUnlockMode = enabled;
        TitleTextBlock.Text = enabled ? "Kh\u00f3a m\u00e1y t\u1ea1m th\u1eddi" : "\u0110\u0103ng nh\u1eadp m\u00e1y tr\u1ea1m";
        ManualUnlockPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        LoginModeTabControl.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;

        if (!enabled)
        {
            ManualUnlockPasswordBox.Password = string.Empty;
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
            BackgroundContainer.Visibility = Visibility.Visible;
            TryApplyImageBackground(_backgroundSource);
            return;
        }

        BackgroundContainer.Visibility = Visibility.Visible;
        TryApplyVideoBackground(_backgroundSource);
    }

    public void PrepareForLock()
    {
        _isAuthenticating = false;
        UsernameTextBox.IsEnabled = true;
        PasswordBox.IsEnabled = true;
        ManualUnlockPasswordBox.IsEnabled = true;
        LoginButton.IsEnabled = true;
        ManualUnlockButton.IsEnabled = true;
        GuestLoginButton.IsEnabled = true;
        SaveServerButton.IsEnabled = true;
        ServerIpTextBox.IsEnabled = true;
        LoginButton.Content = "\u0110\u0102NG NH\u1eacP";
        ManualUnlockButton.Content = "M\u1edf kh\u00f3a m\u00e1y";
        GuestLoginButton.Content = "B\u1eaft \u0111\u1ea7u phi\u00ean Kh\u00e1ch";
        SaveServerButton.Content = "C\u1eadp nh\u1eadt & K\u1ebft n\u1ed1i l\u1ea1i";
        if (_manualUnlockMode)
        {
            TitleTextBlock.Text = "Khóa máy tạm thời";
            ManualUnlockPanel.Visibility = Visibility.Visible;
            LoginModeTabControl.Visibility = Visibility.Collapsed;
        }
        else
        {
            TitleTextBlock.Text = "Đăng nhập máy trạm";
            ManualUnlockPanel.Visibility = Visibility.Collapsed;
            LoginModeTabControl.Visibility = Visibility.Visible;
            LoginModeTabControl.SelectedItem = MemberTabItem;
        }

        PasswordBox.Password = string.Empty;
        ManualUnlockPasswordBox.Password = string.Empty;
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
        
        ApplyBackgroundConfiguration(_backgroundMode, _backgroundSource);

        // Force GC to clear unused assets and reduce RAM usage
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Show();
        Activate();
        if (_manualUnlockMode)
        {
            ManualUnlockPasswordBox.Focus();
        }
        else
        {
            UsernameTextBox.Focus();
        }
    }

    public new void Hide()
    {
        base.Hide();
        
        // Clear background media to save resources
        ClearBackgroundMedia();
        
        // Force GC after hiding to release memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public void AllowShutdown()
    {
        _allowClose = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
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
            ErrorTextBlock.Text = "\u1ee8ng d\u1ee5ng ch\u01b0a s\u1eb5n s\u00e0ng.";
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

            ErrorTextBlock.Text = result.Message ?? "Kh\u00f4ng th\u1ec3 \u0111\u0103ng nh\u1eadp kh\u00e1ch v\u00e3ng lai.";
        }
        catch (System.Exception ex)
        {
            ErrorTextBlock.Text = $"L\u1ec7i h\u1ec7 th\u1ed1ng: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SubmitLoginAsync();
    }

    private void ManualUnlockPasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        SubmitManualUnlock();
    }

    private void ManualUnlockButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitManualUnlock();
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
            ServerSetupStatusTextBlock.Text = "\u1ee8ng d\u1ee5ng ch\u01b0a s\u1eb5n s\u00e0ng.";
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
            ServerSetupStatusTextBlock.Text = result.Message ?? "C\u1eadp nh\u1eadt server th\u1ea5t b\u1ea1i.";
        }
        catch (System.Exception ex)
        {
            ServerSetupStatusTextBlock.Foreground =
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Firebrick);
            ServerSetupStatusTextBlock.Text = $"L\u1ec7i h\u1ec7 th\u1ed1ng: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async System.Threading.Tasks.Task SubmitLoginAsync()
    {
        if (_isAuthenticating)
        {
            return;
        }

        if (_manualUnlockMode)
        {
            SubmitManualUnlock();
            return;
        }

        var username = UsernameTextBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            ErrorTextBlock.Text = "Vui l\u00f2ng nh\u1eadp t\u00ean \u0111\u0103ng nh\u1eadp v\u00e0 m\u1eadt kh\u1ea9u.";
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

            ErrorTextBlock.Text = result.Message ?? "T\u00ean \u0111\u0103ng nh\u1eadp ho\u1eb7c m\u1eadt kh\u1ea9u kh\u00f4ng \u0111\u00fang.";
        }
        catch (System.Exception ex)
        {
            ErrorTextBlock.Text = $"L\u1ec7i k\u1ebft n\u1ed1i: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SubmitManualUnlock()
    {
        if (_isAuthenticating || !_manualUnlockMode)
        {
            return;
        }

        var password = ManualUnlockPasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            ErrorTextBlock.Text = "Vui l\u00f2ng nh\u1eadp m\u1eadt m\u00e3 \u0111\u00e3 \u0111\u1eb7t.";
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

            var result = app.TryUnlockWithManualPassword(password);
            if (result.Success)
            {
                ManualUnlockPasswordBox.Password = string.Empty;
                ErrorTextBlock.Text = string.Empty;
                return;
            }

            ErrorTextBlock.Text = result.Message ?? "M\u1eadt m\u00e3 kh\u00f4ng \u0111\u00fang.";
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
        ManualUnlockPasswordBox.IsEnabled = !isBusy;
        LoginButton.IsEnabled = !isBusy;
        ManualUnlockButton.IsEnabled = !isBusy;
        GuestLoginButton.IsEnabled = !isBusy;
        SaveServerButton.IsEnabled = !isBusy;
        ServerIpTextBox.IsEnabled = !isBusy;

        if (isBusy)
        {
            if (isGuest)
            {
                GuestLoginButton.Content = "\u0110ang x\u1eed l\u00fd...";
            }
            else if (_manualUnlockMode)
            {
                ManualUnlockButton.Content = "\u0110ang ki\u1ec3m tra...";
            }
            else
            {
                LoginButton.Content = "\u0110ang ki\u1ec3m tra...";
            }

            SaveServerButton.Content = "\u0110ang c\u1eadp nh\u1eadt...";
        }
        else
        {
            LoginButton.Content = "ĐĂNG NHẬP";
            ManualUnlockButton.Content = "Mở khóa máy";
            GuestLoginButton.Content = "Bắt đầu phiên Khách";
            SaveServerButton.Content = "Cập nhật & Kết nối lại";
        }
    }

    private static string NormalizeBackgroundMode(string? mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Contains("image") || normalized.Contains("ảnh")) return "image";
        if (normalized.Contains("video")) return "video";
        return "none";
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
        BackgroundContainer.Visibility = Visibility.Collapsed;
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
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 1024;
            image.EndInit();
            
            if (image.CanFreeze) image.Freeze();

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
            BackgroundVideoElement.Position = System.TimeSpan.Zero;
            BackgroundVideoElement.Play();
        }
        catch
        {
            ClearBackgroundMedia();
        }
    }


    private static System.Uri BuildBackgroundUri(string source)
    {
        var raw = (source ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new System.ArgumentException("Background source is empty.", nameof(source));
        }

        if (System.Uri.TryCreate(raw, System.UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        var expanded = System.Environment.ExpandEnvironmentVariables(raw);
        if (!System.IO.Path.IsPathRooted(expanded))
        {
            expanded = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, expanded));
        }

        return new System.Uri(expanded, System.UriKind.Absolute);
    }

    private void BackgroundVideoElement_MediaEnded(object sender, RoutedEventArgs e)
    {
        try
        {
            BackgroundVideoElement.Position = System.TimeSpan.Zero;
            BackgroundVideoElement.Play();
        }
        catch
        {
        }
    }

    private void BackgroundVideoElement_MediaFailed(object sender, System.Windows.ExceptionRoutedEventArgs e)
    {
        ClearBackgroundMedia();
    }
}
