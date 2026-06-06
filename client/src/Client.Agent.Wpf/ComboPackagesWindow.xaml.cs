using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Client.Agent.Wpf
{
    public partial class ComboPackagesWindow : Window
    {
        public ComboPackagesWindow()
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) this.DragMove(); };
            _ = LoadCombosAsync();
        }

        private async Task LoadCombosAsync()
        {
            try
            {
                LoadingText.Visibility = Visibility.Visible;
                ComboListBox.Visibility = Visibility.Collapsed;

                var app = (App)Application.Current;
                using var client = new System.Net.Http.HttpClient();
                var url = app.BuildApiUrl("/combos");
                var response = await client.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var combos = await response.Content.ReadFromJsonAsync<List<ComboPackageModel>>();
                    if (combos != null)
                    {
                        ComboListBox.ItemsSource = combos;
                        TotalCountText.Text = combos.Count.ToString();
                    }
                }
                else
                {
                    MessageBox.Show("Không thể tải danh sách Combo.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi kết nối: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingText.Visibility = Visibility.Collapsed;
                ComboListBox.Visibility = Visibility.Visible;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class ComboPackageModel
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public decimal price { get; set; }
        public string startTime { get; set; } = string.Empty;
        public string endTime { get; set; } = string.Empty;
        public int validityDays { get; set; }
        public int durationHours { get; set; }
    }
}
