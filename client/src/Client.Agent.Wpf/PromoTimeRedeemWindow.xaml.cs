using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace Client.Agent.Wpf
{
    public partial class PromoTimeRedeemWindow : Window
    {
        private readonly HttpClient _httpClient;
        private readonly App _app;
        private readonly string _memberId;
        private readonly bool _isVipSession;
        private readonly int _firstCardSeconds;
        private readonly int _totalPromoSeconds;
        private int _maxRedeemMinutes;

        public int RedeemedSeconds { get; private set; }

        public PromoTimeRedeemWindow(string memberId, bool isVipSession, int firstCardSeconds, int totalPromoSeconds)
        {
            InitializeComponent();
            _app = (App)Application.Current;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", "SmbSecureLANKey2026");
            _memberId = memberId;
            _isVipSession = isVipSession;
            _firstCardSeconds = Math.Max(0, firstCardSeconds);
            _totalPromoSeconds = Math.Max(0, totalPromoSeconds);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                int card1Minutes = _firstCardSeconds / 60;
                int promoMinutes = _totalPromoSeconds / 60;

                if (_isVipSession)
                {
                    _maxRedeemMinutes = Math.Min(card1Minutes, promoMinutes);
                    HeaderTitleText.Text = "🎁 ĐỔI GIỜ KHUYẾN MÃI (HỘI VIÊN VIP)";
                    HeaderSubText.Text = "Dùng giờ khuyến mãi tích lũy để cấn trừ thanh toán thời gian đã chơi";
                    Card1Label.Text = "ĐÃ CHƠI";
                }
                else
                {
                    _maxRedeemMinutes = promoMinutes;
                    HeaderTitleText.Text = "🎁 ĐỔI GIỜ KHUYẾN MÃI (TÀI KHOẢN THƯỜNG)";
                    HeaderSubText.Text = "Đổi giờ khuyến mãi cộng dồn trực tiếp vào số dư tài khoản chính";
                    Card1Label.Text = "TK CHÍNH CÒN";
                }

                PlayedTimeText.Text = FormatMinutes(card1Minutes);
                TotalPromoText.Text = FormatMinutes(promoMinutes);
                MaxRedeemText.Text = FormatMinutes(_maxRedeemMinutes);

                RedeemSlider.Minimum = 0;
                RedeemSlider.Maximum = _maxRedeemMinutes;
                RedeemSlider.Value = _maxRedeemMinutes; // Default to maximum redeemable

                if (_maxRedeemMinutes <= 0)
                {
                    ConfirmButton.IsEnabled = false;
                    ConfirmButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105));
                    ShowError(_isVipSession 
                        ? "Bạn chưa có đủ thời gian chơi phiên hiện tại hoặc số dư giờ KM để quy đổi."
                        : "Tài khoản của bạn hiện chưa có số dư giờ khuyến mãi để quy đổi.");
                }

                UpdatePreview((int)RedeemSlider.Value);
            }
            catch (Exception ex)
            {
                ShowError($"Lỗi hiển thị: {ex.Message}");
            }
        }

        private void RedeemSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SelectedRedeemText == null || RemainingPromoText == null) return;
            UpdatePreview((int)e.NewValue);
        }

        private void UpdatePreview(int redeemMinutes)
        {
            SelectedRedeemText.Text = FormatMinutes(redeemMinutes);
            int remainingMinutes = Math.Max(0, (_totalPromoSeconds / 60) - redeemMinutes);
            RemainingPromoText.Text = FormatMinutes(remainingMinutes);
        }

        private static string FormatMinutes(int totalMinutes)
        {
            if (totalMinutes < 60)
            {
                return $"{totalMinutes} phút";
            }
            int hours = totalMinutes / 60;
            int mins = totalMinutes % 60;
            return mins > 0 ? $"{hours}h {mins}p" : $"{hours}h";
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            int redeemMinutes = (int)RedeemSlider.Value;
            if (redeemMinutes <= 0)
            {
                ShowError("Vui lòng chọn số thời gian muốn đổi lớn hơn 0 phút.");
                return;
            }

            try
            {
                ConfirmButton.IsEnabled = false;
                ConfirmButton.Content = "⏳ ĐANG XỬ LÝ...";
                StatusMessageText.Visibility = Visibility.Collapsed;

                var apiUrl = _app.BuildApiUrl($"/members/{_memberId}/redeem-promo-time");
                var payload = new { redeemSeconds = redeemMinutes * 60 };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(apiUrl, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    RedeemedSeconds = redeemMinutes * 60;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    string errorMessage = "Không thể đổi giờ khuyến mãi.";
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        if (doc.RootElement.TryGetProperty("message", out var msgProp))
                        {
                            errorMessage = msgProp.GetString() ?? errorMessage;
                        }
                    }
                    catch
                    {
                        // Ignore JSON parsing error
                    }
                    ShowError(errorMessage);
                }
            }
            catch (Exception ex)
            {
                ShowError($"Lỗi kết nối: {ex.Message}");
            }
            finally
            {
                ConfirmButton.IsEnabled = true;
                ConfirmButton.Content = "✓ XÁC NHẬN ĐỔI";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ShowError(string message)
        {
            StatusMessageText.Text = message;
            StatusMessageText.Visibility = Visibility.Visible;
        }
    }
}
