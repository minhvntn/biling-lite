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

            _availablePoints = result.Loyalty.AvailablePoints;

            // Animate
            await AnimateRace(result.FinishOrder, result.WinnerHorse);

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
        var duration = 5.0; // 5 seconds max
        
        var trackLength = 720d; // Adjusted to span the whole canvas

        if (finishOrder == null || finishOrder.Count < 10)
        {
            // Fallback if backend returned empty array
            finishOrder = Enumerable.Range(0, 10).ToList();
            finishOrder.Remove(fallbackWinnerIndex);
            finishOrder = finishOrder.OrderBy(x => random.Next()).ToList();
            finishOrder.Insert(0, fallbackWinnerIndex);
        }

        var horseTotalSteps = new int[10];
        for (int rank = 0; rank < 10; rank++)
        {
            var horseIdx = finishOrder[rank];
            // Rank 0 finishes in 30 steps, rank 1 in 32... rank 9 in 48
            horseTotalSteps[horseIdx] = 30 + (rank * 2); 
        }

        int maxSteps = 48;
        var sleepMs = (int)(duration * 1000 / maxSteps);

        for (int step = 1; step <= maxSteps; step++)
        {
            foreach (var horse in Horses)
            {
                var hSteps = horseTotalSteps[horse.Index];
                var progress = Math.Min(1.0, (double)step / hSteps);

                horse.Transform.X = progress * trackLength;
                horse.TrailWidth = progress * trackLength;

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
            await Task.Delay(sleepMs);
        }
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
}
