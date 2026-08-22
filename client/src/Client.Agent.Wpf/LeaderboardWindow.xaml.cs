using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Client.Agent.Wpf;

public partial class LeaderboardWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ActiveMemberSession _activeSession;
    private readonly Func<string, string> _buildApiUrl;
    private string _currentPeriod = "all";

    public LeaderboardWindow(
        HttpClient httpClient,
        ActiveMemberSession activeSession,
        Func<string, string> buildApiUrl,
        LeaderboardResponse? initialResponse = null)
    {
        InitializeComponent();

        _httpClient = httpClient;
        _activeSession = activeSession;
        _buildApiUrl = buildApiUrl;

        HeaderTitleText.Text = $"Bảng Xếp Hạng - {_activeSession.Username}";

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
            DisplayLeaderboard(initialResponse);
        }
        else
        {
            Loaded += async (s, e) => await LoadLeaderboardAsync();
        }
    }

    private void DisplayLeaderboard(LeaderboardResponse response)
    {
        PointsListControl.ItemsSource = response.TopPoints;
        PlaytimeListControl.ItemsSource = response.TopPlaytime;

        if (response.CurrentUserRank != null)
        {
            var pRank = response.CurrentUserRank.PointsRank > 0 ? $"#{response.CurrentUserRank.PointsRank}" : "Chưa có";
            var tRank = response.CurrentUserRank.PlaytimeRank > 0 ? $"#{response.CurrentUserRank.PlaytimeRank}" : "Chưa có";
            MyRankText.Text = $"Thứ hạng của bạn: Điểm {pRank} ({response.CurrentUserRank.PointsValue:N0} điểm) | Giờ chơi {tRank} ({response.CurrentUserRank.PlaytimeValue}h)";
            MyStatsText.Text = "Hãy tích cực chơi game và sử dụng dịch vụ để leo lên Top 15 nhé!";
        }
        else
        {
            MyRankText.Text = "Thứ hạng của bạn: Chưa xếp hạng";
        }
    }

    private async Task LoadLeaderboardAsync()
    {
        try
        {
            UpdateButtonStyles();

            var url = _buildApiUrl($"/members/leaderboard?period={_currentPeriod}&memberId={_activeSession.MemberId}");
            var response = await _httpClient.GetFromJsonAsync<LeaderboardResponse>(url, JsonOptions);
            if (response != null)
            {
                DisplayLeaderboard(response);
            }
        }
        catch (Exception ex)
        {
            MyRankText.Text = "Không thể tải bảng xếp hạng.";
            MyStatsText.Text = ex.Message;
        }
    }

    private void UpdateButtonStyles()
    {
        SetButtonStyle(AllTimeButton, _currentPeriod == "all");
        SetButtonStyle(MonthButton, _currentPeriod == "month");
        SetButtonStyle(WeekButton, _currentPeriod == "week");
    }

    private void SetButtonStyle(Button button, bool isSelected)
    {
        if (isSelected)
        {
            button.Background = (Brush)FindResource("PurpleGradient");
            button.Foreground = Brushes.White;
        }
        else
        {
            button.Background = Brushes.Transparent;
            button.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // #64748B
        }
    }

    private async void PeriodButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string period && period != _currentPeriod)
        {
            _currentPeriod = period;
            await LoadLeaderboardAsync();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
