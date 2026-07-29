using System;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using Client.Agent.Wpf.Services;

namespace Client.Agent.Wpf;

public partial class ChatWindow : Window
{
    private readonly App _app;
    private readonly AgentSocketService _socketService;
    private readonly ObservableCollection<ChatMessage> _messages = new();

    public class ChatMessage
    {
        public string Direction { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string SentAtString => CreatedAt.ToLocalTime().ToString("dd/MM HH:mm");
    }

    public ChatWindow(App app, AgentSocketService socketService)
    {
        InitializeComponent();
        _app = app;
        _socketService = socketService;
        MessagesItemsControl.ItemsSource = _messages;

        // Register event handler for incoming messages
        _socketService.SupportReplyHandler += OnSupportReplyReceived;

        _ = LoadMessagesAsync();
    }

    private async Task LoadMessagesAsync()
    {
        try
        {
            var url = _app.BuildApiUrl($"/pcs/agent/{_app.Settings.AgentId}/support-messages");
            var msgs = await _app.HttpClient.GetFromJsonAsync<System.Collections.Generic.List<ChatMessage>>(url);
            if (msgs != null)
            {
                Dispatcher.Invoke(() =>
                {
                    _messages.Clear();
                    // messages are returned in desc order from backend, so reverse them
                    msgs.Reverse();
                    foreach (var m in msgs)
                    {
                        _messages.Add(m);
                    }
                    ScrollToBottom();
                });
            }
        }
        catch
        {
            // ignore
        }
    }

    private void OnSupportReplyReceived(string message, string repliedBy, string sentAt)
    {
        Dispatcher.Invoke(() =>
        {
            DateTime.TryParse(sentAt, out var dt);
            _messages.Add(new ChatMessage
            {
                Direction = "ADMIN",
                Message = message,
                CreatedAt = dt == default ? DateTime.UtcNow : dt
            });
            ScrollToBottom();
        });
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async void InputTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && !System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftShift) && !System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightShift))
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        var text = InputTextBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        InputTextBox.Text = "";
        
        // Optimistically add to UI
        _messages.Add(new ChatMessage
        {
            Direction = "CLIENT",
            Message = text,
            CreatedAt = DateTime.UtcNow
        });
        ScrollToBottom();

        // Emit
        await _socketService.EmitSupportRequestAsync(text, "GENERAL");
    }

    private void ScrollToBottom()
    {
        MessagesScrollViewer.ScrollToEnd();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _socketService.SupportReplyHandler -= OnSupportReplyReceived;
    }
}
