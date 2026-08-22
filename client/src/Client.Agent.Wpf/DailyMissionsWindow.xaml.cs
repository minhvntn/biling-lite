using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Client.Agent.Wpf;

public partial class DailyMissionsWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ActiveMemberSession _activeSession;
    private readonly Func<string, string> _buildApiUrl;

    public DailyMissionsWindow(
        HttpClient httpClient,
        ActiveMemberSession activeSession,
        Func<string, string> buildApiUrl,
        DailyMissionsResponse? initialResponse = null)
    {
        InitializeComponent();

        _httpClient = httpClient;
        _activeSession = activeSession;
        _buildApiUrl = buildApiUrl;

        HeaderTitleText.Text = $"Nhiệm Vụ Hàng Ngày - {_activeSession.Username}";

        HeaderBorder.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };

        if (initialResponse != null)
        {
            DisplayMissions(initialResponse);
        }
        else
        {
            Loaded += async (s, e) => await LoadMissionsAsync();
        }
    }

    private void DisplayMissions(DailyMissionsResponse response)
    {
        MissionsListControl.ItemsSource = response.Missions;
        SummaryPointsText.Text = $"Hôm nay bạn đã nhận: {response.TotalEarnedToday} điểm thưởng";
    }

    private async Task LoadMissionsAsync()
    {
        try
        {
            var url = _buildApiUrl($"/members/{_activeSession.MemberId}/daily-missions");
            var response = await _httpClient.GetFromJsonAsync<DailyMissionsResponse>(url, JsonOptions);
            if (response != null)
            {
                DisplayMissions(response);
            }
        }
        catch (Exception ex)
        {
            SummaryPointsText.Text = "Không thể tải danh sách nhiệm vụ.";
            MessageBox.Show(
                $"Lỗi tải nhiệm vụ: {ex.Message}",
                "Nhiệm Vụ Hàng Ngày",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void ClaimButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string missionId)
        {
            btn.IsEnabled = false;
            try
            {
                var url = _buildApiUrl($"/members/{_activeSession.MemberId}/daily-missions/{missionId}/claim");
                using var response = await _httpClient.PostAsJsonAsync(url, new { }, JsonOptions);
                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        "🎉 Chúc mừng! Bạn đã nhận thành công thưởng nhiệm vụ!",
                        "Nhiệm Vụ Hàng Ngày",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await LoadMissionsAsync();
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    MessageBox.Show(
                        $"Không thể nhận thưởng: {errorText}",
                        "Nhiệm Vụ Hàng Ngày",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    btn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Lỗi kết nối: {ex.Message}",
                    "Nhiệm Vụ Hàng Ngày",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                btn.IsEnabled = true;
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
