using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Client.Agent.Wpf;

public partial class LockScreenWindow : Window
{
    private bool _allowClose;
    private bool _isAuthenticating;

    public LockScreenWindow()
    {
        InitializeComponent();
    }

    public void SetGuestLoginEnabled(bool isEnabled)
    {
        GuestLoginButton.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    public void PrepareForLock()
    {
        _isAuthenticating = false;
        UsernameTextBox.IsEnabled = true;
        PasswordBox.IsEnabled = true;
        LoginButton.IsEnabled = true;
        GuestLoginButton.IsEnabled = true;
        LoginButton.Content = "Đăng nhập";
        GuestLoginButton.Content = "Sử dụng Khách vãng lai";

        PasswordBox.Password = string.Empty;
        ErrorTextBlock.Text = string.Empty;

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
        }
        else
        {
            LoginButton.Content = "Đăng nhập";
            GuestLoginButton.Content = "Sử dụng Khách vãng lai";
        }
    }
}
