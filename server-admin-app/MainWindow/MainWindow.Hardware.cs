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
    private string _note = string.Empty;

    public string PcId { get; set; } = string.Empty;
    public string PcName { get; set; } = string.Empty;

    public string Cpu
    {
        get => _cpu;
        set { if (_cpu != value) { _cpu = value; IsModified = true; OnPropertyChanged(nameof(Cpu)); } }
    }
    public string Ram
    {
        get => _ram;
        set { if (_ram != value) { _ram = value; IsModified = true; OnPropertyChanged(nameof(Ram)); } }
    }
    public string Gpu
    {
        get => _gpu;
        set { if (_gpu != value) { _gpu = value; IsModified = true; OnPropertyChanged(nameof(Gpu)); } }
    }
    public string Motherboard
    {
        get => _motherboard;
        set { if (_motherboard != value) { _motherboard = value; IsModified = true; OnPropertyChanged(nameof(Motherboard)); } }
    }
    public string Storage
    {
        get => _storage;
        set { if (_storage != value) { _storage = value; IsModified = true; OnPropertyChanged(nameof(Storage)); } }
    }
    public string Monitor
    {
        get => _monitor;
        set { if (_monitor != value) { _monitor = value; IsModified = true; OnPropertyChanged(nameof(Monitor)); } }
    }
    public string Note
    {
        get => _note;
        set { if (_note != value) { _note = value; IsModified = true; OnPropertyChanged(nameof(Note)); } }
    }

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

    private async Task RefreshAllHardwareAsync()
    {
        try
        {
            Dispatcher.Invoke(() => {
                HardwareStatusTextBlock.Text = "Đang tải dữ liệu...";
                HardwareStatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
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
                        Note = hw?.Note ?? string.Empty,
                        IsModified = false
                    };
                    items.Add(row);
                }

                Dispatcher.Invoke(() =>
                {
                    _hardwareRows.Clear();
                    foreach (var item in items)
                    {
                        _hardwareRows.Add(item);
                    }
                    HardwareDataGrid.ItemsSource = _hardwareRows;
                    HardwareStatusTextBlock.Text = "";
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => { HardwareStatusTextBlock.Text = "Lỗi tải dữ liệu"; });
            Console.WriteLine($"Error loading all hardware: {ex.Message}");
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
            
            // clear success text after 3 seconds
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


}
