using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Server.Admin.App.Windows;

public partial class ConnectionErrorDialog : Window
{
    private readonly Func<Task>? _onRepairSuccess;

    public ConnectionErrorDialog(string errorMessage, Func<Task>? onRepairSuccess = null)
    {
        InitializeComponent();
        ErrorMessageTextBlock.Text = errorMessage;
        _onRepairSuccess = onRepairSuccess;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void AutoRepairButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();

        var mainWindow = Application.Current.MainWindow as MainWindow;
        if (mainWindow == null) return;

        // Mở cửa sổ tự động sửa chữa hệ thống
        var diagWindow = new SystemHealthCheckWindow(mainWindow, autoStartRepair: true)
        {
            Owner = mainWindow
        };
        diagWindow.ShowDialog();

        // Nếu có hàm callback sau khi sửa chữa xong, tự động gọi lại để tải mới dữ liệu
        if (_onRepairSuccess != null)
        {
            try
            {
                await _onRepairSuccess();
            }
            catch
            {
                // Bỏ qua lỗi phụ nếu đang tải lại
            }
        }
    }

    public static void Show(Window? owner, string actionName, Exception ex, Func<Task>? onRepairSuccess = null)
    {
        var errorMsg = $"{actionName}: {ex.Message}";
        var dialog = new ConnectionErrorDialog(errorMsg, onRepairSuccess);
        if (owner != null)
        {
            dialog.Owner = owner;
        }
        else if (Application.Current.MainWindow != null)
        {
            dialog.Owner = Application.Current.MainWindow;
        }
        dialog.ShowDialog();
    }
}

