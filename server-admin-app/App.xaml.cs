using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Server.Admin.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Workaround for GPU driver/DWM artifacts where dialog content can appear duplicated.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);

        var loadingWindow = new StartupLoadingWindow();
        loadingWindow.Show();

        try
        {
            var mainWindow = new MainWindow
            {
                ShowInTaskbar = true,
            };

            mainWindow.StartupProgressChanged += (_, args) =>
            {
                loadingWindow.UpdateStatus(args.Message, args.ProgressPercent);
            };
            mainWindow.StartupInitializationCompleted += (_, success) =>
            {
                loadingWindow.Close();
                if (success)
                {
                    mainWindow.Activate();
                }
            };

            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (System.Exception ex)
        {
            loadingWindow.Close();
            MessageBox.Show(
                $"Không thể khởi động Server Admin: {ex.Message}",
                "Server Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
