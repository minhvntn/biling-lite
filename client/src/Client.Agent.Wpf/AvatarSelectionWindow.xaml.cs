using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Client.Agent.Wpf
{
    public partial class AvatarSelectionWindow : Window
    {
        private readonly string _memberId;
        private readonly HttpClient _httpClient;
        private string _currentAvatarId = "avatar_1.png";

        public AvatarSelectionWindow(string memberId)
        {
            InitializeComponent();
            _memberId = memberId;
            
            // Re-use HttpClient from App
            var app = (App)Application.Current;
            _httpClient = new HttpClient(); // For simplicity here, or use App's if exposed. Actually, Wpf app uses `_httpClient` but it's private in App.
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", "SmbSecureLANKey2026");
            // Let's grab the API URL from App
            
            Loaded += AvatarSelectionWindow_Loaded;
            
            // Setup window dragging
            MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        }

        private async void AvatarSelectionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAvatarsAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async Task LoadAvatarsAsync()
        {
            try
            {
                LoadingText.Visibility = Visibility.Visible;
                AvatarsContainer.Children.Clear();

                var app = (App)Application.Current;
                var apiUrl = app.BuildApiUrl($"/members/{_memberId}/avatars");

                var response = await _httpClient.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    throw new Exception($"HTTP {response.StatusCode}: {errorBody}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                _currentAvatarId = root.GetProperty("currentAvatarId").GetString() ?? "avatar_1.png";
                if (string.IsNullOrWhiteSpace(_currentAvatarId)) _currentAvatarId = "avatar_1.png";

                var availableAvatars = new HashSet<string>();
                if (root.TryGetProperty("availableAvatars", out var avatarsArray))
                {
                    foreach (var item in avatarsArray.EnumerateArray())
                    {
                        var name = item.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            availableAvatars.Add(name);
                        }
                    }
                }

                RenderAvatars(availableAvatars);
            }
            catch (HttpRequestException ex)
            {
                var errorBody = "No additional details";
                MessageBox.Show($"Lỗi HTTP: {ex.Message}\nDetails: {errorBody}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi tải danh sách Avatar: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingText.Visibility = Visibility.Collapsed;
            }
        }

        private void RenderAvatars(HashSet<string> availableAvatars)
        {
            AvatarsContainer.Children.Clear();

            var styles = new[] {
                new { Id = "", Name = "Fantasy RPG", Count = 10 },
                new { Id = "_bottts", Name = "Robot Sci-Fi", Count = 10 },
                new { Id = "_micah", Name = "Minimalist", Count = 10 },
                new { Id = "_adventurer-neutral", Name = "Adventurer", Count = 10 },
                new { Id = "_roblox", Name = "Roblox", Count = 10 },
                new { Id = "_tutien", Name = "Tu Tiên (Kiếm Hiệp)", Count = 3 },
                new { Id = "_wukong", Name = "Tề Thiên Đại Thánh", Count = 2 },
                new { Id = "_starcraft", Name = "Starcraft", Count = 2 },
                new { Id = "_mecha", Name = "Mecha Robot", Count = 2 },
                new { Id = "_cute", Name = "Cute & Đáng Yêu", Count = 5 },
                new { Id = "_3d", Name = "Emoji 3D", Count = 5 },
                new { Id = "_cartoon", Name = "Hoạt Hình Cartoon", Count = 5 },
                new { Id = "_superhero", Name = "Siêu Nhân (Rank)", Count = 3 },
                new { Id = "_demon", Name = "Ác Quỷ (Rank)", Count = 3 },
                new { Id = "_angel", Name = "Thiên Thần (Rank)", Count = 3 },
                new { Id = "_ghost", Name = "Ghost (Rank)", Count = 3 },
                new { Id = "_dragonball", Name = "Dragonball (Rank)", Count = 3 }
            };

            foreach (var style in styles)
            {
                var groupTitle = new TextBlock
                {
                    Text = style.Name,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    Margin = new Thickness(0, 15, 0, 5)
                };
                AvatarsContainer.Children.Add(groupTitle);

                var wrapPanel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
                AvatarsContainer.Children.Add(wrapPanel);

                for (int i = 1; i <= style.Count; i++)
                {
                    var fileName = $"avatar{style.Id}_{i}.png";
                    var isUnlocked = availableAvatars.Contains(fileName);
                    var isCurrent = _currentAvatarId == fileName;

                    var border = new Border
                    {
                        Width = 60,
                        Height = 60,
                        Margin = new Thickness(5),
                        CornerRadius = new CornerRadius(30),
                        BorderThickness = new Thickness(3),
                        BorderBrush = isCurrent ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                        Cursor = isUnlocked ? Cursors.Hand : Cursors.Arrow,
                        Opacity = isUnlocked ? 1.0 : 0.4
                    };

                    var imgSource = ResolveIconSource(fileName);
                    var imgBrush = new ImageBrush { Stretch = Stretch.UniformToFill };
                    if (imgSource != null)
                    {
                        imgBrush.ImageSource = imgSource;
                    }
                    
                    border.Background = imgBrush;

                    if (!isUnlocked)
                    {
                        var lockIcon = new TextBlock
                        {
                            Text = "🔒",
                            FontSize = 24,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = Brushes.White
                        };
                        border.Child = lockIcon;
                    }
                    else
                    {
                        border.MouseLeftButtonDown += async (s, e) => await SelectAvatarAsync(fileName);
                        
                        // Hover effect
                        border.MouseEnter += (s, e) => { if (_currentAvatarId != fileName) border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")); };
                        border.MouseLeave += (s, e) => { if (_currentAvatarId != fileName) border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")); };
                    }

                    wrapPanel.Children.Add(border);
                }
            }
        }

        private async Task SelectAvatarAsync(string fileName)
        {
            if (_currentAvatarId == fileName) return;

            try
            {
                LoadingText.Visibility = Visibility.Visible;
                AvatarsContainer.Visibility = Visibility.Collapsed;

                var app = (App)Application.Current;
                var apiUrl = app.BuildApiUrl($"/members/{_memberId}/avatar");
                var payload = new { avatarId = fileName };
                var response = await _httpClient.PatchAsJsonAsync(apiUrl, payload);
                response.EnsureSuccessStatusCode();

                _currentAvatarId = fileName;

                // Update MainWindow
                if (Owner is MainWindow main)
                {
                    var src = ResolveIconSource(fileName);
                    if (src != null)
                    {
                        main.AvatarButton.Background = new ImageBrush(src) { Stretch = Stretch.UniformToFill };
                    }
                }

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi cập nhật Avatar: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadingText.Visibility = Visibility.Collapsed;
                AvatarsContainer.Visibility = Visibility.Visible;
            }
        }

        private async void UploadAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Chọn ảnh đại diện",
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp|All Files|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() != true) return;

            try
            {
                LoadingText.Visibility = Visibility.Visible;
                AvatarsContainer.Visibility = Visibility.Collapsed;

                var bytes = File.ReadAllBytes(openFileDialog.FileName);
                if (!TryOptimizeImageForAvatar(bytes, out var optimizedBytes))
                {
                    MessageBox.Show("Không thể tối ưu ảnh. Vui lòng chọn ảnh khác nhỏ hơn.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var base64 = $"data:image/jpeg;base64,{Convert.ToBase64String(optimizedBytes)}";
                await SelectAvatarAsync(base64);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi đọc file ảnh: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingText.Visibility = Visibility.Collapsed;
                AvatarsContainer.Visibility = Visibility.Visible;
            }
        }

        private static bool TryOptimizeImageForAvatar(byte[] sourceBytes, out byte[] optimizedBytes)
        {
            optimizedBytes = Array.Empty<byte>();
            try
            {
                using var sourceStream = new MemoryStream(sourceBytes);
                var sourceImage = new BitmapImage();
                sourceImage.BeginInit();
                sourceImage.CacheOption = BitmapCacheOption.OnLoad;
                sourceImage.StreamSource = sourceStream;
                sourceImage.EndInit();
                sourceImage.Freeze();

                if (sourceImage.PixelWidth <= 0 || sourceImage.PixelHeight <= 0) return false;

                const int maxDimension = 80;
                const int targetBytes = 150 * 1024;
                var qualityLevels = new[] { 90, 80, 70, 60 };

                var scale = Math.Min(1d, maxDimension / (double)Math.Max(sourceImage.PixelWidth, sourceImage.PixelHeight));
                
                BitmapSource frameSource = sourceImage;
                if (scale < 0.999d)
                {
                    var transformed = new TransformedBitmap(sourceImage, new ScaleTransform(scale, scale));
                    transformed.Freeze();
                    frameSource = transformed;
                }

                foreach (var quality in qualityLevels)
                {
                    var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                    encoder.Frames.Add(BitmapFrame.Create(frameSource));
                    using var output = new MemoryStream();
                    encoder.Save(output);
                    var encoded = output.ToArray();
                    if (encoded.Length <= targetBytes)
                    {
                        optimizedBytes = encoded;
                        return true;
                    }
                    if (optimizedBytes.Length == 0 || encoded.Length < optimizedBytes.Length) optimizedBytes = encoded;
                }
                return optimizedBytes.Length > 0;
            }
            catch { return false; }
        }

        public static ImageSource? ResolveIconSource(string iconAssetName)
        {
            if (iconAssetName.StartsWith("data:image/"))
            {
                try
                {
                    var commaIndex = iconAssetName.IndexOf(',');
                    if (commaIndex > 0)
                    {
                        var bytes = Convert.FromBase64String(iconAssetName.Substring(commaIndex + 1));
                        using var stream = new MemoryStream(bytes);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelHeight = 72;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    }
                }
                catch { return null; }
            }

            try
            {
                var packUri = new Uri($"pack://application:,,,/Assets/Avatars/{iconAssetName}", UriKind.Absolute);
                return new BitmapImage(packUri);
            }
            catch
            {
                try
                {
                    var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Avatars", iconAssetName);
                    if (!File.Exists(iconPath)) return null;
                    return new BitmapImage(new Uri(iconPath, UriKind.Absolute));
                }
                catch { return null; }
            }
        }
    }
}
