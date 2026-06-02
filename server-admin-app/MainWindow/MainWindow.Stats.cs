using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private string _currentStatsPeriod = "week";
    private string _currentPcStatsPeriod = "week";
    private DateTime _currentPcStatsDate = DateTime.Today;
    private bool _isSyncingPcStatsDatePicker;
    private DateTime _lastRealtimeAlertFetchAt = DateTime.MinValue;

    private async Task RefreshStatisticsAsync()
    {
        await LoadStatisticsDataAsync(_currentStatsPeriod);
    }

    private async Task RefreshPcRevenueStatsAsync()
    {
        await LoadPcRevenueStatsAsync(_currentPcStatsPeriod, _currentPcStatsDate);
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
            _ = LoadPromotionsAsync();
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

                var topPcRows = response.TopPcs.Select((p, idx) => {
                    string rankBg = "#F3F4F6";
                    string rankFg = "#374151";
                    string barColor = "#3B82F6";
                    if (idx == 0) { rankBg = "#FEF3C7"; rankFg = "#D97706"; barColor = "#EF4444"; }
                    else if (idx == 1) { rankBg = "#E0E7FF"; rankFg = "#4F46E5"; barColor = "#F59E0B"; }
                    else if (idx == 2) { rankBg = "#ECFDF5"; rankFg = "#059669"; barColor = "#10B981"; }
                    
                    return new TopPcRowViewModel
                    {
                        RankNumber = (idx + 1).ToString(),
                        RankBackground = rankBg,
                        RankForeground = rankFg,
                        PcName = p.Name,
                        ProgressValue = p.Progress,
                        BarColor = barColor,
                        PlayHoursText = $"{p.PlayHours} giờ"
                    };
                }).ToList();

                MostPlayedPcsItemsControl.ItemsSource = topPcRows;

                var leastPcRows = response.LeastPcs.Select((p, idx) => {
                    return new TopPcRowViewModel
                    {
                        RankNumber = (idx + 1).ToString(),
                        PcName = p.Name,
                        ProgressValue = p.Progress,
                        PlayHoursText = $"{p.PlayHours} giờ"
                    };
                }).ToList();

                LeastPlayedPcsItemsControl.ItemsSource = leastPcRows;

                var maxServiceQuantity = response.TopServiceItems.Count == 0
                    ? 1
                    : Math.Max(1, response.TopServiceItems.Max(x => x.Quantity));

                var topServiceRows = response.TopServiceItems
                    .Select((item, idx) =>
                    {
                        string rankBg = "#F3F4F6";
                        string rankFg = "#374151";
                        string barColor = "#10B981";
                        if (idx == 0) { rankBg = "#FEF3C7"; rankFg = "#D97706"; barColor = "#EF4444"; }
                        else if (idx == 1) { rankBg = "#E0E7FF"; rankFg = "#4F46E5"; barColor = "#F59E0B"; }
                        else if (idx == 2) { rankBg = "#ECFDF5"; rankFg = "#059669"; barColor = "#10B981"; }

                        var progress = item.Quantity <= 0 ? 0 : Math.Clamp((int)Math.Round((double)item.Quantity * 100.0 / maxServiceQuantity), 0, 100);

                        var categoryText = string.IsNullOrWhiteSpace(item.Category) ? "-" : item.Category;
                        return new TopServiceItemRowViewModel
                        {
                            RankNumber = (idx + 1).ToString(),
                            RankBackground = rankBg,
                            RankForeground = rankFg,
                            ServiceName = item.Name,
                            MetaText = $"SL: {item.Quantity} | Don: {item.OrderCount} | Nhom: {categoryText}",
                            RevenueText = $"{item.Revenue:N0} VND",
                            ProgressValue = progress,
                            BarColor = barColor,
                        };
                    })
                    .ToList();

                TopServiceItemsItemsControl.ItemsSource = topServiceRows;
                TopServiceEmptyTextBlock.Visibility = topServiceRows.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                RenderDailyRevenueChart(
                    PlaytimeRevenueBarsContainer,
                    response.DailyData,
                    data => Math.Max(0m, data.PlaytimeRevenue),
                    Color.FromRgb(96, 165, 250),
                    Color.FromRgb(37, 99, 235),
                    value => FormatCompactMoney(value),
                    data => $"Doanh thu giờ chơi: {data.PlaytimeRevenue:N0} VND");

                RenderDailyRevenueChart(
                    ServiceRevenueBarsContainer,
                    response.DailyData,
                    data => Math.Max(0m, data.ServiceRevenue),
                    Color.FromRgb(52, 211, 153),
                    Color.FromRgb(5, 150, 105),
                    value => FormatCompactMoney(value),
                    data => $"Doanh thu dịch vụ: {data.ServiceRevenue:N0} VND");

                RenderDailyStackedRevenueChart(RevenueBarsContainer, response.DailyData);

                // Playtime insights (weekday/weekend, peak/off-peak, anomaly)
                var weeklyDistribution = response.WeeklyDistribution ?? new List<DistributionData>();
                var hourlyDistribution = response.HourlyDistribution ?? new List<DistributionData>();

                var weekdayRows = weeklyDistribution.Where(x => !x.IsWeekend).ToList();
                var weekendRows = weeklyDistribution.Where(x => x.IsWeekend).ToList();
                var weekdayAverage = weekdayRows.Count == 0 ? 0m : weekdayRows.Average(x => x.PlayHours);
                var weekendAverage = weekendRows.Count == 0 ? 0m : weekendRows.Average(x => x.PlayHours);

                WeekdayPlaytimeTextBlock.Text = $"{weekdayAverage:0.#} giờ / ngày";
                WeekendPlaytimeTextBlock.Text = $"{weekendAverage:0.#} giờ / ngày";

                if (weekdayAverage > 0m && weekendAverage > 0m)
                {
                    var deltaPercent = weekdayAverage == 0m
                        ? 0m
                        : ((weekendAverage - weekdayAverage) / weekdayAverage) * 100m;
                    if (deltaPercent >= 0m)
                    {
                        WeekdayPlaytimeHintTextBlock.Text = $"Thấp điểm hơn cuối tuần ({Math.Abs(deltaPercent):0.#}%)";
                        WeekendPlaytimeHintTextBlock.Text = $"Cao điểm (+{Math.Abs(deltaPercent):0.#}%)";
                        WeekendPlaytimeHintTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
                    }
                    else
                    {
                        WeekdayPlaytimeHintTextBlock.Text = $"Cao hơn cuối tuần (+{Math.Abs(deltaPercent):0.#}%)";
                        WeekendPlaytimeHintTextBlock.Text = $"Cuối tuần thấp hơn ({Math.Abs(deltaPercent):0.#}%)";
                        WeekendPlaytimeHintTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128));
                    }
                }
                else
                {
                    WeekdayPlaytimeHintTextBlock.Text = "Chưa đủ dữ liệu so sánh ngày thường/cuối tuần";
                    WeekendPlaytimeHintTextBlock.Text = "Chưa đủ dữ liệu so sánh ngày thường/cuối tuần";
                    WeekendPlaytimeHintTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128));
                }

                var peakHour = hourlyDistribution.OrderByDescending(x => x.PlayHours).FirstOrDefault();
                var offPeakHour = hourlyDistribution.OrderBy(x => x.PlayHours).FirstOrDefault();
                if (peakHour is not null)
                {
                    GoldenHoursTextBlock.Text = $"{peakHour.Label} ({peakHour.PlayHours:0.#}h)";
                    GoldenHoursHintTextBlock.Text = offPeakHour is null
                        ? "Không có dữ liệu giờ thấp điểm"
                        : $"Giờ thấp điểm: {offPeakHour.Label} ({offPeakHour.PlayHours:0.#}h)";
                }
                else
                {
                    GoldenHoursTextBlock.Text = "Chưa có dữ liệu";
                    GoldenHoursHintTextBlock.Text = "Không xác định được giờ cao/thấp điểm";
                }

                var mostPlayedDay = weeklyDistribution.OrderByDescending(x => x.PlayHours).FirstOrDefault();
                var leastPlayedDay = weeklyDistribution.OrderBy(x => x.PlayHours).FirstOrDefault();

                var weeklyAverage = weeklyDistribution.Count == 0 ? 0d : weeklyDistribution.Average(x => (double)x.PlayHours);
                var weeklyStd = weeklyDistribution.Count == 0
                    ? 0d
                    : Math.Sqrt(weeklyDistribution.Average(x =>
                    {
                        var d = (double)x.PlayHours - weeklyAverage;
                        return d * d;
                    }));
                var weeklySpikeThreshold = weeklyAverage + (1.5d * weeklyStd);
                var daySpikes = weeklyDistribution
                    .Where(x => (double)x.PlayHours > weeklySpikeThreshold && x.PlayHours > 0)
                    .Select(x => $"{x.Label} {x.PlayHours:0.#}h")
                    .ToList();

                var hourlyAverage = hourlyDistribution.Count == 0 ? 0d : hourlyDistribution.Average(x => (double)x.PlayHours);
                var hourlyStd = hourlyDistribution.Count == 0
                    ? 0d
                    : Math.Sqrt(hourlyDistribution.Average(x =>
                    {
                        var d = (double)x.PlayHours - hourlyAverage;
                        return d * d;
                    }));
                var hourlySpikeThreshold = hourlyAverage + (1.4d * hourlyStd);
                var hourSpikes = hourlyDistribution
                    .Where(x => (double)x.PlayHours > hourlySpikeThreshold && x.PlayHours > 0)
                    .Select(x => $"{x.Label} {x.PlayHours:0.#}h")
                    .ToList();

                var anomalyText = (daySpikes.Count == 0 && hourSpikes.Count == 0)
                    ? "Không phát hiện đột biến giờ chơi rõ rệt trong kỳ."
                    : $"Đột biến: ngày [{string.Join(", ", daySpikes)}], khung giờ [{string.Join(", ", hourSpikes)}].";

                PlaytimeInsightsTextBlock.Text =
                    $"Ngày chơi nhiều nhất: {(mostPlayedDay?.Label ?? "-")} ({mostPlayedDay?.PlayHours.ToString("0.#") ?? "0"}h) | " +
                    $"Ngày chơi ít nhất: {(leastPlayedDay?.Label ?? "-")} ({leastPlayedDay?.PlayHours.ToString("0.#") ?? "0"}h) | " +
                    $"Khung giờ đông nhất: {(peakHour?.Label ?? "-")} ({peakHour?.PlayHours.ToString("0.#") ?? "0"}h) | " +
                    $"Khung giờ ít nhất: {(offPeakHour?.Label ?? "-")} ({offPeakHour?.PlayHours.ToString("0.#") ?? "0"}h). " +
                    anomalyText;

                RenderDailyPlayHoursChart(PlayHoursBarsContainer, response.DailyData);

                // Render Weekly Distribution Chart
                WeeklyDistributionBarsContainer.Children.Clear();
                double maxWeeklyVal = weeklyDistribution.Count > 0 ? (double)weeklyDistribution.Max(w => w.PlayHours) : 1.0;
                for (int i = 0; i < weeklyDistribution.Count; i++)
                {
                    var data = weeklyDistribution[i];
                    double barHeight = maxWeeklyVal > 0 ? (double)data.PlayHours / maxWeeklyVal * 180.0 : 0.0;
                    if (barHeight < 10 && data.PlayHours > 0) barHeight = 10;

                    var colGrid = new Grid { Margin = new Thickness(4, 0, 4, 0) };
                    Grid.SetColumn(colGrid, i);
                    colGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    colGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var barStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                    Grid.SetRow(barStack, 0);

                    var valLabel = new TextBlock
                    {
                        Text = $"{data.PlayHours:0.#}h",
                        FontSize = 13,
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
                        ToolTip = $"Tổng giờ chơi: {data.PlayHours} giờ"
                    };

                    var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    if (data.IsWeekend)
                    {
                        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(251, 146, 60), 0.0));
                        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(234, 88, 12), 1.0));
                    }
                    else
                    {
                        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(96, 165, 250), 0.0));
                        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(37, 99, 235), 1.0));
                    }
                    barBorder.Background = gradient;

                    barContainer.Children.Add(barBorder);
                    barStack.Children.Add(barContainer);

                    var xLabel = new TextBlock
                    {
                        Text = data.Label,
                        FontSize = 13,
                        FontWeight = data.IsWeekend ? FontWeights.Bold : FontWeights.SemiBold,
                        Foreground = data.IsWeekend ? new SolidColorBrush(Color.FromRgb(234, 88, 12)) : new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 8, 0, 0)
                    };
                    Grid.SetRow(xLabel, 1);

                    colGrid.Children.Add(barStack);
                    colGrid.Children.Add(xLabel);
                    WeeklyDistributionBarsContainer.Children.Add(colGrid);
                }

                // Render Hourly Distribution Chart
                HourlyDistributionBarsContainer.Children.Clear();
                double maxHourlyVal = hourlyDistribution.Count > 0 ? (double)hourlyDistribution.Max(h => h.PlayHours) : 1.0;
                for (int i = 0; i < hourlyDistribution.Count; i++)
                {
                    var data = hourlyDistribution[i];
                    double barHeight = maxHourlyVal > 0 ? (double)data.PlayHours / maxHourlyVal * 180.0 : 0.0;
                    if (barHeight < 10 && data.PlayHours > 0) barHeight = 10;

                    var colGrid = new Grid { Margin = new Thickness(4, 0, 4, 0) };
                    Grid.SetColumn(colGrid, i);
                    colGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    colGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var barStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                    Grid.SetRow(barStack, 0);

                    var valLabel = new TextBlock
                    {
                        Text = $"{data.PlayHours:0.#}h",
                        FontSize = 13,
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
                        ToolTip = $"Tổng giờ chơi: {data.PlayHours} giờ"
                    };

                    var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    var hourlyLabel = data.Label?.Trim().ToLowerInvariant() ?? string.Empty;
                    bool isGoldenHour = hourlyLabel.Contains("tối") || hourlyLabel.Contains("toi") || hourlyLabel.Contains("18h-22h");
                    if (isGoldenHour)
                    {
                        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(52, 211, 153), 0.0));
                        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(5, 150, 105), 1.0));
                    }
                    else
                    {
                        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(45, 212, 191), 0.0));
                        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(13, 148, 136), 1.0));
                    }
                    barBorder.Background = gradient;

                    barContainer.Children.Add(barBorder);
                    barStack.Children.Add(barContainer);

                    var xLabel = new TextBlock
                    {
                        Text = data.Label,
                        FontSize = 13,
                        FontWeight = isGoldenHour ? FontWeights.Bold : FontWeights.SemiBold,
                        Foreground = isGoldenHour ? new SolidColorBrush(Color.FromRgb(5, 150, 105)) : new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 8, 0, 0)
                    };
                    Grid.SetRow(xLabel, 1);

                    colGrid.Children.Add(barStack);
                    colGrid.Children.Add(xLabel);
                    HourlyDistributionBarsContainer.Children.Add(colGrid);
                }
            });

            RenderActivityStats(response);

            await RefreshRealtimeAlertsAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load dashboard stats: {ex.Message}");
        }
    }

    private void RenderDailyRevenueChart(
        Grid targetContainer,
        IReadOnlyList<DailyStatsData> dailyData,
        Func<DailyStatsData, decimal> valueSelector,
        Color startColor,
        Color endColor,
        Func<decimal, string> valueTextSelector,
        Func<DailyStatsData, string> toolTipSelector)
    {
        RenderDailyMetricChart(
            targetContainer,
            dailyData,
            valueSelector,
            startColor,
            endColor,
            valueTextSelector,
            toolTipSelector);
    }

    private void RenderDailyPlayHoursChart(Grid targetContainer, IReadOnlyList<DailyStatsData> dailyData)
    {
        RenderDailyMetricChart(
            targetContainer,
            dailyData,
            data => Math.Max(0m, data.PlayHours),
            Color.FromRgb(52, 211, 153),
            Color.FromRgb(5, 150, 105),
            value => $"{value:0.#}h",
            data => $"Giờ chơi: {Math.Max(0m, data.PlayHours):0.#} giờ");
    }

    private void RenderDailyStackedRevenueChart(Grid targetContainer, IReadOnlyList<DailyStatsData> dailyData)
    {
        targetContainer.Children.Clear();

        var maxTotal = dailyData.Count == 0
            ? 1m
            : Math.Max(1m, dailyData.Max(data => Math.Max(0m, data.PlaytimeRevenue + data.ServiceRevenue)));

        for (int i = 0; i < dailyData.Count; i++)
        {
            var data = dailyData[i];
            var playtimeRevenue = Math.Max(0m, data.PlaytimeRevenue);
            var serviceRevenue = Math.Max(0m, data.ServiceRevenue);
            var totalRevenue = playtimeRevenue + serviceRevenue;
            var totalBarHeight = maxTotal > 0m ? (double)(totalRevenue / maxTotal) * 200.0 : 0.0;
            if (totalBarHeight < 10 && totalRevenue > 0m)
            {
                totalBarHeight = 10;
            }

            var playtimeHeight = totalRevenue <= 0m ? 0.0 : totalBarHeight * (double)(playtimeRevenue / totalRevenue);
            var serviceHeight = totalRevenue <= 0m ? 0.0 : totalBarHeight * (double)(serviceRevenue / totalRevenue);

            if (playtimeRevenue > 0m && playtimeHeight < 2.0)
            {
                playtimeHeight = 2.0;
            }

            if (serviceRevenue > 0m && serviceHeight < 2.0)
            {
                serviceHeight = 2.0;
            }

            var normalizedTotal = playtimeHeight + serviceHeight;
            if (normalizedTotal > totalBarHeight && normalizedTotal > 0.0)
            {
                var ratio = totalBarHeight / normalizedTotal;
                playtimeHeight *= ratio;
                serviceHeight *= ratio;
            }

            var colGrid = new Grid { Margin = new Thickness(4, 0, 4, 0) };
            Grid.SetColumn(colGrid, i);
            colGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            colGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var barStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetRow(barStack, 0);

            var valLabel = new TextBlock
            {
                Text = FormatCompactMoney(totalRevenue),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99))
            };
            barStack.Children.Add(valLabel);

            var barContainer = new Grid
            {
                Height = totalBarHeight,
                Width = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = $"Giờ chơi: {playtimeRevenue:N0} VND\nDịch vụ: {serviceRevenue:N0} VND\nTổng: {totalRevenue:N0} VND"
            };

            var segmentStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Orientation = Orientation.Vertical
            };

            if (playtimeHeight > 0.0)
            {
                var playtimeSegment = new Border
                {
                    Height = playtimeHeight,
                    Width = 24,
                    Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                    CornerRadius = serviceHeight > 0.0
                        ? new CornerRadius(0)
                        : new CornerRadius(6, 6, 0, 0)
                };
                segmentStack.Children.Add(playtimeSegment);
            }

            if (serviceHeight > 0.0)
            {
                var serviceSegment = new Border
                {
                    Height = serviceHeight,
                    Width = 24,
                    Background = new SolidColorBrush(Color.FromRgb(5, 150, 105)),
                    CornerRadius = new CornerRadius(6, 6, 0, 0)
                };
                segmentStack.Children.Add(serviceSegment);
            }

            barContainer.Children.Add(segmentStack);
            barStack.Children.Add(barContainer);

            var xLabel = new TextBlock
            {
                Text = data.Label,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(xLabel, 1);

            colGrid.Children.Add(barStack);
            colGrid.Children.Add(xLabel);
            targetContainer.Children.Add(colGrid);
        }
    }

    private void RenderDailyMetricChart(
        Grid targetContainer,
        IReadOnlyList<DailyStatsData> dailyData,
        Func<DailyStatsData, decimal> valueSelector,
        Color startColor,
        Color endColor,
        Func<decimal, string> valueTextSelector,
        Func<DailyStatsData, string> toolTipSelector)
    {
        targetContainer.Children.Clear();

        var maxValue = dailyData.Count == 0
            ? 1m
            : Math.Max(1m, dailyData.Max(valueSelector));

        for (int i = 0; i < dailyData.Count; i++)
        {
            var data = dailyData[i];
            var metricValue = Math.Max(0m, valueSelector(data));
            var barHeight = maxValue > 0m ? (double)(metricValue / maxValue) * 200.0 : 0.0;
            if (barHeight < 10 && metricValue > 0m)
            {
                barHeight = 10;
            }

            var colGrid = new Grid { Margin = new Thickness(4, 0, 4, 0) };
            Grid.SetColumn(colGrid, i);

            colGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            colGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var barStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetRow(barStack, 0);

            var valLabel = new TextBlock
            {
                Text = valueTextSelector(metricValue),
                FontSize = 14,
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
                ToolTip = toolTipSelector(data)
            };

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            gradient.GradientStops.Add(new GradientStop(startColor, 0.0));
            gradient.GradientStops.Add(new GradientStop(endColor, 1.0));
            barBorder.Background = gradient;

            barContainer.Children.Add(barBorder);
            barStack.Children.Add(barContainer);

            var xLabel = new TextBlock
            {
                Text = data.Label,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(xLabel, 1);

            colGrid.Children.Add(barStack);
            colGrid.Children.Add(xLabel);
            targetContainer.Children.Add(colGrid);
        }
    }

    private static string FormatCompactMoney(decimal value)
    {
        if (value <= 0m)
        {
            return "0";
        }

        if (value >= 1_000_000_000m)
        {
            return $"{value / 1_000_000_000m:0.#}B";
        }

        if (value >= 1_000_000m)
        {
            return $"{value / 1_000_000m:0.#}M";
        }

        if (value >= 1_000m)
        {
            return $"{value / 1_000m:0.#}K";
        }

        return $"{value:0}";
    }

    private async Task LoadPcRevenueStatsAsync(string period, DateTime anchorDate)
    {
        _currentPcStatsPeriod = NormalizePcStatsPeriod(period);
        _currentPcStatsDate = anchorDate.Date;

        Dispatcher.Invoke(() =>
        {
            PcStatsDayButton.Background = _currentPcStatsPeriod == "day" ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            PcStatsDayButton.Foreground = _currentPcStatsPeriod == "day" ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));

            PcStatsWeekButton.Background = _currentPcStatsPeriod == "week" ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            PcStatsWeekButton.Foreground = _currentPcStatsPeriod == "week" ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));

            PcStatsMonthButton.Background = _currentPcStatsPeriod == "month" ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            PcStatsMonthButton.Foreground = _currentPcStatsPeriod == "month" ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));

            PcStatsYearButton.Background = _currentPcStatsPeriod == "year" ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            PcStatsYearButton.Foreground = _currentPcStatsPeriod == "year" ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));

            _isSyncingPcStatsDatePicker = true;
            PcStatsDatePicker.SelectedDate = _currentPcStatsDate;
            _isSyncingPcStatsDatePicker = false;
        });

        try
        {
            var url =
                BuildApiUrl($"/reports/pc-revenue-stats?period={Uri.EscapeDataString(_currentPcStatsPeriod)}&date={Uri.EscapeDataString(_currentPcStatsDate.ToString("yyyy-MM-dd"))}");

            var response = await _httpClient.GetFromJsonAsync<PcRevenueStatsResponse>(url, JsonOptions());
            if (response is null)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                PcStatsPeriodLabelTextBlock.Text =
                    $"{response.PeriodLabel} | Mốc: {response.AnchorDate} | Từ {FormatDateTime(response.RangeStart)} đến {FormatDateTime(response.RangeEndExclusive)}";
                PcStatsPlaytimeRevenueTextBlock.Text = $"{response.TotalPlaytimeRevenue:N0} VND";
                PcStatsServiceRevenueTextBlock.Text = $"{response.TotalServiceRevenue:N0} VND";
                PcStatsTotalRevenueTextBlock.Text = $"{response.TotalRevenue:N0} VND";
                PcStatsTotalPlayHoursTextBlock.Text = $"{response.TotalPlayHours:0.#} giờ";

                var rows = response.Items.Select((item, idx) => new PcRevenueMachineStatsRowViewModel
                {
                    RankText = (idx + 1).ToString(),
                    PcName = item.PcName,
                    PlayHoursText = $"{item.PlayHours:0.#}",
                    PlaytimeRevenueText = $"{item.PlaytimeRevenue:N0}",
                    ServiceRevenueText = $"{item.ServiceRevenue:N0}",
                    TotalRevenueText = $"{item.TotalRevenue:N0}",
                }).ToList();

                PcStatsDataGrid.ItemsSource = rows;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load pc revenue stats: {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                PcStatsPeriodLabelTextBlock.Text = $"Không tải được thống kê theo máy: {ex.Message}";
                PcStatsDataGrid.ItemsSource = new List<PcRevenueMachineStatsRowViewModel>();
            });
        }
    }

    private static string NormalizePcStatsPeriod(string period)
    {
        var normalized = (period ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "day" => "day",
            "week" => "week",
            "month" => "month",
            "year" => "year",
            _ => "week",
        };
    }

    private async void StatsFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string period)
        {
            await LoadStatisticsDataAsync(period);
        }
    }

    private async void PcStatsFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string period)
        {
            await LoadPcRevenueStatsAsync(period, _currentPcStatsDate);
        }
    }

    private async void PcStatsDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingPcStatsDatePicker)
        {
            return;
        }

        var selected = PcStatsDatePicker.SelectedDate ?? DateTime.Today;
        await LoadPcRevenueStatsAsync(_currentPcStatsPeriod, selected);
    }

    private async void RefreshPcStatsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPcRevenueStatsAsync();
    }

    private async Task RefreshRealtimeAlertsAsync(bool force = false)
    {
        if (!force && (DateTime.UtcNow - _lastRealtimeAlertFetchAt) < TimeSpan.FromSeconds(8))
        {
            return;
        }

        try
        {
            var response = await _httpClient.GetFromJsonAsync<SystemEventsResponse>(
                BuildApiUrl("/reports/events/system?limit=120"),
                JsonOptions());

            _lastRealtimeAlertFetchAt = DateTime.UtcNow;
            UpdateRealtimeAlertPanelFromEvents(response?.Items ?? new List<SystemEventItem>());
        }
        catch
        {
            RealtimeAlertSummaryTextBlock.Text = "Không tải được cảnh báo realtime.";
            RealtimeAlertDetailsTextBlock.Text = "Vui lòng kiểm tra kết nối backend hoặc thử tải lại nhật ký hệ thống.";
        }
    }

    private void UpdateRealtimeAlertPanelFromEvents(IReadOnlyList<SystemEventItem> items)
    {
        var recent = items
            .OrderByDescending(item => ParseDateLocal(item.CreatedAt) ?? DateTime.MinValue)
            .Take(120)
            .ToList();

        if (recent.Count == 0)
        {
            RealtimeAlertSummaryTextBlock.Text = "Chưa có dữ liệu cảnh báo.";
            RealtimeAlertDetailsTextBlock.Text = "Hệ thống chưa ghi nhận sự kiện nào trong bộ log gần nhất.";
            return;
        }

        var criticalEvents = recent.Where(IsCriticalSystemEvent).ToList();
        var warningEvents = recent.Count(item =>
            string.Equals(item.EventType, "member.withdraw.requested", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.EventType, "member.topup.requested", StringComparison.OrdinalIgnoreCase));

        if (criticalEvents.Count > 0)
        {
            RealtimeAlertSummaryTextBlock.Text = $"Cảnh báo: {criticalEvents.Count} sự kiện rủi ro trong {recent.Count} log gần nhất.";
            var latest = criticalEvents
                .OrderByDescending(item => ParseDateLocal(item.CreatedAt) ?? DateTime.MinValue)
                .First();
            var (eventName, details) = TranslateSystemEvent(latest);
            var machineText = BuildMachineText(latest);
            RealtimeAlertDetailsTextBlock.Text =
                $"Mới nhất: {eventName} | Máy: {machineText} | Lúc: {FormatDateTime(latest.CreatedAt)}. " +
                $"{details}";
            return;
        }

        RealtimeAlertSummaryTextBlock.Text = "Hệ thống ổn định, chưa có cảnh báo nghiêm trọng.";
        RealtimeAlertDetailsTextBlock.Text =
            warningEvents > 0
                ? $"Có {warningEvents} yêu cầu nạp/rút đang phát sinh gần đây. Nên kiểm tra tab Nhật ký để xử lý sớm."
                : "Không phát hiện timeout lệnh, mất kết nối đột ngột hoặc sự kiện offline bất thường.";
    }

    private static bool IsCriticalSystemEvent(SystemEventItem item)
    {
        var type = (item.EventType ?? string.Empty).Trim().ToLowerInvariant();
        if (type.StartsWith("command.timeout", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(type, "session.closed.auto_offline", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(type, "pc.status.changed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (item.Payload is not JsonElement payload || payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var status = ReadJsonString(payload, "status");
        return string.Equals(status, "OFFLINE", StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadPromotionsAsync()
    {
        try
        {
            var promotions = await _httpClient.GetFromJsonAsync<List<TimeBasedPromotionDto>>(
                BuildApiUrl("/pricing/promotions"),
                JsonOptions());

            if (promotions is null) return;

            Dispatcher.Invoke(() =>
            {
                var viewModels = promotions.Select(p => new PromotionRowViewModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    TimeRange = $"{p.StartTime} - {p.EndTime}",
                    DaysOfWeekText = p.AnnualDates != null && p.AnnualDates.Count > 0 
                        ? $"Ngày lễ: {string.Join(", ", p.AnnualDates)}" 
                        : string.Join(", ", p.DaysOfWeek.Select(d => d == 0 ? "CN" : $"T{d + 1}")),
                    DiscountText = $"{p.DiscountPercent:N0}%",
                    StatusText = p.IsActive ? "Đang chạy" : "Tạm dừng",
                    StatusBackground = p.IsActive ? "#ECFDF5" : "#F3F4F6",
                    StatusForeground = p.IsActive ? "#059669" : "#6B7280"
                }).ToList();

                PromotionsDataGrid.ItemsSource = viewModels;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load promotions: {ex.Message}");
        }
    }

    private async void ReloadPromotionsButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadPromotionsAsync();
    }

    private async void DeletePromotionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string promoId)
        {
            var confirm = MessageBox.Show("Bạn có chắc chắn muốn xóa chương trình khuyến mãi này không?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    var res = await _httpClient.DeleteAsync(BuildApiUrl($"/pricing/promotions/{promoId}"));
                    if (res.IsSuccessStatusCode)
                    {
                        MessageBox.Show("Đã xóa chương trình khuyến mãi thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                        await LoadPromotionsAsync();
                    }
                    else
                    {
                        MessageBox.Show("Xóa chương trình khuyến mãi thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async void AddPromotionButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Hệ thống sẽ tạo tự động một chương trình Khuyến mãi Giờ vàng Ngày thường:\n\n" +
            "• Tên: Khuyến mãi Giờ vàng Ngày thường\n" +
            "• Khung giờ: 08:00 - 16:00\n" +
            "• Ngày áp dụng: Thứ 2 đến Thứ 6\n" +
            "• Giảm giá: 10%\n\n" +
            "Bạn có đồng ý tạo chương trình khuyến mãi này không?",
            "Thêm chương trình khuyến mãi mới",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var payload = new
                {
                    name = "Khuyến mãi Giờ vàng Ngày thường",
                    daysOfWeek = new List<int> { 1, 2, 3, 4, 5 },
                    annualDates = new List<string>(),
                    startTime = "08:00",
                    endTime = "16:00",
                    discountPercent = 10,
                    isActive = true
                };

                var res = await _httpClient.PostAsJsonAsync(BuildApiUrl("/pricing/promotions"), payload, JsonOptions());
                if (res.IsSuccessStatusCode)
                {
                    MessageBox.Show("Thêm chương trình khuyến mãi thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadPromotionsAsync();
                }
                else
                {
                    MessageBox.Show("Thêm chương trình khuyến mãi thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void AddAnnualPromotionButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Hệ thống sẽ tạo tự động một chương trình Khuyến mãi Ngày Lễ:\n\n" +
            "• Tên: Khuyến mãi Lễ 30/4\n" +
            "• Khung giờ: 00:00 - 23:59\n" +
            "• Ngày áp dụng: 30/04, 01/05\n" +
            "• Giảm giá: 50%\n\n" +
            "Bạn có đồng ý tạo chương trình khuyến mãi này không?",
            "Thêm chương trình Khuyến mãi Lễ",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var payload = new
                {
                    name = "Khuyến mãi Lễ 30/4",
                    daysOfWeek = new List<int>(),
                    annualDates = new List<string> { "30/04", "01/05" },
                    startTime = "00:00",
                    endTime = "23:59",
                    discountPercent = 50,
                    isActive = true
                };

                var res = await _httpClient.PostAsJsonAsync(BuildApiUrl("/pricing/promotions"), payload, JsonOptions());
                if (res.IsSuccessStatusCode)
                {
                    MessageBox.Show("Thêm chương trình sự kiện ngày lễ thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadPromotionsAsync();
                }
                else
                {
                    MessageBox.Show("Thêm chương trình thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void PromotionsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;

        var row = e.Row.Item as PromotionRowViewModel;
        if (row is null) return;

        var el = e.EditingElement as TextBox;
        if (el is null) return;

        string newVal = el.Text.Trim();
        string header = e.Column.Header?.ToString() ?? string.Empty;

        var payload = new Dictionary<string, object>();

        if (header == "Tên chương trình")
        {
            if (string.IsNullOrEmpty(newVal)) return;
            payload["name"] = newVal;
        }
        else if (header == "Khung giờ áp dụng")
        {
            var parts = newVal.Split('-');
            if (parts.Length != 2)
            {
                MessageBox.Show("Khung giờ áp dụng không đúng định dạng. Định dạng chuẩn: HH:mm - HH:mm (ví dụ: 08:00 - 16:00)", "Lỗi định dạng", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Cancel = true;
                return;
            }
            payload["startTime"] = parts[0].Trim();
            payload["endTime"] = parts[1].Trim();
        }
        else if (header == "Ngày áp dụng")
        {
            if (newVal.StartsWith("Ngày lễ:", StringComparison.OrdinalIgnoreCase))
            {
                var datesText = newVal.Substring("Ngày lễ:".Length).Split(',');
                var dates = new List<string>();
                foreach (var d in datesText)
                {
                    string clean = d.Trim();
                    if (!string.IsNullOrEmpty(clean)) dates.Add(clean);
                }
                payload["annualDates"] = dates;
                payload["daysOfWeek"] = new List<int>(); // Xoá ngày thường
            }
            else
            {
                var daysText = newVal.Split(',');
                var days = new List<int>();
                foreach (var d in daysText)
                {
                    string clean = d.Trim().ToUpper();
                    if (clean == "CN") days.Add(0);
                    else if (clean.StartsWith("T") && int.TryParse(clean.Substring(1), out int dayNum) && dayNum >= 2 && dayNum <= 7)
                    {
                        days.Add(dayNum - 1);
                    }
                }

                if (days.Count == 0)
                {
                    MessageBox.Show("Định dạng không đúng. Cần là T2, T3 (hoặc CN). Nếu là lễ thì gõ: 'Ngày lễ: 30/04, 01/05'", "Lỗi định dạng", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Cancel = true;
                    return;
                }
                payload["daysOfWeek"] = days;
                payload["annualDates"] = new List<string>(); // Xóa ngày lễ
            }
        }
        else if (header == "% Giảm giá")
        {
            string cleanPct = newVal.Replace("%", "").Trim();
            if (!decimal.TryParse(cleanPct, out decimal discount) || discount < 0 || discount > 100)
            {
                MessageBox.Show("Phần trăm giảm giá phải là số từ 0 đến 100.", "Lỗi định dạng", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Cancel = true;
                return;
            }
            payload["discountPercent"] = discount;
        }

        if (payload.Count > 0)
        {
            try
            {
                var res = await _httpClient.PutAsJsonAsync(BuildApiUrl($"/pricing/promotions/{row.Id}"), payload, JsonOptions());
                if (!res.IsSuccessStatusCode)
                {
                    MessageBox.Show("Cập nhật khuyến mãi thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Cancel = true;
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100);
                        await LoadPromotionsAsync();
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi kết nối: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Cancel = true;
            }
        }
    }

    private void ActivityPeriodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActivityPeriodComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            var period = selectedItem.Tag?.ToString() ?? "week";
            _ = LoadStatisticsDataAsync(period);
        }
    }

    private void RenderActivityStats(DashboardStatsResponse response)
    {
        ActivityTotalRevenueTextBlock.Text = $"{response.TotalRevenue:N0}đ";
        ActivityRevenueGrowthTextBlock.Text = response.TotalGrowth;
        
        ActivityTotalCustomersTextBlock.Text = $"{response.TotalMembers:N0}";
        ActivityCustomersGrowthTextBlock.Text = response.TotalMembersGrowth;
        
        ActivityVipCustomersTextBlock.Text = $"{response.VipMembers:N0}";
        ActivityVipGrowthTextBlock.Text = response.VipMembersGrowth;

        // Render Top Services
        ActivityTopServicesStackPanel.Children.Clear();
        var maxServiceRevenue = response.TopServiceItems.Count == 0
            ? 1m
            : Math.Max(1m, response.TopServiceItems.Max(x => x.Revenue));

        var barColors = new[] { "#A855F7", "#C084FC", "#D8B4FE", "#E9D5FF" };
        var top4Services = response.TopServiceItems.Take(4).ToList();
        for (int i = 0; i < top4Services.Count; i++)
        {
            var item = top4Services[i];
            var progress = (double)(item.Revenue / maxServiceRevenue) * 100.0;
            var colorStr = barColors[i % barColors.Length];
            var barColor = (Color)ColorConverter.ConvertFromString(colorStr);

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            var nameText = new TextBlock
            {
                Text = item.Category ?? item.Name,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)) // #475569
            };
            Grid.SetColumn(nameText, 0);

            var progressBar = new ProgressBar
            {
                Value = progress,
                Height = 10,
                Foreground = new SolidColorBrush(barColor),
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)), // #F1F5F9
                BorderThickness = new Thickness(0),
                Margin = new Thickness(12, 0, 12, 0)
            };
            Grid.SetColumn(progressBar, 1);

            var valText = new TextBlock
            {
                Text = FormatCompactMoney(item.Revenue),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)) // #1E293B
            };
            Grid.SetColumn(valText, 2);

            grid.Children.Add(nameText);
            grid.Children.Add(progressBar);
            grid.Children.Add(valText);
            
            ActivityTopServicesStackPanel.Children.Add(grid);
        }

        // Render Activity Daily Chart (Line Chart)
        ActivityDailyChartCanvas.Children.Clear();
        ActivityDailyChartXAxisGrid.Children.Clear();
        ActivityDailyChartXAxisGrid.ColumnDefinitions.Clear();

        if (response.DailyData.Count > 0)
        {
            var maxVal = response.DailyData.Max(d => d.PlaytimeRevenue + d.ServiceRevenue);
            if (maxVal == 0) maxVal = 1;
            
            double width = 340;
            double height = 150;
            double stepX = response.DailyData.Count > 1 ? width / (response.DailyData.Count - 1) : width;
            
            var points = new PointCollection();
            points.Add(new Point(0, height)); // Bottom left for polygon
            
            var linePoints = new PointCollection();

            for (int i = 0; i < response.DailyData.Count; i++)
            {
                var data = response.DailyData[i];
                var total = data.PlaytimeRevenue + data.ServiceRevenue;
                
                double x = i * stepX;
                double y = height - ((double)(total / maxVal) * (height - 20)) - 10; // 10 padding top/bottom
                
                points.Add(new Point(x, y));
                linePoints.Add(new Point(x, y));
                
                // Add X Axis Label
                ActivityDailyChartXAxisGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var xLabel = new TextBlock
                {
                    Text = data.Label,
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), // #94A3B8
                    FontSize = 12
                };
                Grid.SetColumn(xLabel, i);
                ActivityDailyChartXAxisGrid.Children.Add(xLabel);
            }
            
            points.Add(new Point(width, height)); // Bottom right for polygon
            
            var polygon = new Polygon
            {
                Points = points,
                Fill = new SolidColorBrush(Color.FromRgb(239, 246, 255)) // #EFF6FF
            };
            
            var polyline = new Polyline
            {
                Points = linePoints,
                Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246)), // #3B82F6
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round
            };
            
            ActivityDailyChartCanvas.Children.Add(polygon);
            ActivityDailyChartCanvas.Children.Add(polyline);
            
            // Re-add ellipses on top
            for (int i = 0; i < response.DailyData.Count; i++)
            {
                var data = response.DailyData[i];
                var total = data.PlaytimeRevenue + data.ServiceRevenue;
                double x = i * stepX;
                double y = height - ((double)(total / maxVal) * (height - 20)) - 10;
                
                var ellipse = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    ToolTip = $"{data.Label}: {total:N0}đ"
                };
                Canvas.SetLeft(ellipse, x - 5);
                Canvas.SetTop(ellipse, y - 5);
                ActivityDailyChartCanvas.Children.Add(ellipse);
            }
        }
        
        RenderActivityDonutChart();
    }
    
    private void RenderActivityDonutChart()
    {
        var onlineCount = _machineRows.Count(m => m.StatusCode == "IN_USE");
        var emptyCount = _machineRows.Count(m => m.StatusCode == "ONLINE");
        var maintenanceCount = _machineRows.Count(m => m.StatusCode == "LOCKED" || m.StatusCode == "OFFLINE");
        var total = onlineCount + emptyCount + maintenanceCount;
        if (total == 0) total = 1; // Prevent div by 0 for display
        var actualTotal = _machineRows.Count;
        
        ActivityTotalMachinesTextBlock.Text = $"Tổng: {actualTotal} máy";
        
        if (actualTotal == 0)
        {
            ActivityInUseMachinesTextBlock.Text = "0 (0%)";
            ActivityEmptyMachinesTextBlock.Text = "0 (0%)";
            ActivityMaintenanceMachinesTextBlock.Text = "0 (0%)";
            return;
        }
        
        double onlinePct = (double)onlineCount / total;
        double emptyPct = (double)emptyCount / total;
        double maintPct = (double)maintenanceCount / total;
        
        ActivityInUseMachinesTextBlock.Text = $"{onlineCount} ({onlinePct:P0})";
        ActivityEmptyMachinesTextBlock.Text = $"{emptyCount} ({emptyPct:P0})";
        ActivityMaintenanceMachinesTextBlock.Text = $"{maintenanceCount} ({maintPct:P0})";
        
        // Draw Donut
        double radius = 68;
        double cx = 80;
        double cy = 80;
        
        // Start angle -90deg (top)
        double currentAngle = -90;
        
        ActivityDonutInUsePath.Data = CreateArcGeometry(cx, cy, radius, currentAngle, onlinePct * 360);
        currentAngle += onlinePct * 360;
        
        ActivityDonutEmptyPath.Data = CreateArcGeometry(cx, cy, radius, currentAngle, emptyPct * 360);
        currentAngle += emptyPct * 360;
        
        ActivityDonutMaintenancePath.Data = CreateArcGeometry(cx, cy, radius, currentAngle, maintPct * 360);
    }
    
    private Geometry CreateArcGeometry(double cx, double cy, double radius, double startAngle, double sweepAngle)
    {
        if (sweepAngle <= 0) return new PathGeometry();
        if (sweepAngle >= 360) sweepAngle = 359.99; // Approximates full circle without failing ArcSegment
        
        double startRad = startAngle * Math.PI / 180.0;
        double endRad = (startAngle + sweepAngle) * Math.PI / 180.0;
        
        Point startPoint = new Point(cx + radius * Math.Cos(startRad), cy + radius * Math.Sin(startRad));
        Point endPoint = new Point(cx + radius * Math.Cos(endRad), cy + radius * Math.Sin(endRad));
        
        bool isLargeArc = sweepAngle > 180.0;
        
        PathFigure figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false
        };
        
        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Size(radius, radius),
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise,
            RotationAngle = 0
        });
        
        return new PathGeometry(new[] { figure });
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
    
    public int TotalMembers { get; set; }
    public int VipMembers { get; set; }
    public string TotalMembersGrowth { get; set; } = string.Empty;
    public string VipMembersGrowth { get; set; } = string.Empty;

    public List<DailyStatsData> DailyData { get; set; } = new();
    public List<TopMemberData> TopMembers { get; set; } = new();
    public List<TopPcData> TopPcs { get; set; } = new();
    public List<TopPcData> LeastPcs { get; set; } = new();
    public List<PcRevenueStatsData> PcRevenueStats { get; set; } = new();
    public List<TopServiceItemData> TopServiceItems { get; set; } = new();
    public List<DistributionData> WeeklyDistribution { get; set; } = new();
    public List<DistributionData> HourlyDistribution { get; set; } = new();
}

public class PcRevenueStatsResponse
{
    public string Period { get; set; } = string.Empty;
    public string AnchorDate { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public string RangeStart { get; set; } = string.Empty;
    public string RangeEndExclusive { get; set; } = string.Empty;
    public decimal TotalPlayHours { get; set; }
    public decimal TotalPlaytimeRevenue { get; set; }
    public decimal TotalServiceRevenue { get; set; }
    public decimal TotalRevenue { get; set; }
    public List<PcRevenueStatsData> Items { get; set; } = new();
}

public class DistributionData
{
    public string Label { get; set; } = string.Empty;
    public decimal PlayHours { get; set; }
    public bool IsWeekend { get; set; }
}

public class DailyStatsData
{
    public string Label { get; set; } = string.Empty;
    public decimal PlaytimeRevenue { get; set; }
    public decimal ServiceRevenue { get; set; }
    public decimal PlayHours { get; set; }
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

public class TopPcData
{
    public string Name { get; set; } = string.Empty;
    public int PlayHours { get; set; }
    public int Progress { get; set; }
}

public class PcRevenueStatsData
{
    public string PcId { get; set; } = string.Empty;
    public string PcName { get; set; } = string.Empty;
    public decimal PlayHours { get; set; }
    public decimal PlaytimeRevenue { get; set; }
    public decimal ServiceRevenue { get; set; }
    public decimal TotalRevenue { get; set; }
    public int RevenueProgress { get; set; }
    public int PlayHoursProgress { get; set; }
}

public class TopServiceItemData
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int Quantity { get; set; }
    public decimal Revenue { get; set; }
    public int OrderCount { get; set; }
}

public class TopPcRowViewModel
{
    public string RankNumber { get; set; } = string.Empty;
    public string RankBackground { get; set; } = string.Empty;
    public string RankForeground { get; set; } = string.Empty;
    public string PcName { get; set; } = string.Empty;
    public int ProgressValue { get; set; }
    public string BarColor { get; set; } = string.Empty;
    public string PlayHoursText { get; set; } = string.Empty;
}

public class TopServiceItemRowViewModel
{
    public string RankNumber { get; set; } = string.Empty;
    public string RankBackground { get; set; } = string.Empty;
    public string RankForeground { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string MetaText { get; set; } = string.Empty;
    public string RevenueText { get; set; } = string.Empty;
    public int ProgressValue { get; set; }
    public string BarColor { get; set; } = string.Empty;
}

public class PcRevenueMachineStatsRowViewModel
{
    public string RankText { get; set; } = string.Empty;
    public string PcName { get; set; } = string.Empty;
    public string PlayHoursText { get; set; } = string.Empty;
    public string PlaytimeRevenueText { get; set; } = string.Empty;
    public string ServiceRevenueText { get; set; } = string.Empty;
    public string TotalRevenueText { get; set; } = string.Empty;
}

public class TimeBasedPromotionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<int> DaysOfWeek { get; set; } = new();
    public List<string> AnnualDates { get; set; } = new();
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public bool IsActive { get; set; }
}

public class PromotionRowViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public string DaysOfWeekText { get; set; } = string.Empty;
    public string DiscountText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string StatusBackground { get; set; } = string.Empty;
    public string StatusForeground { get; set; } = string.Empty;
}

