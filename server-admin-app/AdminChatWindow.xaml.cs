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
    public string PcId => _pcId;
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
                    msgs.Reverse(); // backend is desc, we want asc

                    // Remove optimistic messages that don't have an ID yet
                    var toRemove = new System.Collections.Generic.List<Models.SupportMessageDto>();
                    foreach (var m in _messages)
                    {
                        if (string.IsNullOrEmpty(m.Id))
                        {
                            toRemove.Add(m);
                        }
                    }
                    foreach (var r in toRemove)
                    {
                        _messages.Remove(r);
                    }

                    bool added = false;
                    foreach (var m in msgs)
                    {
                        m.SentBy = m.SentBy ?? "Admin"; // Fallback for display
                        
                        // Only add if not already in the list
                        bool exists = false;
                        foreach (var existing in _messages)
                        {
                            if (existing.Id == m.Id)
                            {
                                exists = true;
                                break;
                            }
                        }

                        if (!exists)
                        {
                            _messages.Add(m);
                            added = true;
                        }
                    }

                    if (added)
                    {
                        // Auto scroll down only when new messages are added
                        MessagesScrollViewer.ScrollToEnd();
                    }
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

    public async Task ReloadMessagesAsync()
    {
        await LoadMessagesAsync();
    }

    public void ReceiveIncomingMessage(string? id, string text, string? createdAtStr)
    {
        Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrEmpty(id))
            {
                foreach (var existing in _messages)
                {
                    if (existing.Id == id)
                    {
                        return; // Already added
                    }
                }
            }

            var createdAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(createdAtStr) && DateTime.TryParse(createdAtStr, out var parsed))
            {
                createdAt = parsed.ToUniversalTime();
            }

            _messages.Add(new Models.SupportMessageDto
            {
                Id = id ?? string.Empty,
                PcId = _pcId,
                PcName = _pcName,
                Direction = "CLIENT",
                Message = text,
                SentBy = _pcName,
                CreatedAt = createdAt
            });

            MessagesScrollViewer.ScrollToEnd();
        });

        // Background sync to ensure all messages are in sync
        _ = LoadMessagesAsync();
    }

    public void ClearMessages()
    {
        Dispatcher.Invoke(() =>
        {
            _messages.Clear();
        });
    }

    private async void ResetChatButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            $"Bạn có chắc muốn xóa toàn bộ tin nhắn hỗ trợ của {_pcName} và bắt đầu phiên chat mới?",
            "Xác nhận phiên mới",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var res = await _parent._httpClient.DeleteAsync(_parent.BuildApiUrl($"/pcs/{_pcId}/support-messages"));
            if (res.IsSuccessStatusCode)
            {
                ClearMessages();
            }
            else
            {
                MessageBox.Show(this, "Không thể xóa lịch sử chat.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Lỗi xóa lịch sử chat: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _pollTimer.Stop();
        if (string.Equals(_parent._lastChatNotificationPcId, _pcId, StringComparison.OrdinalIgnoreCase))
        {
            _parent.ChatNotificationPopupBorder.Visibility = Visibility.Collapsed;
        }
    }
}
