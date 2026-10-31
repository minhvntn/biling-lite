using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Server.Admin.App;

public class HardwareRow : INotifyPropertyChanged
{
    private string _cpu = string.Empty;
    private string _ram = string.Empty;
    private string _gpu = string.Empty;
    private string _motherboard = string.Empty;
    private string _storage = string.Empty;
    private string _monitor = string.Empty;
    private string _lan = string.Empty;
    private string _temperature = string.Empty;
    private string _note = string.Empty;

    public string PcId { get; set; } = string.Empty;
    public string PcName { get; set; } = string.Empty;

    public string Cpu
    {
        get => _cpu;
        set
        {
            if (_cpu != value)
            {
                _cpu = value;
                IsModified = true;
                OnPropertyChanged(nameof(Cpu));
                OnPropertyChanged(nameof(DisplayCpu));
                OnPropertyChanged(nameof(IsSynced));
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(StatusDotColor));
                OnPropertyChanged(nameof(StatusBgColor));
                OnPropertyChanged(nameof(StatusBorderColor));
            }
        }
    }

    public string Ram
    {
        get => _ram;
        set
        {
            if (_ram != value)
            {
                _ram = value;
                IsModified = true;
                OnPropertyChanged(nameof(Ram));
                OnPropertyChanged(nameof(DisplayRam));
            }
        }
    }

    public string Gpu
    {
        get => _gpu;
        set
        {
            if (_gpu != value)
            {
                _gpu = value;
                IsModified = true;
                OnPropertyChanged(nameof(Gpu));
                OnPropertyChanged(nameof(DisplayGpu));
                OnPropertyChanged(nameof(IsSynced));
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(StatusDotColor));
                OnPropertyChanged(nameof(StatusBgColor));
                OnPropertyChanged(nameof(StatusBorderColor));
            }
        }
    }

    public string Motherboard
    {
        get => _motherboard;
        set
        {
            if (_motherboard != value)
            {
                _motherboard = value;
                IsModified = true;
                OnPropertyChanged(nameof(Motherboard));
                OnPropertyChanged(nameof(DisplayMotherboard));
            }
        }
    }

    public string Storage
    {
        get => _storage;
        set
        {
            if (_storage != value)
            {
                _storage = value;
                IsModified = true;
                OnPropertyChanged(nameof(Storage));
                OnPropertyChanged(nameof(DisplayStorage));
            }
        }
    }

    public string Monitor
    {
        get => _monitor;
        set
        {
            if (_monitor != value)
            {
                _monitor = value;
                IsModified = true;
                OnPropertyChanged(nameof(Monitor));
                OnPropertyChanged(nameof(DisplayMonitor));
            }
        }
    }

    public string Lan
    {
        get => _lan;
        set
        {
            if (_lan != value)
            {
                _lan = value;
                IsModified = true;
                OnPropertyChanged(nameof(Lan));
                OnPropertyChanged(nameof(DisplayLan));
            }
        }
    }

    public string Temperature
    {
        get => _temperature;
        set
        {
            if (_temperature != value)
            {
                _temperature = value;
                IsModified = true;
                OnPropertyChanged(nameof(Temperature));
                OnPropertyChanged(nameof(DisplayTemperature));
            }
        }
    }

    public string Note
    {
        get => _note;
        set
        {
            if (_note != value)
            {
                _note = value;
                IsModified = true;
                OnPropertyChanged(nameof(Note));
            }
        }
    }

    public bool IsSynced => !string.IsNullOrWhiteSpace(Cpu) || !string.IsNullOrWhiteSpace(Gpu);
    public string StatusDisplay => IsSynced ? "Đã nhận diện" : "Chờ máy kết nối";
    public string StatusDotColor => IsSynced ? "#10B981" : "#94A3B8";
    public string StatusBgColor => IsSynced ? "#ECFDF5" : "#F8FAFC";
    public string StatusBorderColor => IsSynced ? "#A7F3D0" : "#E2E8F0";

    public string DisplayCpu => !string.IsNullOrWhiteSpace(Cpu) ? Cpu : "—";
    public string DisplayRam => !string.IsNullOrWhiteSpace(Ram) ? Ram : "—";
    public string DisplayGpu => !string.IsNullOrWhiteSpace(Gpu) ? Gpu : "—";
    public string DisplayMotherboard => !string.IsNullOrWhiteSpace(Motherboard) ? Motherboard : "—";
    public string DisplayStorage => !string.IsNullOrWhiteSpace(Storage) ? Storage : "—";
    public string DisplayLan => !string.IsNullOrWhiteSpace(Lan) ? Lan : "—";
    public string DisplayMonitor => !string.IsNullOrWhiteSpace(Monitor) ? Monitor : "—";
    public string DisplayTemperature => !string.IsNullOrWhiteSpace(Temperature) ? Temperature : "Ổn định";

    public bool IsModified { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class PcHardwareDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("pcId")]
    public string PcId { get; set; } = string.Empty;
    [JsonPropertyName("cpu")]
    public string? Cpu { get; set; }
    [JsonPropertyName("ram")]
    public string? Ram { get; set; }
    [JsonPropertyName("gpu")]
    public string? Gpu { get; set; }
    [JsonPropertyName("motherboard")]
    public string? Motherboard { get; set; }
    [JsonPropertyName("lan")]
    public string? Lan { get; set; }
    [JsonPropertyName("temperature")]
    public string? Temperature { get; set; }
    [JsonPropertyName("storage")]
    public string? Storage { get; set; }
    [JsonPropertyName("monitor")]
    public string? Monitor { get; set; }
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public class PcMaintenanceLogDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("pcId")]
    public string PcId { get; set; } = string.Empty;
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    [JsonPropertyName("cost")]
    public decimal Cost { get; set; }
    [JsonPropertyName("performedBy")]
    public string PerformedBy { get; set; } = string.Empty;
}

public partial class MainWindow : Window
{
    private readonly ObservableCollection<HardwareRow> _hardwareRows = new();
    private readonly List<HardwareRow> _allHardwareMasterList = new();
    private string _hardwareSearchQuery = string.Empty;

    private async Task RefreshAllHardwareAsync()
    {
        try
        {
            Dispatcher.Invoke(() => {
                if (HardwareStatusTextBlock != null)
                {
                    HardwareStatusTextBlock.Text = "Đang tải dữ liệu...";
                    HardwareStatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
                }
            });

            var allHardware = await _httpClient.GetFromJsonAsync<List<PcHardwareDto>>(BuildApiUrl("/pcs/hardware/all"), JsonOptions());
            if (allHardware != null)
            {
                var dict = allHardware.ToDictionary(h => h.PcId);
                var items = new List<HardwareRow>();
                
                foreach (var machine in _allMachineRows)
                {
                    dict.TryGetValue(machine.Id, out var hw);
                    
                    var row = new HardwareRow
                    {
                        PcId = machine.Id,
                        PcName = machine.Name,
                        Cpu = hw?.Cpu ?? string.Empty,
                        Ram = hw?.Ram ?? string.Empty,
                        Gpu = hw?.Gpu ?? string.Empty,
                        Motherboard = hw?.Motherboard ?? string.Empty,
                        Storage = hw?.Storage ?? string.Empty,
                        Monitor = hw?.Monitor ?? string.Empty,
                        Lan = hw?.Lan ?? string.Empty,
                        Temperature = hw?.Temperature ?? string.Empty,
                        Note = hw?.Note ?? string.Empty,
                        IsModified = false
                    };
                    items.Add(row);
                }

                Dispatcher.Invoke(() =>
                {
                    _allHardwareMasterList.Clear();
                    _allHardwareMasterList.AddRange(items);
                    ApplyHardwareFilter();
                    UpdateHardwareSummaryKpis();
                    if (HardwareStatusTextBlock != null)
                    {
                        HardwareStatusTextBlock.Text = $"Cập nhật: {DateTime.Now:HH:mm:ss}";
                        HardwareStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => {
                if (HardwareStatusTextBlock != null)
                {
                    HardwareStatusTextBlock.Text = "Lỗi tải dữ liệu";
                    HardwareStatusTextBlock.Foreground = System.Windows.Media.Brushes.Crimson;
                }
            });
            Console.WriteLine($"Error loading all hardware: {ex.Message}");
        }
    }

    private void ApplyHardwareFilter()
    {
        _hardwareRows.Clear();
        var q = _hardwareSearchQuery.Trim().ToLowerInvariant();
        var filtered = string.IsNullOrEmpty(q)
            ? _allHardwareMasterList
            : _allHardwareMasterList.Where(h =>
                (h.PcName?.ToLowerInvariant().Contains(q) ?? false) ||
                (h.Cpu?.ToLowerInvariant().Contains(q) ?? false) ||
                (h.Gpu?.ToLowerInvariant().Contains(q) ?? false) ||
                (h.Ram?.ToLowerInvariant().Contains(q) ?? false) ||
                (h.Storage?.ToLowerInvariant().Contains(q) ?? false) ||
                (h.Motherboard?.ToLowerInvariant().Contains(q) ?? false) ||
                (h.Lan?.ToLowerInvariant().Contains(q) ?? false) ||
                (h.Monitor?.ToLowerInvariant().Contains(q) ?? false) ||
                (h.Note?.ToLowerInvariant().Contains(q) ?? false)
            ).ToList();

        foreach (var item in filtered)
        {
            _hardwareRows.Add(item);
        }

        if (HardwareDataGrid != null && HardwareDataGrid.ItemsSource != _hardwareRows)
        {
            HardwareDataGrid.ItemsSource = _hardwareRows;
        }
    }

    private void UpdateHardwareSummaryKpis()
    {
        try
        {
            int total = _allHardwareMasterList.Count;
            int synced = _allHardwareMasterList.Count(h => h.IsSynced);

            if (HardwareSyncedCountTextBlock != null)
            {
                HardwareSyncedCountTextBlock.Text = $"{synced}/{total} máy";
            }

            if (HardwareSyncedPercentTextBlock != null)
            {
                double pct = total > 0 ? (synced * 100.0 / total) : 0;
                HardwareSyncedPercentTextBlock.Text = $"{pct:0.#}% hoàn thành";
            }

            var syncedItems = _allHardwareMasterList.Where(h => h.IsSynced).ToList();
            if (syncedItems.Count > 0)
            {
                var topCpu = syncedItems
                    .GroupBy(h => h.Cpu)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "—";

                var topRam = syncedItems
                    .GroupBy(h => h.Ram)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "—";

                string shortCpu = topCpu.Replace("12th Gen Intel(R) Core(TM)", "Intel Core")
                                        .Replace("Intel(R) Core(TM)", "Intel")
                                        .Replace("AMD Ryzen", "Ryzen")
                                        .Trim();
                if (shortCpu.Length > 24) shortCpu = shortCpu.Substring(0, 24) + "...";

                if (HardwareCpuRamSummaryTextBlock != null)
                {
                    HardwareCpuRamSummaryTextBlock.Text = $"{shortCpu} · {topRam}";
                }

                var topGpu = syncedItems
                    .GroupBy(h => h.Gpu)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "—";

                string shortGpu = topGpu.Replace("NVIDIA GeForce", "NVIDIA").Trim();
                if (shortGpu.Length > 25) shortGpu = shortGpu.Substring(0, 25) + "...";

                if (HardwareGpuSummaryTextBlock != null)
                {
                    HardwareGpuSummaryTextBlock.Text = shortGpu;
                }

                var topLan = syncedItems
                    .Select(h => h.Lan)
                    .FirstOrDefault(l => !string.IsNullOrEmpty(l)) ?? "Gigabit LAN";

                if (HardwareNetworkSummaryTextBlock != null)
                {
                    string shortLan = topLan.Length > 24 ? topLan.Substring(0, 24) + "..." : topLan;
                    HardwareNetworkSummaryTextBlock.Text = shortLan;
                }
            }
            else
            {
                if (HardwareCpuRamSummaryTextBlock != null) HardwareCpuRamSummaryTextBlock.Text = "Chờ đồng bộ";
                if (HardwareGpuSummaryTextBlock != null) HardwareGpuSummaryTextBlock.Text = "Chờ đồng bộ";
                if (HardwareNetworkSummaryTextBlock != null) HardwareNetworkSummaryTextBlock.Text = "Chờ đồng bộ";
            }
        }
        catch { }
    }

    private void HardwareSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _hardwareSearchQuery = tb.Text ?? string.Empty;
            ApplyHardwareFilter();
        }
    }

    private async void ScanAllHardwareButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (HardwareStatusTextBlock != null)
            {
                HardwareStatusTextBlock.Text = "⚡ Đang gửi lệnh quét...";
                HardwareStatusTextBlock.Foreground = System.Windows.Media.Brushes.DodgerBlue;
            }

            await SendCommandAsync("SCAN_HARDWARE");

            if (HardwareStatusTextBlock != null)
            {
                HardwareStatusTextBlock.Text = "Đã gửi lệnh quét! Đang chờ kết quả...";
                HardwareStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
            }

            _ = Task.Delay(3000).ContinueWith(_ => RefreshAllHardwareAsync());
        }
        catch (Exception ex)
        {
            if (HardwareStatusTextBlock != null)
            {
                HardwareStatusTextBlock.Text = "Lỗi gửi lệnh: " + ex.Message;
                HardwareStatusTextBlock.Foreground = System.Windows.Media.Brushes.Crimson;
            }
        }
    }

    private async void SaveAllHardwareButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HardwareStatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
            HardwareStatusTextBlock.Text = "Đang lưu...";
            int savedCount = 0;
            
            foreach (var row in _hardwareRows)
            {
                if (row.IsModified)
                {
                    var dto = new
                    {
                        cpu = row.Cpu,
                        ram = row.Ram,
                        gpu = row.Gpu,
                        motherboard = row.Motherboard,
                        storage = row.Storage,
                        monitor = row.Monitor,
                        note = row.Note
                    };

                    var response = await _httpClient.PutAsJsonAsync(BuildApiUrl($"/pcs/{row.PcId}/hardware"), dto, JsonOptions());
                    if (response.IsSuccessStatusCode)
                    {
                        row.IsModified = false;
                        savedCount++;
                    }
                }
            }
            
            HardwareStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
            HardwareStatusTextBlock.Text = $"Đã lưu {savedCount} máy.";
            
            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => { if (HardwareStatusTextBlock.Text.StartsWith("Đã lưu")) HardwareStatusTextBlock.Text = ""; });
            });
        }
        catch (Exception ex)
        {
            HardwareStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            HardwareStatusTextBlock.Text = "Lỗi khi lưu";
            MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RefreshHardwareButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllHardwareAsync();
    }
}
