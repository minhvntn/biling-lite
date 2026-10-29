using System.Windows;
using Microsoft.Win32;

namespace Server.Admin.App
{
    public static class WindowExtensions
    {
        public static bool? ShowModal(this Window dialog, Window? owner = null)
        {
            owner ??= Application.Current.MainWindow;
            
            if (owner is MainWindow mainWindow && mainWindow.GlobalModalOverlay != null)
            {
                try
                {
                    mainWindow.GlobalModalOverlay.Visibility = Visibility.Visible;
                    dialog.Owner = owner;
                    return dialog.ShowDialog();
                }
                finally
                {
                    mainWindow.GlobalModalOverlay.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                if (owner != null && dialog != owner) dialog.Owner = owner;
                return dialog.ShowDialog();
            }
        }
    }
}
