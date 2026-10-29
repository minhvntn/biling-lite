using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Server.Admin.App;

public class PcAnalyticsResponse
{
    [JsonPropertyName("period")]
    public string Period { get; set; } = string.Empty;
    [JsonPropertyName("rangeStart")]
    public string RangeStart { get; set; } = string.Empty;
    [JsonPropertyName("rangeEndExclusive")]
    public string RangeEndExclusive { get; set; } = string.Empty;
    [JsonPropertyName("items")]
    public List<PcAnalyticsItem> Items { get; set; } = new();
}

public class PcAnalyticsItem
{
    [JsonPropertyName("pcId")]
    public string PcId { get; set; } = string.Empty;
    [JsonPropertyName("pcName")]
    public string PcName { get; set; } = string.Empty;
    [JsonPropertyName("totalDurationSeconds")]
    public int TotalDurationSeconds { get; set; }
    [JsonPropertyName("playHours")]
    public double PlayHours { get; set; }
    [JsonPropertyName("utilizationPercentage")]
    public double UtilizationPercentage { get; set; }
    [JsonPropertyName("sessionCount")]
    public int SessionCount { get; set; }
}

public partial class MainWindow : Window
{
    private string _currentPcAnalyticsPeriod = "week";

    private async Task RefreshPcAnalyticsAsync()
    {
        await LoadPcAnalyticsDataAsync(_currentPcAnalyticsPeriod);
    }

    private async Task LoadPcAnalyticsDataAsync(string period)
    {
        _currentPcAnalyticsPeriod = period;
        
        Dispatcher.Invoke(() =>
        {
            PcAnalyticsWeekButton.Background = period == "week" ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            PcAnalyticsWeekButton.Foreground = period == "week" ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));
            
            PcAnalyticsMonthButton.Background = period == "month" ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            PcAnalyticsMonthButton.Foreground = period == "month" ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));
            
            PcAnalyticsYearButton.Background = period == "year" ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            PcAnalyticsYearButton.Foreground = period == "year" ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81));
        });

        try
        {
            var response = await _httpClient.GetFromJsonAsync<PcAnalyticsResponse>(
                BuildApiUrl($"/reports/pc-analytics?period={period}"),
                JsonOptions());

            if (response?.Items != null)
            {
                Dispatcher.Invoke(() =>
                {
                    PcAnalyticsDataGrid.ItemsSource = response.Items;
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading pc analytics: {ex.Message}");
        }
    }

    private void PcAnalyticsFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string period)
        {
            _ = LoadPcAnalyticsDataAsync(period);
        }
    }
}
