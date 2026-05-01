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

    public void PrepareForLock()
    {
        _isAuthenticating = false;
        UsernameTextBox.IsEnabled = true;
        PasswordBox.IsEnabled = true;
        LoginButton.IsEnabled = true;
        LoginButton.Content = "Đăng nhập";

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

            ErrorTextBlock.Text = result.Message;
        }
        catch (Exception ex)
        {
            ErrorTextBlock.Text = $"Đăng nhập lỗi: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _isAuthenticating = busy;
        UsernameTextBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;
        LoginButton.IsEnabled = !busy;
        LoginButton.Content = busy ? "Đang kiểm tra..." : "Đăng nhập";
    }
}
