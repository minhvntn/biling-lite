using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Client.Agent.Wpf;

public partial class LoyaltyWindow : Window
{
    private readonly App _app;
    private readonly HttpClient _httpClient;
    private readonly ActiveMemberSession _activeSession;
    private readonly LoyaltySettingsResponse _settings;
    private MemberLoyaltyResponse _loyaltyResponse;

    public LoyaltyWindow(
        App app,
        HttpClient httpClient,
        ActiveMemberSession activeSession,
        LoyaltySettingsResponse settings,
        MemberLoyaltyResponse loyaltyResponse)
    {
        InitializeComponent();

        _app = app;
        _httpClient = httpClient;
        _activeSession = activeSession;
        _settings = settings;
        _loyaltyResponse = loyaltyResponse;

        HeaderTitleText.Text = $"Điểm tích lũy - {activeSession.Username}";
        MemberNameText.Text = $"Hội viên: {activeSession.Username}";

        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        RefreshUi();
    }

    private void RefreshUi()
    {
        var member = _loyaltyResponse.Member;
        var currentLoyalty = _loyaltyResponse.Loyalty;
        var dailyCheckin = _loyaltyResponse.DailyCheckin;

        var tSecs = (int)Math.Floor(_settings.MinutesPerPoint * 60.0);
        var tStr = tSecs % 60 == 0 ? $"{tSecs / 60} phút" : $"{tSecs / 60} phút {tSecs % 60} giây";
        var pSecs = (int)Math.Floor(currentLoyalty.ProgressMinutes * 60.0);
        var pStr = $"{pSecs / 60:00}:{pSecs % 60:00}";

        BalanceText.Text = $"Số dư: {member.Balance:N0} VND";
        PointsText.Text = $"Điểm: {currentLoyalty.AvailablePoints:N0}";
        ProgressText.Text = $"Tích lũy: {pStr} / {tStr}";

        SpinPointsText.Text = currentLoyalty.AvailablePoints.ToString("N0");
        OpenSpinButton.IsEnabled = currentLoyalty.AvailablePoints >= 5;

        // Daily Checkin UI
        var pointsPerCheckin = Math.Max(1, dailyCheckin?.PointsPerCheckin ?? 1);
        var bonusEveryDays = Math.Max(1, dailyCheckin?.BonusEveryDays ?? 7);
        var bonusPoints = Math.Max(0, dailyCheckin?.BonusPoints ?? 0);

        if (dailyCheckin?.CheckedInToday == true)
        {
            var checkedInText = string.Empty;
            if (DateTimeOffset.TryParse(dailyCheckin.CheckedInAt, out var checkedInAt))
            {
                checkedInText = $" lúc {checkedInAt.ToLocalTime():HH:mm}";
            }

            var bonusTodayText = dailyCheckin.BonusReadyToday && bonusPoints > 0
                ? $" Đã nhận bonus +{bonusPoints} điểm."
                : string.Empty;
            CheckinStatusText.Text = $"Hôm nay bạn đã điểm danh{checkedInText}. Streak: {dailyCheckin.CurrentStreakDays} ngày.{bonusTodayText}";
            CheckinStatusText.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74)); // Green
            DailyCheckinButton.IsEnabled = false;
        }
        else
        {
            var streakDays = Math.Max(0, dailyCheckin?.CurrentStreakDays ?? 0);
            if (streakDays > 0)
            {
                var daysToBonus = Math.Max(0, dailyCheckin?.DaysUntilNextBonus ?? 0);
                var bonusHint = bonusPoints > 0
                    ? $" Còn {daysToBonus} ngày để nhận +{bonusPoints} điểm bonus."
                    : string.Empty;
                CheckinStatusText.Text = $"Streak hiện tại: {streakDays} ngày.{bonusHint}";
            }
            else
            {
                CheckinStatusText.Text = "Mỗi ngày điểm danh 1 lần để nhận điểm.";
            }
            CheckinStatusText.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)); // Gray
            DailyCheckinButton.IsEnabled = true;
        }

        // Redeem UI
        RedeemButton.IsEnabled = currentLoyalty.AvailablePoints > 0;
        RedeemAllButton.IsEnabled = currentLoyalty.AvailablePoints > 0;

        // Horse Race UI
        OpenHorseRaceButton.IsEnabled = _loyaltyResponse.CanPlayHorseRace;
        if (!_loyaltyResponse.CanPlayHorseRace)
        {
            var minRankName = string.IsNullOrWhiteSpace(_settings.HorseRaceMinRankName) ? "quy định" : _settings.HorseRaceMinRankName;
            HorseRaceErrorText.Text = $"* Yêu cầu đạt hạng {minRankName} trở lên để mở khóa.";
            HorseRaceErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            HorseRaceErrorText.Visibility = Visibility.Collapsed;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenSpinButton_Click(object sender, RoutedEventArgs e)
    {
        _app.ShowLuckySpinDialog(_activeSession, _settings, _loyaltyResponse);
    }

    private void OpenHorseRaceButton_Click(object sender, RoutedEventArgs e)
    {
        _app.ShowHorseRaceMiniDialog(_activeSession, _loyaltyResponse.Loyalty.AvailablePoints);
    }

    private async void DailyCheckinButton_Click(object sender, RoutedEventArgs e)
    {
        CheckinErrorText.Text = string.Empty;
        DailyCheckinButton.IsEnabled = false;

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                _app.BuildApiUrl($"/members/{_activeSession.MemberId}/loyalty/daily-checkin"),
                new { createdBy = "client.loyalty.checkin" });

            if (!response.IsSuccessStatusCode)
            {
                var message = await _app.ReadErrorMessageAsync(response);
                CheckinErrorText.Text = string.IsNullOrWhiteSpace(message)
                    ? $"Điểm danh thất bại ({(int)response.StatusCode})"
                    : message;
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<MemberLoyaltyDailyCheckinResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload?.Member is not null)
            {
                _app.SynchronizeMemberBillingFromServerProxy(payload.Member);
                _app.UpdateLastCommandProxy($"Điểm danh +{payload.GainedPoints} @ {DateTime.Now:HH:mm:ss}");
            }

            if (payload?.Loyalty is not null)
            {
                _loyaltyResponse.Loyalty = payload.Loyalty;
                _app.SynchronizeLoyaltyFromServerProxy(payload.Loyalty);
            }

            if (payload?.DailyCheckin is not null)
            {
                _loyaltyResponse.DailyCheckin = payload.DailyCheckin;
            }

            RefreshUi();

            MessageBox.Show(
                (payload?.BonusPoints ?? 0) > 0
                    ? $"Điểm danh thành công: +{Math.Max(1, payload?.GainedPoints ?? 1)} điểm (gồm bonus +{payload?.BonusPoints ?? 0})."
                    : $"Điểm danh thành công: +{Math.Max(1, payload?.GainedPoints ?? 1)} điểm.",
                "Điểm tích lũy",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            CheckinErrorText.Text = $"Lỗi điểm danh: {ex.Message}";
        }
        finally
        {
            RefreshUi();
        }
    }

    private async void RedeemButton_Click(object sender, RoutedEventArgs e)
    {
        RedeemErrorText.Text = string.Empty;
        if (!int.TryParse(RedeemPointsBox.Text.Trim(), out var redeemPoints) || redeemPoints < 1)
        {
            RedeemErrorText.Text = "Số điểm đổi phải là số nguyên >= 1.";
            return;
        }

        if (redeemPoints > _loyaltyResponse.Loyalty.AvailablePoints)
        {
            RedeemErrorText.Text = "Không đủ điểm tích lũy.";
            return;
        }

        await ExecuteRedeemAsync(redeemPoints);
    }

    private async void RedeemAllButton_Click(object sender, RoutedEventArgs e)
    {
        RedeemErrorText.Text = string.Empty;
        var redeemPoints = _loyaltyResponse.Loyalty.AvailablePoints;
        if (redeemPoints < 1)
        {
            RedeemErrorText.Text = "Bạn chưa có điểm nào để đổi.";
            return;
        }

        await ExecuteRedeemAsync(redeemPoints);
    }

    private async System.Threading.Tasks.Task ExecuteRedeemAsync(int redeemPoints)
    {
        RedeemButton.IsEnabled = false;
        RedeemAllButton.IsEnabled = false;

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                _app.BuildApiUrl($"/members/{_activeSession.MemberId}/loyalty/redeem"),
                new
                {
                    points = redeemPoints,
                    createdBy = "client.loyalty.redeem"
                });

            if (!response.IsSuccessStatusCode)
            {
                var message = await _app.ReadErrorMessageAsync(response);
                RedeemErrorText.Text = string.IsNullOrWhiteSpace(message)
                    ? $"Đổi điểm thất bại ({(int)response.StatusCode})"
                    : message;
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<MemberLoyaltyRedeemResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload?.Member is not null)
            {
                _app.SynchronizeMemberBillingFromServerProxy(payload.Member);
                _app.UpdateLastCommandProxy($"Đổi {redeemPoints} điểm @ {DateTime.Now:HH:mm:ss}");
            }

            if (payload?.Loyalty is not null)
            {
                _loyaltyResponse.Loyalty = payload.Loyalty;
                _app.SynchronizeLoyaltyFromServerProxy(payload.Loyalty);
            }

            RedeemPointsBox.Text = string.Empty;
            RefreshUi();

            MessageBox.Show(
                $"Đổi điểm thành công: +{redeemPoints} phút chơi.",
                "Điểm tích lũy",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            RedeemErrorText.Text = $"Lỗi đổi điểm: {ex.Message}";
        }
        finally
        {
            RefreshUi();
        }
    }
}
