using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Client.Agent.Wpf;

public partial class HorseRaceWindow : Window
{
    private readonly HttpClient _httpClient;
    private readonly ActiveMemberSession _session;
    private readonly string _apiBaseUrl;
    private int _availablePoints;
    
    public ObservableCollection<HorseViewModel> Horses { get; set; } = new();
    public MemberLoyaltyItem? LoyaltyAfterRace { get; private set; }

    public HorseRaceWindow(HttpClient httpClient, ActiveMemberSession session, string apiBaseUrl, int availablePoints)
    {
        InitializeComponent();
        _httpClient = httpClient;
        _session = session;
        _apiBaseUrl = apiBaseUrl;
        _availablePoints = availablePoints;

        DataContext = this;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AvailablePointsText.Text = _availablePoints.ToString("N0");

        var horseNames = new[] 
        { 
            "Sấm Chớp", "Bão Đêm", "Hỏa Tiễn", "Lốc Xanh", "Thần Tốc", 
            "Mặt Trời", "Gió Bụi", "Tia Chớp", "Bóng Đêm", "Vua Lì Đòn" 
        };

        var colors = new[]
        {
            "#EF4444", "#3B82F6", "#F59E0B", "#10B981", "#8B5CF6",
            "#F43F5E", "#EAB308", "#06B6D4", "#6366F1", "#14B8A6"
        };

        var icons = new[]
        {
            "🐎", "🐴", "🦄", "🦓", "🦌",
            "🐂", "🐃", "🐄", "🐅", "🐆"
        };

        for (int i = 0; i < 10; i++)
        {
            Horses.Add(new HorseViewModel
            {
                Index = i,
                Number = $"#{i + 1}",
                Name = horseNames[i],
                ColorHex = colors[i],
                ColorValue = (Color)ColorConverter.ConvertFromString(colors[i]),
                Icon = icons[i],
                Transform = new TranslateTransform()
            });
        }

        TracksItemsControl.ItemsSource = Horses;
        HorseSelectionListBox.ItemsSource = Horses;
        HorseSelectionListBox.SelectedIndex = 0;
    }

    private async void StartRaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (HorseSelectionListBox.SelectedItem is not HorseViewModel selectedHorse)
        {
            MessageBox.Show("Vui lòng chọn 1 con ngựa để đặt cược.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        var betPoints = (int)BetPointsSlider.Value;
        if (betPoints < 1)
        {
            MessageBox.Show("Mức cược không hợp lệ.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (betPoints > _availablePoints)
        {
            MessageBox.Show($"Bạn chỉ có {_availablePoints} điểm. Không đủ điểm cược.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StartRaceButton.IsEnabled = false;
        BetPointsSlider.IsEnabled = false;
        HorseSelectionListBox.IsEnabled = false;

        // Reset positions
        foreach (var horse in Horses)
        {
            horse.Transform.X = 0;
            horse.TrailWidth = 0;
            horse.DistanceText = "0m";
        }

        try
        {
            var request = new MemberLoyaltyHorseRaceRequest
            {
                BetPoints = betPoints,
                SelectedHorse = selectedHorse.Index
            };

            using var response = await _httpClient.PostAsJsonAsync($"{_apiBaseUrl}/members/{_session.MemberId}/loyalty/horse-race", request);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                MessageBox.Show($"Lỗi gọi API: {response.StatusCode}\n{error}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetUI();
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<MemberLoyaltyHorseRaceResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result == null)
            {
                MessageBox.Show("Lỗi phân tích kết quả trả về.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetUI();
                return;
            }

            // Deduct from UI immediately to show bet
            AvailablePointsText.Text = (_availablePoints - betPoints).ToString("N0");

            // Animate
            await AnimateRace(result.FinishOrder, result.WinnerHorse);

            // Update to final points from server
            _availablePoints = result.Loyalty.AvailablePoints;
            LoyaltyAfterRace = result.Loyalty;

            // Show Result
            ShowResultOverlay(result.IsWin, result.Rank, result.WonPoints, selectedHorse.Name, result.WinnerHorse);
            AvailablePointsText.Text = _availablePoints.ToString("N0");
            
            // Notify MainWindow or parent to update their loyalty state if needed
            // Assuming App handles that or we don't care since we refresh on next open
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            ResetUI();
        }
    }

    private void ResetUI()
    {
        StartRaceButton.IsEnabled = true;
        BetPointsSlider.IsEnabled = true;
        HorseSelectionListBox.IsEnabled = true;
    }

    private async Task AnimateRace(List<int> finishOrder, int fallbackWinnerIndex)
    {
        var random = new Random();
        var duration = 4.0; // 4 seconds base
        
        var trackLength = 720d;

        if (finishOrder == null || finishOrder.Count < 10)
        {
            finishOrder = Enumerable.Range(0, 10).ToList();
            finishOrder.Remove(fallbackWinnerIndex);
            finishOrder = finishOrder.OrderBy(x => random.Next()).ToList();
            finishOrder.Insert(0, fallbackWinnerIndex);
        }

        bool hasPhotoFinish = random.NextDouble() < 0.6; // 60% chance for close finish
        
        var horseTotalSteps = new int[10];
        horseTotalSteps[finishOrder[0]] = 200; // 1st place finishes at step 200
        
        int stepGap = hasPhotoFinish ? 4 : random.Next(12, 25);
        horseTotalSteps[finishOrder[1]] = 200 + stepGap; // 2nd place
        
        for (int rank = 2; rank < 10; rank++)
        {
            var horseIdx = finishOrder[rank];
            horseTotalSteps[horseIdx] = horseTotalSteps[finishOrder[rank - 1]] + random.Next(8, 16); 
        }

        int maxSteps = horseTotalSteps[finishOrder[9]];
        var normalSleepMs = (int)(duration * 1000 / 200); // base speed on 1st place

        int boostRank = random.Next(4, 9); // A losing horse
        int boostHorseIdx = finishOrder[boostRank];
        int boostStartStep = 75;
        int boostEndStep = 140;

        for (int step = 1; step <= maxSteps; step++)
        {
            int currentSleepMs = normalSleepMs;
            bool isPhotoFinishActive = false;

            foreach (var horse in Horses)
            {
                var hSteps = horseTotalSteps[horse.Index];
                
                double p = (double)step / hSteps;
                
                // Boost acceleration logic
                if (horse.Index == boostHorseIdx)
                {
                    if (step >= boostStartStep && step <= boostEndStep)
                    {
                        horse.IsBoosting = true;
                        BoostOverlayText.Text = $"⚡ {horse.Name.ToUpper()} BOOST! ⚡";
                        BoostOverlay.Visibility = Visibility.Visible;
                        
                        // Artificial speed up
                        p += 0.08 * Math.Sin((double)(step - boostStartStep) / (boostEndStep - boostStartStep) * Math.PI);
                    }
                    else
                    {
                        horse.IsBoosting = false;
                        if (step == boostEndStep + 1)
                        {
                            BoostOverlay.Visibility = Visibility.Collapsed;
                        }
                    }
                }

                var progress = Math.Min(1.0, p);

                // Photo Finish Trigger
                if (horse.Index == finishOrder[0] && progress > 0.85 && progress < 1.0 && stepGap <= 6)
                {
                    isPhotoFinishActive = true;
                }

                if (horse.Index == finishOrder[0] && progress >= 1.0 && PhotoFinishOverlay.Visibility == Visibility.Visible)
                {
                    // Winner just crossed line, flash!
                    PhotoFinishOverlay.Visibility = Visibility.Collapsed;
                    TriggerFlashEffect();
                }

                // Make sure we never move backwards due to math
                var newX = progress * trackLength;
                if (newX > horse.Transform.X || step == 1)
                {
                    horse.Transform.X = newX;
                    horse.TrailWidth = newX;
                }

                // Add wobble effect if still running
                if (progress < 1.0)
                {
                    // Wiggles up and down by 2 pixels based on progress
                    horse.Transform.Y = Math.Sin(progress * 40 * Math.PI) * 2;
                }
                else
                {
                    horse.Transform.Y = 0;
                }

                if (progress >= 1.0)
                {
                    int rank = finishOrder.IndexOf(horse.Index);
                    horse.DistanceText = $"Hạng {rank + 1}";
                }
                else
                {
                    horse.DistanceText = $"{(int)(progress * 100)}m";
                }
            }

            if (isPhotoFinishActive)
            {
                PhotoFinishOverlay.Visibility = Visibility.Visible;
                currentSleepMs = normalSleepMs * 4; // Slow motion!
            }

            // Screen Shake on Boost
            bool anyBoosting = Horses.Any(h => h.IsBoosting);
            if (anyBoosting)
            {
                MainContainerTransform.X = random.Next(-2, 3);
                MainContainerTransform.Y = random.Next(-2, 3);
            }
            else
            {
                MainContainerTransform.X = 0;
                MainContainerTransform.Y = 0;
            }

            await Task.Delay(currentSleepMs);
        }
        
        BoostOverlay.Visibility = Visibility.Collapsed;
        PhotoFinishOverlay.Visibility = Visibility.Collapsed;
        MainContainerTransform.X = 0;
        MainContainerTransform.Y = 0;
    }

    private void TriggerFlashEffect()
    {
        FlashOverlay.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromSeconds(0.5),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (s, e) => FlashOverlay.Visibility = Visibility.Collapsed;
        FlashOverlay.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void ShowResultOverlay(bool isWin, int rank, int wonPoints, string selectedHorseName, int fallbackWinnerIndex)
    {
        ResultOverlay.Visibility = Visibility.Visible;
        var rankText = (rank + 1) == 1 ? "về Nhất" : (rank + 1) == 2 ? "về Nhì" : (rank + 1) == 3 ? "về Ba" : $"về hạng {rank + 1}";

        if (isWin)
        {
            ResultTitleText.Text = "CHÚC MỪNG!";
            ResultTitleText.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Emerald 500
            if (ResultTitleText.Effect is DropShadowEffect shadow)
            {
                shadow.Color = Color.FromRgb(16, 185, 129);
            }
            ResultMessageText.Text = $"Ngựa {selectedHorseName} đã {rankText}.\nBạn trúng {wonPoints:N0} điểm!";
        }
        else
        {
            ResultTitleText.Text = "RẤT TIẾC!";
            ResultTitleText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red 500
            if (ResultTitleText.Effect is DropShadowEffect shadow)
            {
                shadow.Color = Color.FromRgb(239, 68, 68);
            }
            if (rank == -1 || rank >= 10)
            {
                ResultMessageText.Text = $"Ngựa bạn chọn đã không trúng thưởng.\nNgựa về nhất là {Horses[fallbackWinnerIndex].Name}!";
            }
            else
            {
                ResultMessageText.Text = $"Ngựa {selectedHorseName} đã {rankText}.\nChúc bạn may mắn lần sau!";
            }
        }
    }

    private void CloseOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        ResultOverlay.Visibility = Visibility.Collapsed;
        ResetUI();
    }
}

public class HorseViewModel : INotifyPropertyChanged
{
    public int Index { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public Color ColorValue { get; set; }
    public string DisplayName => $"{Number} - {Name}";
    public string Icon { get; set; } = "🐎";

    private string _distanceText = "0m";
    public string DistanceText
    {
        get => _distanceText;
        set
        {
            _distanceText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistanceText)));
        }
    }

    public TranslateTransform Transform { get; set; } = new();

    private double _trailWidth = 0;
    public double TrailWidth
    {
        get => _trailWidth;
        set
        {
            _trailWidth = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrailWidth)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    
    private bool _isBoosting;
    public bool IsBoosting
    {
        get => _isBoosting;
        set
        {
            _isBoosting = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBoosting)));
        }
    }
}
