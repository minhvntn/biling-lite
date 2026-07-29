using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Server.Admin.App;

public partial class AppUsageWindow : Window
{
    public class AppUsageDto
    {
        public int id { get; set; }
        public string appName { get; set; } = string.Empty;
        public string? windowTitle { get; set; }
        public DateTime startedAt { get; set; }
        public DateTime? endedAt { get; set; }
    }

    private readonly string _pcId;

    private readonly MainWindow _parent;

    public AppUsageWindow(MainWindow parent, string pcId, string pcName)
    {
        InitializeComponent();
        _parent = parent;
        _pcId = pcId;
        TitleTextBlock.Text = $"Các ứng dụng đang chạy trên: {pcName}";
        
        _ = LoadAppUsageAsync();
    }

    private async Task LoadAppUsageAsync()
    {
        try
        {
            var url = _parent.BuildApiUrl($"/pcs/{_pcId}/app-usage");
            var items = await _parent._httpClient.GetFromJsonAsync<List<AppUsageDto>>(url);
            
            if (items != null)
            {
                Dispatcher.Invoke(() =>
                {
                    // Filter out ended ones manually to show only currently active apps if we want,
                    // or just show all fetched history (the endpoint returns history descending).
                    // Let's filter to only those without endedAt to show current active.
                    var activeApps = items.FindAll(x => x.endedAt == null);
                    AppUsageDataGrid.ItemsSource = activeApps;
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(this, $"Không thể tải dữ liệu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }
}
