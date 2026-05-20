using System.Windows;

namespace Server.Admin.App;

public partial class StartupLoadingWindow : Window
{
    public StartupLoadingWindow()
    {
        InitializeComponent();
    }

    public void UpdateStatus(string? message, int progressPercent)
    {
        var percent = Math.Clamp(progressPercent, 0, 100);
        StartupProgressBar.Value = percent;
        if (!string.IsNullOrWhiteSpace(message))
        {
            StartupStatusTextBlock.Text = message.Trim();
        }
    }
}
