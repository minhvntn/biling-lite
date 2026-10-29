using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;

namespace Server.Admin.App.Windows;

public partial class ChangeMemberPasswordWindow : Window
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrlBase;
    private readonly MemberRow _member;
    private bool _isPasswordVisible = false;

    public ChangeMemberPasswordWindow(HttpClient httpClient, string apiUrlBase, MemberRow member)
    {
        InitializeComponent();

        _httpClient = httpClient;
        _apiUrlBase = apiUrlBase.TrimEnd('/');
        _member = member;

        MemberUsernameTextBlock.Text = (member.Username ?? string.Empty).ToUpperInvariant();
        MemberFullNameTextBlock.Text = string.IsNullOrWhiteSpace(member.FullName) ? "-" : member.FullName;

        Loaded += (s, e) =>
        {
            NewPasswordBox.Focus();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        if (_isPasswordVisible)
        {
            NewPasswordVisibleBox.Text = NewPasswordBox.Password;
            NewPasswordVisibleBox.Visibility = Visibility.Visible;
            NewPasswordBox.Visibility = Visibility.Collapsed;
            EyeIconTextBlock.Text = "🙈";
            NewPasswordVisibleBox.Focus();
            NewPasswordVisibleBox.CaretIndex = NewPasswordVisibleBox.Text.Length;
        }
        else
        {
            NewPasswordBox.Password = NewPasswordVisibleBox.Text;
            NewPasswordBox.Visibility = Visibility.Visible;
            NewPasswordVisibleBox.Visibility = Visibility.Collapsed;
            EyeIconTextBlock.Text = "👁️";
            NewPasswordBox.Focus();
        }
    }

    private void SetDefaultPassword_Click(object sender, RoutedEventArgs e)
    {
        NewPasswordBox.Password = "123456";
        NewPasswordVisibleBox.Text = "123456";
        if (_isPasswordVisible)
        {
            NewPasswordVisibleBox.Focus();
            NewPasswordVisibleBox.CaretIndex = 6;
        }
        else
        {
            NewPasswordBox.Focus();
        }
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveButton_Click(sender, e);
        }
    }

    private string GetCurrentPassword()
    {
        return _isPasswordVisible ? NewPasswordVisibleBox.Text : NewPasswordBox.Password;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var newPassword = GetCurrentPassword();
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            ErrorTextBlock.Text = "Vui lòng nhập mật khẩu mới.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        SetLoading(true);

        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["password"] = newPassword.Trim(),
                ["updatedBy"] = AdminSession.CurrentUsername ?? "admin",
                ["note"] = "Đổi mật khẩu nhanh tại quầy"
            };

            using var response = await _httpClient.PatchAsJsonAsync(
                $"{_apiUrlBase}/members/{_member.Id}",
                payload);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                ErrorTextBlock.Text = string.IsNullOrWhiteSpace(err)
                    ? $"Đổi mật khẩu thất bại ({(int)response.StatusCode})"
                    : err;
                ErrorTextBlock.Visibility = Visibility.Visible;
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorTextBlock.Text = $"Lỗi kết nối: {ex.Message}";
            ErrorTextBlock.Visibility = Visibility.Visible;
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SetLoading(bool isLoading)
    {
        SaveButton.IsEnabled = !isLoading;
        NewPasswordBox.IsEnabled = !isLoading;
        NewPasswordVisibleBox.IsEnabled = !isLoading;
        SaveButton.Content = isLoading ? "Đang lưu..." : "Lưu mật khẩu";
        if (isLoading)
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;
        }
    }
}

