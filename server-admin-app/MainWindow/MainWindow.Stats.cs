using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private string _currentStatsPeriod = "week";

    private async Task RefreshStatisticsAsync()
    {
        await LoadStatisticsDataAsync(_currentStatsPeriod);
    }

    private async Task LoadStatisticsDataAsync(string period)
    {
        _currentStatsPeriod = period;
        
        Dispatcher.Invoke(() =>
        {
            StatsWeekButton.Background = period == "week" ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            StatsWeekButton.Foreground = period == "week" ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));
            
            StatsMonthButton.Background = period == "month" ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            StatsMonthButton.Foreground = period == "month" ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));
            
            StatsYearButton.Background = period == "year" ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            StatsYearButton.Foreground = period == "year" ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));
        });

        try
        {
            var response = await _httpClient.GetFromJsonAsync<DashboardStatsResponse>(
                BuildApiUrl($"/reports/dashboard?period={period}"),
                JsonOptions());

            if (response is null) return;

            Dispatcher.Invoke(() =>
            {
                PlaytimeRevenueTextBlock.Text = $"{response.PlaytimeRevenue:N0} VND";
                PlaytimeGrowthTextBlock.Text = response.PlaytimeGrowth;
                
                ServiceRevenueTextBlock.Text = $"{response.ServiceRevenue:N0} VND";
                ServiceGrowthTextBlock.Text = response.ServiceGrowth;
                
                TotalRevenueTextBlock.Text = $"{response.TotalRevenue:N0} VND";
                TotalGrowthTextBlock.Text = response.TotalGrowth;
                
                TotalPlayHoursTextBlock.Text = $"{response.TotalPlayHours:N0} giờ";
                PlayHoursGrowthTextBlock.Text = response.PlayhoursGrowth;

                var memberRows = response.TopMembers.Select((m, idx) => {
                    string rankBg = "#F3F4F6";
                    string rankFg = "#374151";
                    string barColor = "#3B82F6";
                    if (idx == 0) { rankBg = "#FEF3C7"; rankFg = "#D97706"; barColor = "#EF4444"; }
                    else if (idx == 1) { rankBg = "#E0E7FF"; rankFg = "#4F46E5"; barColor = "#F59E0B"; }
                    else if (idx == 2) { rankBg = "#ECFDF5"; rankFg = "#059669"; barColor = "#10B981"; }
                    
                    return new TopMemberRowViewModel
                    {
                        RankNumber = (idx + 1).ToString(),
                        RankBackground = rankBg,
                        RankForeground = rankFg,
                        Username = m.Username,
                        ProgressValue = m.Progress,
                        BarColor = barColor,
                        PlayHoursText = $"{m.PlayHours} giờ"
                    };
                }).ToList();

                TopMembersItemsControl.ItemsSource = memberRows;

                RevenueBarsContainer.Children.Clear();
                
                decimal maxVal = 10000;
                foreach (var day in response.DailyData)
                {
                    decimal tot = day.PlaytimeRevenue + day.ServiceRevenue;
                    if (tot > maxVal) maxVal = tot;
                }

                for (int i = 0; i < response.DailyData.Count; i++)
                {
                    var data = response.DailyData[i];
                    decimal totalVal = data.PlaytimeRevenue + data.ServiceRevenue;
                    double barHeight = maxVal > 0 ? (double)(totalVal / maxVal) * 180.0 : 0.0;
                    if (barHeight < 10 && totalVal > 0) barHeight = 10;

                    var colGrid = new Grid { Margin = new Thickness(4, 0, 4, 0) };
                    Grid.SetColumn(colGrid, i);
                    
                    colGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    colGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var barStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                    Grid.SetRow(barStack, 0);

                    var valLabel = new TextBlock
                    {
                        Text = totalVal >= 1000000m ? $"{totalVal / 1000000m:N1}M" : $"{totalVal / 1000m:N0}K",
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 4),
                        Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99))
                    };
                    barStack.Children.Add(valLabel);

                    var barContainer = new Grid { Height = barHeight, Width = 28, HorizontalAlignment = HorizontalAlignment.Center };
                    
                    var barBorder = new Border
                    {
                        Height = barHeight,
                        Width = 24,
                        CornerRadius = new CornerRadius(6, 6, 0, 0),
                        ToolTip = $"Giờ chơi: {data.PlaytimeRevenue:N0} VND\nDịch vụ: {data.ServiceRevenue:N0} VND"
                    };

                    var gradient = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1)
                    };
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(96, 165, 250), 0.0));
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(37, 99, 235), 1.0));
                    barBorder.Background = gradient;

                    barContainer.Children.Add(barBorder);
                    barStack.Children.Add(barContainer);

                    var xLabel = new TextBlock
                    {
                        Text = data.Label,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 8, 0, 0)
                    };
                    Grid.SetRow(xLabel, 1);

                    colGrid.Children.Add(barStack);
                    colGrid.Children.Add(xLabel);

                    RevenueBarsContainer.Children.Add(colGrid);
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load dashboard stats: {ex.Message}");
        }
    }

    private async void StatsFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string period)
        {
            await LoadStatisticsDataAsync(period);
        }
    }
}

public class DashboardStatsResponse
{
    public string Period { get; set; } = string.Empty;
    public decimal PlaytimeRevenue { get; set; }
    public decimal ServiceRevenue { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalPlayHours { get; set; }
    public string PlaytimeGrowth { get; set; } = string.Empty;
    public string ServiceGrowth { get; set; } = string.Empty;
    public string TotalGrowth { get; set; } = string.Empty;
    public string PlayhoursGrowth { get; set; } = string.Empty;
    public List<DailyStatsData> DailyData { get; set; } = new();
    public List<TopMemberData> TopMembers { get; set; } = new();
}

public class DailyStatsData
{
    public string Label { get; set; } = string.Empty;
    public decimal PlaytimeRevenue { get; set; }
    public decimal ServiceRevenue { get; set; }
}

public class TopMemberData
{
    public string Username { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public int PlayHours { get; set; }
    public int Progress { get; set; }
}

public class TopMemberRowViewModel
{
    public string RankNumber { get; set; } = string.Empty;
    public string RankBackground { get; set; } = string.Empty;
    public string RankForeground { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int ProgressValue { get; set; }
    public string BarColor { get; set; } = string.Empty;
    public string PlayHoursText { get; set; } = string.Empty;
}
