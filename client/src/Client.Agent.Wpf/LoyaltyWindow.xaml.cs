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
    private LuckyEnvelopeSettingsResponse? _luckyEnvelopeSettings;

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

        _app.LoyaltyUpdated += OnLoyaltyUpdated;
        _app.MemberBillingUpdated += OnMemberBillingUpdated;

        Closed += (s, e) =>
        {
            _app.LoyaltyUpdated -= OnLoyaltyUpdated;
            _app.MemberBillingUpdated -= OnMemberBillingUpdated;
        };

        RefreshUi();
        _ = LoadLuckyEnvelopeSettingsAsync();
    }

    private async System.Threading.Tasks.Task LoadLuckyEnvelopeSettingsAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<LuckyEnvelopeSettingsResponse>(
                _app.BuildApiUrl($"/members/loyalty/lucky-envelope/settings?memberId={_activeSession.MemberId}"),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (response != null)
            {
                _luckyEnvelopeSettings = response;
                Dispatcher.Invoke(RefreshUi);
            }
        }
        catch
        {
            // Silently ignore
        }
    }

    private void OnLoyaltyUpdated(MemberLoyaltyItem loyalty)
    {
        Dispatcher.Invoke(() =>
        {
            _loyaltyResponse.Loyalty = loyalty;
            RefreshUi();
        });
    }

    private void OnMemberBillingUpdated(MemberLoginItem member)
    {
        Dispatcher.Invoke(() =>
        {
            _loyaltyResponse.Member = member;
            RefreshUi();
        });
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
        ReferralCodeText.Text = string.IsNullOrWhiteSpace(_activeSession.ReferralCode) ? "Mã giới thiệu: ---" : $"Mã giới thiệu: {_activeSession.ReferralCode}";


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
        var pointsToMinutes = Math.Max(1, _settings?.PointsToMinutes ?? 5);
        RedeemRateText.Text = $"Tỷ lệ: 1 điểm = {pointsToMinutes} phút chơi.";
        RedeemButton.IsEnabled = currentLoyalty.AvailablePoints > 0;
        RedeemAllButton.IsEnabled = currentLoyalty.AvailablePoints > 0;

        // Horse Race UI
        OpenHorseRaceButton.IsEnabled = _loyaltyResponse.CanPlayHorseRace;
        if (!_loyaltyResponse.CanPlayHorseRace)
        {
            var minRankName = string.IsNullOrWhiteSpace(_settings?.HorseRaceMinRankName) ? "quy định" : _settings.HorseRaceMinRankName;
            HorseRaceErrorText.Text = $"* Yêu cầu đạt hạng {minRankName} trở lên để mở khóa.";
            HorseRaceErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            HorseRaceErrorText.Visibility = Visibility.Collapsed;
        }

        // Lucky Envelope UI
        if (_luckyEnvelopeSettings == null || !_luckyEnvelopeSettings.Enabled)
        {
            LuckyEnvelopeTabItem.Visibility = Visibility.Collapsed;
        }
        else
        {
            LuckyEnvelopeTabItem.Visibility = Visibility.Visible;
            LuckyEnvelopeAvailablePointsText.Text = currentLoyalty.AvailablePoints.ToString("N0");
            LuckyEnvelopeCostText.Text = $"{_luckyEnvelopeSettings.PointCost} điểm / bao";
            LuckyEnvelopeRangeText.Text = $"Phần thưởng: từ {_luckyEnvelopeSettings.MinMinutes} đến {_luckyEnvelopeSettings.MaxMinutes} phút chơi.";

            if (_luckyEnvelopeSettings.MaxClaims > 0)
            {
                LuckyEnvelopeStatusHintText.Text = $"Bạn đã mở {_luckyEnvelopeSettings.ClaimedCount}/{_luckyEnvelopeSettings.MaxClaims} lần.";
            }
            else
            {
                LuckyEnvelopeStatusHintText.Text = _luckyEnvelopeSettings.ClaimedCount > 0
                    ? $"Bạn đã mở {_luckyEnvelopeSettings.ClaimedCount} lần."
                    : "Không giới hạn số lần mở.";
            }

            var canOpen = _luckyEnvelopeSettings.IsActive && _luckyEnvelopeSettings.CanClaim && currentLoyalty.AvailablePoints >= _luckyEnvelopeSettings.PointCost;
            OpenLuckyEnvelopeButton.IsEnabled = canOpen;

            if (!_luckyEnvelopeSettings.IsActive)
            {
                LuckyEnvelopeErrorText.Text = _luckyEnvelopeSettings.Reason ?? "Sự kiện Lì Xì Tết đang tạm đóng.";
                LuckyEnvelopeErrorText.Visibility = Visibility.Visible;
            }
            else if (!_luckyEnvelopeSettings.CanClaim)
            {
                LuckyEnvelopeErrorText.Text = _luckyEnvelopeSettings.Reason ?? "Bạn đã hết lượt mở lì xì.";
                LuckyEnvelopeErrorText.Visibility = Visibility.Visible;
            }
            else if (currentLoyalty.AvailablePoints < _luckyEnvelopeSettings.PointCost)
            {
                LuckyEnvelopeErrorText.Text = $"* Bạn cần thêm {_luckyEnvelopeSettings.PointCost - currentLoyalty.AvailablePoints} điểm để mở bao lì xì.";
                LuckyEnvelopeErrorText.Visibility = Visibility.Visible;
            }
            else
            {
                LuckyEnvelopeErrorText.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenSpinButton_Click(object sender, RoutedEventArgs e)
    {
        _app.ShowLuckySpinDialog(_activeSession, _settings, _loyaltyResponse);
        RefreshUi();
    }

    private void OpenHorseRaceButton_Click(object sender, RoutedEventArgs e)
    {
        var loyaltyAfter = _app.ShowHorseRaceMiniDialog(_activeSession, _loyaltyResponse.Loyalty.AvailablePoints);
        if (loyaltyAfter != null)
        {
            _loyaltyResponse.Loyalty = loyaltyAfter;
        }
        RefreshUi();
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

        var pointsToMinutes = Math.Max(1, _settings?.PointsToMinutes ?? 5);
        var totalMinutes = redeemPoints * pointsToMinutes;
        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn đổi {redeemPoints} điểm tích lũy để nhận +{totalMinutes} phút chơi miễn phí?",
            "Xác nhận đổi điểm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

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

        var pointsToMinutes = Math.Max(1, _settings?.PointsToMinutes ?? 5);
        var totalMinutes = redeemPoints * pointsToMinutes;
        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn đổi toàn bộ {redeemPoints} điểm tích lũy để nhận +{totalMinutes} phút chơi miễn phí?",
            "Xác nhận đổi tất cả",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

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

            var pointsToMinutes = Math.Max(1, _settings?.PointsToMinutes ?? 5);
            var totalMinutes = payload?.GrantedMinutes > 0 ? payload.GrantedMinutes : (redeemPoints * pointsToMinutes);
            MessageBox.Show(
                $"🎉 Đổi điểm thành công!\n\n- Đã trừ: {redeemPoints} điểm\n- Đã cộng: +{totalMinutes} phút chơi miễn phí vào tài khoản.",
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

    private async void OpenLuckyEnvelopeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_luckyEnvelopeSettings == null)
            return;

        LuckyEnvelopeErrorText.Visibility = Visibility.Collapsed;
        var pointCost = _luckyEnvelopeSettings.PointCost;

        if (_loyaltyResponse.Loyalty.AvailablePoints < pointCost)
        {
            LuckyEnvelopeErrorText.Text = $"Không đủ điểm tích lũy (cần {pointCost} điểm).";
            LuckyEnvelopeErrorText.Visibility = Visibility.Visible;
            return;
        }

        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn dùng {pointCost} điểm tích lũy để mở phong bao Lì Xì May Mắn?",
            "Xác nhận Mở Lì Xì Tết",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        OpenLuckyEnvelopeButton.IsEnabled = false;

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                _app.BuildApiUrl($"/members/{_activeSession.MemberId}/loyalty/lucky-envelope/open"),
                new { createdBy = "client.loyalty.lucky_envelope" });

            if (!response.IsSuccessStatusCode)
            {
                var message = await _app.ReadErrorMessageAsync(response);
                LuckyEnvelopeErrorText.Text = string.IsNullOrWhiteSpace(message)
                    ? $"Mở lì xì thất bại ({(int)response.StatusCode})"
                    : message;
                LuckyEnvelopeErrorText.Visibility = Visibility.Visible;
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenLuckyEnvelopeResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload != null)
            {
                if (payload.Member != null)
                {
                    _app.SynchronizeMemberBillingFromServerProxy(payload.Member);
                    _app.UpdateLastCommandProxy($"Lì xì +{payload.WonMinutes}p @ {DateTime.Now:HH:mm:ss}");
                }

                if (payload.Loyalty != null)
                {
                    _loyaltyResponse.Loyalty = payload.Loyalty;
                    _app.SynchronizeLoyaltyFromServerProxy(payload.Loyalty);
                }

                _luckyEnvelopeSettings.ClaimedCount++;
                if (_luckyEnvelopeSettings.MaxClaims > 0 && _luckyEnvelopeSettings.ClaimedCount >= _luckyEnvelopeSettings.MaxClaims)
                {
                    _luckyEnvelopeSettings.CanClaim = false;
                }

                RefreshUi();

                var resultWindow = new LuckyEnvelopeResultWindow(payload.WonMinutes, payload.BlessingMessage)
                {
                    Owner = this
                };
                resultWindow.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            LuckyEnvelopeErrorText.Text = $"Lỗi khi mở lì xì: {ex.Message}";
            LuckyEnvelopeErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            RefreshUi();
        }
    }
}
