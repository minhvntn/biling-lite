using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace Server.Admin.App.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null && VersionTextBlock != null)
            {
                VersionTextBlock.Text = $"Phiên bản {version.Major}.{version.Minor}.{version.Build} (Build 2026.10)";
            }
        }
        catch
        {
            // fallback to default xaml text
        }
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
        Close();
    }

    private void CopyInfoButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var versionStr = VersionTextBlock?.Text ?? "2.6.0";
            var info = $"CYBERS ADMIN - HỆ THỐNG QUẢN LÝ PHÒNG MÁY\n" +
                       $"{versionStr}\n" +
                       $"Nền tảng: .NET 10.0 (WPF x64 - Native Desktop)\n" +
                       $"Máy chủ Backend: NestJS Engine + Prisma ORM\n" +
                       $"Cơ sở dữ liệu: PostgreSQL 16\n" +
                       $"Bản quyền: © 2026 CyberS Team. All rights reserved.";
            Clipboard.SetText(info);

            if (CopyButtonTextBlock != null)
            {
                CopyButtonTextBlock.Text = "Đã sao chép!";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể sao chép: {ex.Message}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

