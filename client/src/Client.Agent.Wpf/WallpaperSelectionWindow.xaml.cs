using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Client.Agent.Wpf
{
    public partial class WallpaperSelectionWindow : Window
    {
        private readonly HttpClient _httpClient;
        private readonly App _app;
        private readonly string _memberId;

        public WallpaperSelectionWindow(string memberId)
        {
            InitializeComponent();
            _app = (App)Application.Current;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", "SmbSecureLANKey2026");
            _memberId = memberId;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadingText.Visibility = Visibility.Visible;
                ErrorText.Visibility = Visibility.Collapsed;
                WallpapersContainer.Children.Clear();

                var apiUrl = _app.BuildApiUrl($"/members/{_memberId}/wallpapers");
                var response = await _httpClient.GetAsync(apiUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<WallpaperResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (data != null && data.AvailableWallpapers != null)
                    {
                        foreach (var wp in data.AvailableWallpapers)
                        {
                            var isSelected = wp == data.CurrentWallpaperId;
                            var item = CreateWallpaperItem(wp, isSelected);
                            WallpapersContainer.Children.Add(item);
                        }
                    }
                }
                else
                {
                    ErrorText.Text = "Không thể tải danh sách hình nền.";
                    ErrorText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Lỗi: {ex.Message}";
                ErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                LoadingText.Visibility = Visibility.Collapsed;
            }
        }

        private Border CreateWallpaperItem(string wallpaperFilename, bool isSelected)
        {
            var border = new Border
            {
                Width = 160,
                Height = 90,
                Margin = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(3),
                BorderBrush = isSelected ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")) : Brushes.Transparent,
                Cursor = Cursors.Hand,
                ClipToBounds = true
            };

            var image = new Image
            {
                Stretch = Stretch.UniformToFill
            };
            
            try
            {
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Wallpapers", wallpaperFilename);
                if (System.IO.File.Exists(path))
                {
                    var uri = new Uri(path, UriKind.Absolute);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = uri;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    image.Source = bmp;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image {wallpaperFilename}: {ex.Message}");
            }

            border.Child = image;
            border.MouseLeftButtonDown += async (s, e) => await SelectWallpaperAsync(wallpaperFilename);
            
            return border;
        }

        private async System.Threading.Tasks.Task SelectWallpaperAsync(string wallpaperFilename)
        {
            try
            {
                var apiUrl = _app.BuildApiUrl($"/members/{_memberId}/wallpaper");
                var content = new StringContent(JsonSerializer.Serialize(new { wallpaperId = wallpaperFilename }), Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PatchAsync(apiUrl, content);
                if (response.IsSuccessStatusCode)
                {
                    // Refresh UI
                    Window_Loaded(null!, null!);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set wallpaper: {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private class WallpaperResponse
        {
            public string? CurrentWallpaperId { get; set; }
            public List<string>? AvailableWallpapers { get; set; }
        }
    }
}
