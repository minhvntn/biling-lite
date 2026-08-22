using System;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Client.Agent.Wpf;

public partial class InGameOverlayWindow : Window
{
    private readonly MainWindow _mainWindow;

    public InGameOverlayWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        this.Loaded += InGameOverlayWindow_Loaded;
    }

    private void InGameOverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Position at top right
        var desktopWorkingArea = SystemParameters.WorkArea;
        this.Left = desktopWorkingArea.Right - this.Width - 20;
        this.Top = desktopWorkingArea.Top + 20;
    }

    public void UpdateData(string memberName, string balance, string timeRemaining, bool isPostpaid)
    {
        MemberNameTextBlock.Text = string.IsNullOrEmpty(memberName) ? "Khách vãng lai" : memberName;
        BalanceTextBlock.Text = balance;
        TimeTextBlock.Text = timeRemaining;
        BalanceLabelTextBlock.Text = isPostpaid ? "TIỀN ĐÃ CHƠI" : "SỐ DƯ";
    }

    private void ServiceButton_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
        if (Application.Current is App app)
        {
            app.OpenServicesPanelFromClientUi();
        }
    }

    private void ChatButton_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
        if (Application.Current is App app)
        {
            var socketService = app.GetSocketService();
            if (socketService != null)
            {
                var existingChat = Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault();
                if (existingChat != null)
                {
                    if (existingChat.WindowState == WindowState.Minimized)
                    {
                        existingChat.WindowState = WindowState.Normal;
                    }
                    existingChat.Activate();
                    existingChat.Topmost = true;
                    return;
                }

                var dialog = new ChatWindow(app, socketService);
                dialog.Owner = _mainWindow;
                dialog.Topmost = true;
                dialog.Show();
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
    }
    
    private void ManageAppsButton_Click(object sender, RoutedEventArgs e)
    {
        this.Hide(); // Optionally hide overlay when managing apps
        var runningAppsWindow = new RunningAppsWindow();
        runningAppsWindow.ShowDialog();
    }
    
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        
        // WS_EX_NOACTIVATE ensures the window doesn't steal focus from the game when it appears.
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
    }
    
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}