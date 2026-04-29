using System.ComponentModel;
using System.Windows;

namespace Client.Agent.Wpf;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void SetAgentId(string agentId)
    {
        AgentIdTextBlock.Text = agentId;
    }

    public void SetConnectionStatus(string status)
    {
        ConnectionStatusTextBlock.Text = status;
    }

    public void SetMachineState(string state)
    {
        MachineStateTextBlock.Text = state;
    }

    public void SetLastCommand(string command)
    {
        LastCommandTextBlock.Text = command;
    }

    public void AllowShutdown()
    {
        _allowClose = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        // Basic anti-close behavior for Phase 5: hide instead of closing.
        e.Cancel = true;
        Hide();
    }
}
