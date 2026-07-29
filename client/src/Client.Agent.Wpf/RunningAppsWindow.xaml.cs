using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Client.Agent.Wpf.Services;

namespace Client.Agent.Wpf
{
    public partial class RunningAppsWindow : Window
    {
        public RunningAppsWindow()
        {
            InitializeComponent();
            LoadRunningApps();
        }

        private void LoadRunningApps()
        {
            var apps = AppUsageTracker.GetRunningApps();
            AppsListBox.ItemsSource = apps;
        }

        private void CloseAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int processId)
            {
                try
                {
                    var proc = Process.GetProcessById(processId);
                    if (proc != null && !proc.HasExited)
                    {
                        proc.Kill();
                        MessageBox.Show(this, $"Đã đóng ứng dụng: {proc.MainWindowTitle}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Không thể đóng ứng dụng này: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                
                // Refresh list
                LoadRunningApps();
            }
        }
    }
}
