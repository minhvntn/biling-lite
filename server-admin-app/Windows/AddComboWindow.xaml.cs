using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace Server.Admin.App.Windows
{
    public partial class AddComboWindow : Window
    {
        private string? _comboId = null;

        public AddComboWindow(Server.Admin.App.Windows.Controls.ComboTabControl.ComboPackage? existingCombo = null)
        {
            InitializeComponent();
            PopulateTimeComboBoxes();

            if (existingCombo != null)
            {
                _comboId = existingCombo.id;
                this.Title = "Sửa Gói Combo";
                TxtName.Text = existingCombo.name;
                TxtPrice.Text = existingCombo.price.ToString("0");
                CboStartTime.Text = existingCombo.startTime;
                CboEndTime.Text = existingCombo.endTime;
                TxtValidityDays.Text = existingCombo.validityDays.ToString();
                TxtDurationHours.Text = existingCombo.durationHours.ToString();
            }
        }

        private void PopulateTimeComboBoxes()
        {
            var times = new System.Collections.Generic.List<string>();
            for (int h = 0; h < 24; h++)
            {
                times.Add($"{h:00}:00");
                times.Add($"{h:00}:30");
            }

            CboStartTime.ItemsSource = times;
            CboEndTime.ItemsSource = times;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtName.Text) || 
                string.IsNullOrWhiteSpace(CboStartTime.Text) || 
                string.IsNullOrWhiteSpace(CboEndTime.Text))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ thông tin", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(TxtPrice.Text, out decimal price))
            {
                MessageBox.Show("Giá tiền không hợp lệ", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int validityDays = 1;
            int.TryParse(TxtValidityDays.Text, out validityDays);

            int durationHours = 0;
            int.TryParse(TxtDurationHours.Text, out durationHours);

            var payload = new
            {
                name = TxtName.Text.Trim(),
                price = price,
                startTime = CboStartTime.Text.Trim(),
                endTime = CboEndTime.Text.Trim(),
                validityDays = validityDays,
                durationHours = durationHours,
                isActive = true
            };

            try
            {
                var mainWindow = (MainWindow)Application.Current.MainWindow;
                var client = mainWindow._httpClient;
                
                string url;
                HttpResponseMessage response;
                var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                if (!string.IsNullOrEmpty(_comboId))
                {
                    url = mainWindow.BuildApiUrl($"/combos/{_comboId}");
                    response = await client.PutAsync(url, jsonContent);
                }
                else
                {
                    url = mainWindow.BuildApiUrl("/combos");
                    response = await client.PostAsync(url, jsonContent);
                }

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show(string.IsNullOrEmpty(_comboId) ? "Thêm gói Combo thành công!" : "Cập nhật gói Combo thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.DialogResult = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Lỗi: " + await response.Content.ReadAsStringAsync(), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi hệ thống: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
