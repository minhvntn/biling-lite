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
    }
}
