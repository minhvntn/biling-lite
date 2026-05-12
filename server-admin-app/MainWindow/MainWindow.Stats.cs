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

                // Render Weekly Distribution Chart
                WeeklyDistributionBarsContainer.Children.Clear();
                double maxWeeklyVal = response.WeeklyDistribution.Any() ? response.WeeklyDistribution.Max(w => w.PlayHours) : 1;
                for (int i = 0; i < response.WeeklyDistribution.Count; i++)
                {
                    var data = response.WeeklyDistribution[i];
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
                        Text = $"{data.PlayHours}h",
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
                        FontSize = 10,
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
                double maxHourlyVal = response.HourlyDistribution.Any() ? response.HourlyDistribution.Max(h => h.PlayHours) : 1;
                for (int i = 0; i < response.HourlyDistribution.Count; i++)
                {
                    var data = response.HourlyDistribution[i];
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
                        Text = $"{data.PlayHours}h",
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
                        ToolTip = $"Tổng giờ chơi: {data.PlayHours} giờ"
                    };

                    var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    bool isGoldenHour = data.Label.Contains("Tối");
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
                        FontSize = 10,
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
                    DaysOfWeekText = string.Join(", ", p.DaysOfWeek.Select(d => d == 7 ? "CN" : $"T{d + 1}")),
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
            var daysText = newVal.Split(',');
            var days = new List<int>();
            foreach (var d in daysText)
            {
                string clean = d.Trim().ToUpper();
                if (clean == "CN") days.Add(7);
                else if (clean.StartsWith("T") && int.TryParse(clean.Substring(1), out int dayNum) && dayNum >= 2 && dayNum <= 7)
                {
                    days.Add(dayNum - 1);
                }
            }

            if (days.Count == 0)
            {
                MessageBox.Show("Ngày áp dụng không đúng định dạng. Định dạng chuẩn: T2, T3, T4 (hoặc CN)", "Lỗi định dạng", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Cancel = true;
                return;
            }
            payload["daysOfWeek"] = days;
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
    public List<TopPcData> TopPcs { get; set; } = new();
    public List<TopPcData> LeastPcs { get; set; } = new();
    public List<TopServiceItemData> TopServiceItems { get; set; } = new();
    public List<DistributionData> WeeklyDistribution { get; set; } = new();
    public List<DistributionData> HourlyDistribution { get; set; } = new();
}

public class DistributionData
{
    public string Label { get; set; } = string.Empty;
    public int PlayHours { get; set; }
    public bool IsWeekend { get; set; }
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

public class TopPcData
{
    public string Name { get; set; } = string.Empty;
    public int PlayHours { get; set; }
    public int Progress { get; set; }
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

public class TimeBasedPromotionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<int> DaysOfWeek { get; set; } = new();
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

