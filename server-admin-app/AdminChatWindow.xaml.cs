using System;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Server.Admin.App;

public partial class AdminChatWindow : Window
{
    private readonly MainWindow _parent;
    private readonly string _pcId;
    private readonly string _pcName;
    private readonly ObservableCollection<Models.SupportMessageDto> _messages = new();
    private readonly DispatcherTimer _pollTimer;

    public AdminChatWindow(MainWindow parent, string pcId, string pcName)
    {
        InitializeComponent();
        _parent = parent;
        _pcId = pcId;
        _pcName = pcName;
        
        TitleTextBlock.Text = $"Trò chuyện với máy: {pcName}";
        MessagesItemsControl.ItemsSource = _messages;

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _pollTimer.Tick += async (s, e) => await LoadMessagesAsync();
        
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadMessagesAsync();
        _pollTimer.Start();
    }

    private async Task LoadMessagesAsync()
    {
        try
        {
            var url = _parent.BuildApiUrl($"/pcs/{_pcId}/support-messages");
            var msgs = await _parent._httpClient.GetFromJsonAsync<System.Collections.Generic.List<Models.SupportMessageDto>>(url);
            if (msgs != null)
            {
                Dispatcher.Invoke(() =>
                {
                    _messages.Clear();
                    msgs.Reverse(); // backend is desc, we want asc
                    foreach (var m in msgs)
                    {
                        m.SentBy = m.SentBy ?? "Admin"; // Fallback for display
                        _messages.Add(m);
                    }
                    // Auto scroll down if not actively scrolling up
                    MessagesScrollViewer.ScrollToEnd();
                });
            }
        }
        catch
        {
            // Ignore polling errors
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
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
        _messages.Add(new Models.SupportMessageDto
        {
            Direction = "ADMIN",
            Message = text,
            CreatedAt = DateTime.UtcNow
        });
        MessagesScrollViewer.ScrollToEnd();

        try
        {
            var payload = new { message = text };
            var content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            await _parent._httpClient.PostAsync(_parent.BuildApiUrl($"/pcs/{_pcId}/reply-support"), content);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Lỗi gửi tin: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _pollTimer.Stop();
    }
}
