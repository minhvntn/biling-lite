using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace Server.Admin.App.Windows
{
    public partial class EditMemberWindow : Window
    {
        private readonly HttpClient _httpClient;
        private readonly MemberRow _member;
        private readonly string _apiUrlBase;

        public EditMemberWindow(
            HttpClient httpClient,
            string apiUrlBase,
            MemberRow member,
            string lastLoginText,
            string totalUsageText,
            decimal lifetimeTopup)
        {
            InitializeComponent();
            _httpClient = httpClient;
            _apiUrlBase = apiUrlBase.TrimEnd('/');
            _member = member;

            UsernameBox.Text = member.Username;
            FullNameBox.Text = member.FullName;
            PhoneBox.Text = member.Phone == "-" ? string.Empty : member.Phone;
            IdentityBox.Text = member.IdentityNumber == "-" ? string.Empty : member.IdentityNumber;
            BalanceBox.Text = member.BalanceRaw.ToString("0.##", CultureInfo.InvariantCulture);
            PointsBox.Text = member.AvailablePoints.ToString();
            TotalTopupBox.Text = lifetimeTopup.ToString("0.##", CultureInfo.InvariantCulture);
            
            RankTextBlock.Text = member.Rank;
            LifetimeTopupTextBlock.Text = $"Tổng nạp: {lifetimeTopup:N0} VND";

            if (string.Equals(member.MemberType, "VIP", StringComparison.OrdinalIgnoreCase))
            {
                MemberTypeComboBox.SelectedIndex = 1;
            }
            else
            {
                MemberTypeComboBox.SelectedIndex = 0;
            }

            LastLoginBox.Text = lastLoginText;
            TotalUsageBox.Text = totalUsageText;
            StatusCheckBox.IsChecked = member.IsActive;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Text = string.Empty;

            if (!decimal.TryParse(BalanceBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var balance) || balance < 0)
            {
                ErrorTextBlock.Text = "Số dư không hợp lệ.";
                return;
            }
            if (!int.TryParse(PointsBox.Text.Trim(), out var points) || points < 0)
            {
                ErrorTextBlock.Text = "Điểm tích lũy không hợp lệ.";
                return;
            }
            if (!decimal.TryParse(TotalTopupBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var totalTopup) || totalTopup < 0)
            {
                ErrorTextBlock.Text = "Tổng nạp không hợp lệ.";
                return;
            }

            var fullName = FullNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(fullName))
            {
                ErrorTextBlock.Text = "Họ tên không được để trống.";
                return;
            }

            var selectedMemberType = (MemberTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "REGULAR";

            var payload = new Dictionary<string, object?>
            {
                ["fullName"] = fullName,
                ["phone"] = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim(),
                ["identityNumber"] = string.IsNullOrWhiteSpace(IdentityBox.Text) ? null : IdentityBox.Text.Trim(),
                ["isActive"] = StatusCheckBox.IsChecked == true,
                ["balance"] = Convert.ToDouble(balance),
                ["totalTopup"] = Convert.ToDouble(totalTopup),
                ["availablePoints"] = points,
                ["memberType"] = selectedMemberType,
                ["updatedBy"] = "admin.desktop",
                ["note"] = "Cap nhat tu app server admin",
            };

            var newPassword = PasswordBox.Password;
            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                payload["password"] = newPassword;
            }

            try
            {
                using var response = await _httpClient.PatchAsJsonAsync(
                    $"{_apiUrlBase}/members/{_member.Id}",
                    payload);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    ErrorTextBlock.Text = string.IsNullOrWhiteSpace(err)
                        ? $"Cập nhật thất bại ({(int)response.StatusCode})"
                        : err;
                    return;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ErrorTextBlock.Text = ex.Message;
            }
        }
    }
}
