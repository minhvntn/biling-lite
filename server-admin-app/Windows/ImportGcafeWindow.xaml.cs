using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Server.Admin.App.Windows;

public partial class ImportGcafeWindow : Window
{
    private readonly MainWindow? _mainWindow;
    private readonly string _filePath;
    private bool _isCompleted;

    public string SelectedMode { get; private set; } = "merge"; // "merge" | "replace"
    public bool IsSuccess { get; private set; }

    public ImportGcafeWindow(string filePath) : this(Application.Current.MainWindow as MainWindow, filePath)
    {
    }

    public ImportGcafeWindow(MainWindow? mainWindow, string filePath)
    {
        InitializeComponent();
        _mainWindow = mainWindow ?? (Application.Current.MainWindow as MainWindow);
        _filePath = filePath;

        if (File.Exists(filePath))
        {
            FileNameTextBlock.Text = Path.GetFileName(filePath);
            FilePathTextBlock.Text = filePath;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MergeOption_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isCompleted) return;
        MergeRadioButton.IsChecked = true;
        ReplaceRadioButton.IsChecked = false;
        UpdateVisualMode();
    }

    private void ReplaceOption_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isCompleted) return;
        ReplaceRadioButton.IsChecked = true;
        MergeRadioButton.IsChecked = false;
        UpdateVisualMode();
    }

    private void ModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender == ReplaceRadioButton)
        {
            if (MergeRadioButton != null) MergeRadioButton.IsChecked = false;
        }
        else if (sender == MergeRadioButton)
        {
            if (ReplaceRadioButton != null) ReplaceRadioButton.IsChecked = false;
        }
        UpdateVisualMode();
    }

    private void UpdateVisualMode()
    {
        if (MergeRadioButton == null || ReplaceRadioButton == null) return;

        if (ReplaceRadioButton.IsChecked == true)
        {
            SelectedMode = "replace";
            ReplaceOptionBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
            ReplaceOptionBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            ReplaceOptionBorder.BorderThickness = new Thickness(1.5);

            MergeOptionBorder.Background = Brushes.White;
            MergeOptionBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
            MergeOptionBorder.BorderThickness = new Thickness(1);
        }
        else
        {
            SelectedMode = "merge";
            MergeOptionBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAF5FF"));
            MergeOptionBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));
            MergeOptionBorder.BorderThickness = new Thickness(1.5);

            ReplaceOptionBorder.Background = Brushes.White;
            ReplaceOptionBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
            ReplaceOptionBorder.BorderThickness = new Thickness(1);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (LoadingPanel.Visibility == Visibility.Visible)
        {
            // Không cho phép hủy khi đang ghi dữ liệu vào DB
            return;
        }

        DialogResult = _isCompleted;
        Close();
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted)
        {
            DialogResult = true;
            Close();
            return;
        }

        if (SelectedMode == "replace")
        {
            var confirm = MessageBox.Show(
                "⚠️ CẢNH BÁO QUAN TRỌNG:\n\n" +
                "Bạn đã chọn chế độ: XÓA TẤT CẢ VÀ GHI LẠI!\n" +
                "Toàn bộ hội viên và lịch sử giao dịch cũ trên máy chủ sẽ bị xóa sạch hoàn toàn để nhập mới từ file GCafe.\n\n" +
                "Hành động này không thể hoàn tác! Bạn có chắc chắn 100% muốn tiếp tục?",
                "Xác nhận xóa tất cả và ghi lại",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (_mainWindow == null || !File.Exists(_filePath))
        {
            MessageBox.Show("Không tìm thấy tệp hoặc thông tin kết nối máy chủ.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Chuyển sang giao diện Loading
        OptionsPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        SuccessPanel.Visibility = Visibility.Collapsed;

        SubmitButton.IsEnabled = false;
        SubmitButton.Content = "⏳ Đang nhập dữ liệu...";
        CancelButton.IsEnabled = false;
        TitleCloseButton.IsEnabled = false;

        try
        {
            using var form = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(_filePath);
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(streamContent, "file", Path.GetFileName(_filePath));
            form.Add(new StringContent(_filePath), "filePath");
            form.Add(new StringContent(SelectedMode), "mode");

            var url = _mainWindow.BuildApiUrl("/members/import-gcafe");
            var response = await _mainWindow._httpClient.PostAsync(url, form);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Trả lại giao diện Options để thử lại
                LoadingPanel.Visibility = Visibility.Collapsed;
                OptionsPanel.Visibility = Visibility.Visible;
                SubmitButton.IsEnabled = true;
                SubmitButton.Content = "📥 Bắt đầu nhập dữ liệu";
                CancelButton.IsEnabled = true;
                TitleCloseButton.IsEnabled = true;

                MessageBox.Show($"Nhập dữ liệu thất bại (Mã {(int)response.StatusCode}):\n{json}", "Lỗi Import GCafe", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var message = root.TryGetProperty("message", out var msgProp) 
                ? msgProp.GetString() 
                : "Đã nhập thành công!";

            // Chuyển sang giao diện Success
            LoadingPanel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Visible;
            SuccessDetailTextBlock.Text = message;

            _isCompleted = true;
            IsSuccess = true;
            SubmitButton.Content = "✓ Hoàn tất";
            SubmitButton.IsEnabled = true;
            SubmitButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            CancelButton.Visibility = Visibility.Collapsed;
            TitleCloseButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            OptionsPanel.Visibility = Visibility.Visible;
            SubmitButton.IsEnabled = true;
            SubmitButton.Content = "📥 Bắt đầu nhập dữ liệu";
            CancelButton.IsEnabled = true;
            TitleCloseButton.IsEnabled = true;

            MessageBox.Show($"Có lỗi xảy ra trong quá trình nhập GCafe:\n{ex.Message}", "Lỗi ngoại lệ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
