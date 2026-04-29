using System.ComponentModel;
using System.Windows;

namespace Client.Agent.Wpf;

public partial class LockScreenWindow : Window
{
    public LockScreenWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Prevent users from closing the lock overlay directly.
        e.Cancel = true;
        Hide();
    }
}
