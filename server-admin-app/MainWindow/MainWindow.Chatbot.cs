using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private ChatbotConfigModel _chatbotConfig = new();
    private readonly HttpClient _geminiHttpClient = new(new HttpClientHandler
    {
        UseProxy = true,
        DefaultProxyCredentials = System.Net.CredentialCache.DefaultCredentials
    }) { Timeout = TimeSpan.FromSeconds(60) };
    private bool _isGeminiWaitingResponse;

    public sealed class ChatbotConfigModel
    {
        public bool IsEnabled { get; set; } = true;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = "gemini-3.8-flash";
        public string AssistantName { get; set; } = "Ami";
        public bool AllowPcContext { get; set; } = true;
        public string TooltipGreeting { get; set; } = "Anh ơi, có Ami ở đây nè! ✨";
    }

    private bool _chatbotWelcomeAdded;
    private void EnsureChatbotWelcomeMessage()
    {
        if (_chatbotWelcomeAdded) return;
        _chatbotWelcomeAdded = true;
        AddAmiMessageToChat(
            $"Em chào Anh ạ! Em là **{_chatbotConfig.AssistantName}**, trợ lý AI của hệ thống Server Manager Billing. " +
            "Anh cần em kiểm tra tình hình phòng máy, soi máy trạm hay trò chuyện gì cứ nhắn em nhé! ✨💖"
        );
    }

    private void InitializeChatbot()
    {
        LoadChatbotSettings();
        ApplyChatbotConfigToUi();
        UpdateChatbotWidgetVisibility();

        if (!_settings.IsLiteMode)
        {
            EnsureChatbotWelcomeMessage();
        }
    }

    private string GetChatbotConfigFilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "chatbot-config.json");
    }

    private void LoadChatbotSettings()
    {
        try
        {
            var path = GetChatbotConfigFilePath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<ChatbotConfigModel>(json);
                if (loaded != null)
                {
                    _chatbotConfig = loaded;
                    return;
                }
            }
        }
        catch
        {
            // Fallback sang cấu hình mặc định
        }

        _chatbotConfig = new ChatbotConfigModel();
    }

    private void SaveChatbotSettings()
    {
        try
        {
            var path = GetChatbotConfigFilePath();
            var json = JsonSerializer.Serialize(_chatbotConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch
        {
            // Bỏ qua lỗi nếu không có quyền ghi đường dẫn
        }
    }

    private void ApplyChatbotConfigToUi()
    {
        if (ChatbotEnableCheckBox != null)
        {
            ChatbotEnableCheckBox.IsChecked = _chatbotConfig.IsEnabled;
        }

        if (ChatbotApiKeyTextBox != null)
        {
            ChatbotApiKeyTextBox.Text = _chatbotConfig.ApiKey;
        }

        if (ChatbotAssistantNameTextBox != null)
        {
            ChatbotAssistantNameTextBox.Text = string.IsNullOrWhiteSpace(_chatbotConfig.AssistantName) ? "Ami" : _chatbotConfig.AssistantName;
        }

        if (ChatbotAllowPcContextCheckBox != null)
        {
            ChatbotAllowPcContextCheckBox.IsChecked = _chatbotConfig.AllowPcContext;
        }

        if (ChatbotModelComboBox != null)
        {
            bool found = false;
            foreach (var obj in ChatbotModelComboBox.Items)
            {
                if (obj is ComboBoxItem item && string.Equals(item.Tag?.ToString(), _chatbotConfig.ModelName, StringComparison.OrdinalIgnoreCase))
                {
                    ChatbotModelComboBox.SelectedItem = item;
                    found = true;
                    break;
                }
            }
            if (!found && !string.IsNullOrWhiteSpace(_chatbotConfig.ModelName))
            {
                ChatbotModelComboBox.Text = _chatbotConfig.ModelName;
            }
        }

        if (ChatbotHeaderNameText != null)
        {
            ChatbotHeaderNameText.Text = $"{_chatbotConfig.AssistantName} 🌸";
        }
    }

    private void UpdateChatbotWidgetVisibility()
    {
        // Trong Bản Lite: Luôn luôn tắt và ẩn hoàn toàn chức năng Chatbot AI
        var shouldShow = !_settings.IsLiteMode && _chatbotConfig.IsEnabled;

        if (ChatbotLauncherContainer != null)
        {
            ChatbotLauncherContainer.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!shouldShow && ChatbotModalBorder != null)
        {
            ChatbotModalBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void ChatbotEnableCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _chatbotConfig.IsEnabled = ChatbotEnableCheckBox?.IsChecked ?? true;
        UpdateChatbotWidgetVisibility();
    }

    private void SaveChatbotSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _chatbotConfig.IsEnabled = ChatbotEnableCheckBox?.IsChecked ?? true;
        _chatbotConfig.ApiKey = ChatbotApiKeyTextBox?.Text?.Trim() ?? string.Empty;
        _chatbotConfig.AssistantName = string.IsNullOrWhiteSpace(ChatbotAssistantNameTextBox?.Text?.Trim()) ? "Ami" : ChatbotAssistantNameTextBox.Text.Trim();
        _chatbotConfig.AllowPcContext = ChatbotAllowPcContextCheckBox?.IsChecked ?? true;

        _chatbotConfig.ModelName = GetSelectedModelName();

        SaveChatbotSettings();
        ApplyChatbotConfigToUi();
        UpdateChatbotWidgetVisibility();

        if (ChatbotSettingsStatusTextBlock != null)
        {
            ChatbotSettingsStatusTextBlock.Text = $"✅ Đã lưu cài đặt Chatbot AI thành công lúc {DateTime.Now:HH:mm:ss}!";
            ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
        }
    }

    private void ResetChatbotSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _chatbotConfig = new ChatbotConfigModel();
        SaveChatbotSettings();
        ApplyChatbotConfigToUi();
        UpdateChatbotWidgetVisibility();

        if (ChatbotSettingsStatusTextBlock != null)
        {
            ChatbotSettingsStatusTextBlock.Text = $"🔄 Đã khôi phục cài đặt Chatbot về mặc định lúc {DateTime.Now:HH:mm:ss}.";
            ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        }
    }

    private async void TestGeminiApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ChatbotApiKeyTextBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (ChatbotSettingsStatusTextBlock != null)
            {
                ChatbotSettingsStatusTextBlock.Text = "⚠️ Vui lòng nhập Gemini API Key trước khi kiểm tra.";
                ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
            }
            return;
        }

        var model = GetSelectedModelName();

        if (ChatbotSettingsStatusTextBlock != null)
        {
            ChatbotSettingsStatusTextBlock.Text = "⏳ Đang kết nối tới máy chủ Google Gemini để kiểm tra...";
            ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = "Xin chào, hãy trả lời đúng 2 từ: Kết nối OK" } }
                    }
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _geminiHttpClient.PostAsync(url, jsonContent);

            if (response.IsSuccessStatusCode)
            {
                var respJson = await response.Content.ReadAsStringAsync();
                if (ChatbotSettingsStatusTextBlock != null)
                {
                    ChatbotSettingsStatusTextBlock.Text = $"🎉 Kết nối Gemini API thành công! Mô hình ({model}) phản hồi tốt.";
                    ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                }
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                if (ChatbotSettingsStatusTextBlock != null)
                {
                    ChatbotSettingsStatusTextBlock.Text = FormatGeminiError((int)response.StatusCode, errorText);
                    ChatbotSettingsStatusTextBlock.Foreground = (int)response.StatusCode == 429
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                }
            }
        }
        catch (Exception ex)
        {
            if (ChatbotSettingsStatusTextBlock != null)
            {
                ChatbotSettingsStatusTextBlock.Text = $"❌ Không thể kết nối tới Google AI: {ex.Message}";
                ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            }
        }
    }


    private void OpenGeminiAiStudioLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://aistudio.google.com/app/apikey",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch errors
        }
    }

    private void OpenChatbotFromSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ChatbotLauncherContainer != null)
        {
            ChatbotLauncherContainer.Visibility = Visibility.Visible;
        }
        if (ChatbotModalBorder != null)
        {
            ChatbotModalBorder.Visibility = Visibility.Visible;
        }
        if (ChatbotInputTextBox != null)
        {
            ChatbotInputTextBox.Focus();
        }
    }

    // ==========================================
    // CHATBOT WIDGET ACTIONS
    // ==========================================
    private void ChatbotLauncher_Click(object sender, MouseButtonEventArgs e)
    {
        ToggleChatbotModal();
    }

    private void ToggleChatbotModal()
    {
        if (ChatbotModalBorder == null) return;

        if (_settings.IsLiteMode)
        {
            ChatbotModalBorder.Visibility = Visibility.Collapsed;
            return;
        }

        if (ChatbotModalBorder.Visibility == Visibility.Visible)
        {
            ChatbotModalBorder.Visibility = Visibility.Collapsed;
        }
        else
        {
            EnsureChatbotWelcomeMessage();
            ChatbotModalBorder.Visibility = Visibility.Visible;
            if (ChatbotInputTextBox != null)
            {
                ChatbotInputTextBox.Focus();
            }
            ScrollChatToBottom();
        }
    }

    private void ChatbotCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChatbotModalBorder != null)
        {
            ChatbotModalBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void ChatbotClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChatbotMessagesPanel == null) return;
        ChatbotMessagesPanel.Children.Clear();

        AddAmiMessageToChat(
            $"Em đã xóa toàn bộ lịch sử trò chuyện rồi ạ! Anh cần **{_chatbotConfig.AssistantName}** hỗ trợ gì cứ nhắn em nhé ✨💖"
        );
    }

    private void ChatbotQuickPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string prompt)
        {
            if (ChatbotInputTextBox != null)
            {
                ChatbotInputTextBox.Text = prompt;
            }
            SubmitChatbotUserMessage();
        }
    }

    private void ChatbotInputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SubmitChatbotUserMessage();
        }
    }

    private void ChatbotSendButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitChatbotUserMessage();
    }

    private void SubmitChatbotUserMessage()
    {
        if (ChatbotInputTextBox == null || _isGeminiWaitingResponse) return;

        var message = ChatbotInputTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message)) return;

        ChatbotInputTextBox.Text = string.Empty;

        // Thêm tin nhắn người dùng vào UI
        AddUserMessageToChat(message);

        // Kiểm tra xem đã có API Key chưa
        if (string.IsNullOrWhiteSpace(_chatbotConfig.ApiKey))
        {
            AddAmiMessageToChat(
                "Anh ơi, em chưa thấy cấu hình **Gemini API Key** ạ! 🔑\n\n" +
                "Anh vui lòng mở tab **Cài đặt -> Chatbot AI**, dán API Key của Google AI Studio vào và nhấn **Lưu cài đặt** để em có thể phản hồi anh nha! ✨💖"
            );
            return;
        }

        // Bắt đầu gọi Gemini API
        _ = CallGeminiChatApiAsync(message);
    }

    private async Task CallGeminiChatApiAsync(string userMessage)
    {
        _isGeminiWaitingResponse = true;
        ShowChatbotThinkingIndicator(true);

        try
        {
            var model = GetSelectedModelName();
            var apiKey = _chatbotConfig.ApiKey;
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            // Thu thập context phòng máy
            var pcSummaryContext = BuildPcSummaryContext();

            var systemInstructionText =
                $"Bạn là {_chatbotConfig.AssistantName}, một nữ trợ lý AI thông minh, ngọt ngào, tinh tế và dễ thương của hệ thống quản lý phòng máy Cyber Game ServerManagerBilling.\n" +
                "Cách xưng hô: Bạn gọi người dùng là 'Anh' hoặc 'Quản lý', và tự xưng là 'Em' hoặc 'Ami'.\n" +
                "Phong cách trò chuyện: Vui vẻ, thân thiện, chu đáo, dùng một số biểu tượng cảm xúc (emoji) dễ thương như ✨, 💖, 🌸, 🖥️, 🚀.\n" +
                "Nhiệm vụ: Giúp quản lý theo dõi máy trạm, doanh thu, thành viên VIP, cảnh báo nợ dịch vụ, hoặc trò chuyện tâm sự giải tỏa căng thẳng khi làm việc.\n" +
                (string.IsNullOrWhiteSpace(pcSummaryContext) ? "" : $"\n--- DỮ LIỆU THỜI GIAN THỰC CỦA PHÒNG MÁY ---\n{pcSummaryContext}\n-----------------------------------------------\n");

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = userMessage } }
                    }
                },
                systemInstruction = new
                {
                    parts = new[] { new { text = systemInstructionText } }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 800
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _geminiHttpClient.PostAsync(url, jsonContent);

            if (response.IsSuccessStatusCode)
            {
                var respString = await response.Content.ReadAsStringAsync();
                var answer = ParseGeminiAnswer(respString);
                if (string.IsNullOrWhiteSpace(answer))
                {
                    answer = "Dạ em đã nhận được tin nhắn của anh nhưng chưa có câu trả lời phù hợp, anh thử hỏi lại em xem sao nhé! ✨";
                }
                AddAmiMessageToChat(answer);
            }
            else
            {
                var errDetail = await response.Content.ReadAsStringAsync();
                if ((int)response.StatusCode == 429)
                {
                    AddAmiMessageToChat("Anh ơi, Google Gemini báo lỗi **429 (Hết hạn ngạch miễn phí hôm nay)** rồi ạ! Gói Free của Google chỉ cho 20 lượt/ngày trên model này. Anh chỉ cần vào **Google AI Studio** bấm tạo thêm 1 API Key mới miễn phí rồi dán vào tab Cài đặt là em lại trò chuyện với anh được ngay nha! 🌸✨");
                }
                else
                {
                    AddAmiMessageToChat($"Anh ơi, Google Gemini báo lỗi (Mã {((int)response.StatusCode)}). Anh kiểm tra lại API Key hoặc hạn ngạch trong tab Cài đặt nhé! 🌸");
                }
            }
        }
        catch (TaskCanceledException)
        {
            AddAmiMessageToChat("Anh ơi, kết nối tới Google Gemini bị quá thời gian (hơn 60 giây) do mạng chậm hoặc model xử lý lâu. Anh thử gửi lại hoặc đổi sang model nhẹ hơn như **gemini-2.5-flash** trong tab Cài đặt nhé! 🌸");
        }
        catch (Exception ex)
        {
            AddAmiMessageToChat($"Anh ơi, gặp sự cố kết nối tới Gemini rồi ạ: {ex.Message} 😿");
        }
        finally
        {
            _isGeminiWaitingResponse = false;
            ShowChatbotThinkingIndicator(false);
            ScrollChatToBottom();
        }
    }

    private string ParseGeminiAnswer(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var text = parts[0].GetProperty("text").GetString();
                    return text ?? string.Empty;
                }
            }
        }
        catch
        {
            // Parse fallback
        }
        return string.Empty;
    }

    private string BuildPcSummaryContext()
    {
        if (!_chatbotConfig.AllowPcContext)
        {
            return string.Empty;
        }

        try
        {
            var rows = _allMachineRows.ToList();
            var totalCount = rows.Count;
            if (totalCount == 0)
            {
                return "Hiện tại chưa có danh sách máy trạm nào kết nối.";
            }

            var inUseCount = rows.Count(r => string.Equals(r.StatusCode, "IN_USE", StringComparison.OrdinalIgnoreCase));
            var onlineCount = rows.Count(r => string.Equals(r.StatusCode, "ONLINE", StringComparison.OrdinalIgnoreCase));
            var offlineCount = rows.Count(r => string.Equals(r.StatusCode, "OFFLINE", StringComparison.OrdinalIgnoreCase));
            var vipCount = rows.Count(r => r.IsVipSession);
            var guestCount = rows.Count(r => r.IsGuestSession);
            var debtCount = rows.Count(r => r.HasServiceDebtHighlight);

            decimal totalMoney = 0;
            foreach (var r in rows)
            {
                if (decimal.TryParse(r.MoneyText?.Replace(",", "")?.Replace(".", ""), out var m))
                {
                    totalMoney += m;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"- Tổng số máy: {totalCount} máy.");
            sb.AppendLine($"- Đang có người chơi (IN_USE): {inUseCount} máy (trong đó có {vipCount} máy VIP, {guestCount} khách vãng lai).");
            sb.AppendLine($"- Máy sẵn sàng (ONLINE): {onlineCount} máy.");
            sb.AppendLine($"- Máy tắt / mất kết nối (OFFLINE): {offlineCount} máy.");
            sb.AppendLine($"- Số máy có nợ dịch vụ đồ ăn/uống: {debtCount} máy.");
            sb.AppendLine($"- Tạm tính tổng tiền giờ các máy đang chơi: ~{totalMoney:N0} VNĐ.");

            // Liệt kê một số máy nổi bật
            var activeMachines = rows.Where(r => string.Equals(r.StatusCode, "IN_USE", StringComparison.OrdinalIgnoreCase)).Take(10).ToList();
            if (activeMachines.Count > 0)
            {
                sb.AppendLine("- Danh sách máy đang chơi tiêu biểu:");
                foreach (var m in activeMachines)
                {
                    sb.AppendLine($"  + {m.Name}: {m.UserName} (Chơi: {m.UsedText}, Tiền: {m.MoneyText}đ)");
                }
            }

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    // ==========================================
    // UI CHAT MESSAGE RENDERING
    // ==========================================
    private void AddUserMessageToChat(string text)
    {
        if (ChatbotMessagesPanel == null) return;

        var row = new DockPanel
        {
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var bubble = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
            CornerRadius = new CornerRadius(14, 14, 4, 14),
            Padding = new Thickness(12, 8, 12, 8),
            MaxWidth = 280
        };

        var tb = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };

        bubble.Child = tb;
        row.Children.Add(bubble);
        ChatbotMessagesPanel.Children.Add(row);

        ScrollChatToBottom();
    }

    private void AddAmiMessageToChat(string markdownText)
    {
        if (ChatbotMessagesPanel == null) return;

        var row = new Grid
        {
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Avatar nhỏ
        var avatarBorder = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 2, 8, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        var avatarText = new TextBlock
        {
            Text = "🌸",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        avatarBorder.Child = avatarText;
        Grid.SetColumn(avatarBorder, 0);
        row.Children.Add(avatarBorder);

        // Bong bóng tin nhắn
        var bubble = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4, 14, 14, 14),
            Padding = new Thickness(12, 8, 12, 8),
            MaxWidth = 290
        };

        var tb = new TextBlock
        {
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };

        // Render Markdown cơ bản (in đậm **text**, ngắt dòng)
        RenderMarkdownToTextBlock(tb, markdownText);

        bubble.Child = tb;
        Grid.SetColumn(bubble, 1);
        row.Children.Add(bubble);

        ChatbotMessagesPanel.Children.Add(row);
        ScrollChatToBottom();
    }

    private static void RenderMarkdownToTextBlock(TextBlock target, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var parts = text.Split(new[] { "**" }, StringSplitOptions.None);
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;

            if (i % 2 == 1) // In đậm
            {
                target.Inlines.Add(new Run(parts[i])
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8"))
                });
            }
            else // Chữ bình thường
            {
                target.Inlines.Add(new Run(parts[i]));
            }
        }
    }

    private void ShowChatbotThinkingIndicator(bool isThinking)
    {
        if (ChatbotThinkingRow != null)
        {
            ChatbotThinkingRow.Visibility = isThinking ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ScrollChatToBottom()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            ChatbotScrollViewer?.ScrollToBottom();
        }));
    }

    private string GetSelectedModelName()
    {
        if (ChatbotModelComboBox != null)
        {
            if (ChatbotModelComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                return item.Tag.ToString() ?? "gemini-3.8-flash";
            }
            var text = ChatbotModelComboBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Split(' ')[0].Trim();
            }
        }
        return !string.IsNullOrWhiteSpace(_chatbotConfig.ModelName) ? _chatbotConfig.ModelName : "gemini-3.8-flash";
    }

    private void QuickSelectModelChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelName)
        {
            if (ChatbotModelComboBox != null)
            {
                bool matched = false;
                foreach (var obj in ChatbotModelComboBox.Items)
                {
                    if (obj is ComboBoxItem item && string.Equals(item.Tag?.ToString(), modelName, StringComparison.OrdinalIgnoreCase))
                    {
                        ChatbotModelComboBox.SelectedItem = item;
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    ChatbotModelComboBox.Text = modelName;
                }
            }

            _chatbotConfig.ModelName = modelName;
            SaveChatbotSettings();

            if (ChatbotSettingsStatusTextBlock != null)
            {
                ChatbotSettingsStatusTextBlock.Text = $"⚡ Đã chọn mô hình: {modelName}";
                ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
            }
        }
    }

    private async void ScanGeminiModelsButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ChatbotApiKeyTextBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (ChatbotSettingsStatusTextBlock != null)
            {
                ChatbotSettingsStatusTextBlock.Text = "⚠️ Vui lòng nhập API Key trước khi quét model.";
                ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
            }
            return;
        }

        if (ChatbotSettingsStatusTextBlock != null)
        {
            ChatbotSettingsStatusTextBlock.Text = "⏳ Đang quét danh sách model khả dụng trên Google AI Studio...";
            ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
            var response = await _geminiHttpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("models", out var modelsArray))
                {
                    var foundModels = new List<string>();
                    foreach (var m in modelsArray.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var nameProp))
                        {
                            var fullName = nameProp.GetString() ?? "";
                            var cleanName = fullName.Replace("models/", "").Trim();

                            // Kiểm tra xem model có hỗ trợ generateContent không
                            bool supportsGenerate = false;
                            if (m.TryGetProperty("supportedGenerationMethods", out var methods))
                            {
                                foreach (var method in methods.EnumerateArray())
                                {
                                    if (string.Equals(method.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase))
                                    {
                                        supportsGenerate = true;
                                        break;
                                    }
                                }
                            }

                            if (supportsGenerate && !string.IsNullOrWhiteSpace(cleanName))
                            {
                                foundModels.Add(cleanName);
                            }
                        }
                    }

                    if (foundModels.Count > 0 && ChatbotModelComboBox != null)
                    {
                        ChatbotModelComboBox.Items.Clear();
                        foreach (var mName in foundModels)
                        {
                            var item = new ComboBoxItem
                            {
                                Content = mName,
                                Tag = mName
                            };
                            ChatbotModelComboBox.Items.Add(item);
                        }

                        // Ưu tiên chọn model tốt nhất
                        var preferred = foundModels.FirstOrDefault(m => m.Contains("3.8") || m.Contains("4-argon"))
                                     ?? foundModels.FirstOrDefault(m => m.Contains("2.5"))
                                     ?? foundModels.FirstOrDefault(m => m.Contains("2.0"))
                                     ?? foundModels.FirstOrDefault(m => m.Contains("1.5-flash"))
                                     ?? foundModels.First();

                        foreach (ComboBoxItem item in ChatbotModelComboBox.Items)
                        {
                            if (string.Equals(item.Tag?.ToString(), preferred, StringComparison.OrdinalIgnoreCase))
                            {
                                ChatbotModelComboBox.SelectedItem = item;
                                break;
                            }
                        }

                        _chatbotConfig.ModelName = preferred;
                        SaveChatbotSettings();

                        if (ChatbotSettingsStatusTextBlock != null)
                        {
                            ChatbotSettingsStatusTextBlock.Text = $"🎉 Đã quét thành công {foundModels.Count} model khả dụng trên API Key của bạn! Đang chọn: {preferred}";
                            ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                        }
                        return;
                    }
                }
            }

            if (ChatbotSettingsStatusTextBlock != null)
            {
                ChatbotSettingsStatusTextBlock.Text = $"⚠️ Không tìm thấy danh sách model hoặc API Key không hợp lệ (Mã: {((int)response.StatusCode)}).";
                ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            }
        }
        catch (Exception ex)
        {
            if (ChatbotSettingsStatusTextBlock != null)
            {
                ChatbotSettingsStatusTextBlock.Text = $"❌ Lỗi khi quét model: {ex.Message}";
                ChatbotSettingsStatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            }
        }
    }

    private static string FormatGeminiError(int statusCode, string responseContent)
    {
        if (statusCode == 429)
        {
            var extra = "Hạn ngạch gói miễn phí (Free Tier) hôm nay của model này trên Google AI Studio đã hết (giới hạn 20 lượt/ngày).";
            if (responseContent.Contains("retry in"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(responseContent, @"retry in ([^.\n]+)");
                if (match.Success)
                {
                    extra += $" Thử lại sau {match.Groups[1].Value.Trim()}.";
                }
            }
            return $"⚠️ Lỗi 429 (Hết hạn ngạch / Quota Exceeded): {extra} Bạn có thể tạo thêm API Key mới miễn phí tại Google AI Studio để tiếp tục ngay!";
        }

        if (statusCode == 400)
        {
            if (responseContent.Contains("API_KEY_INVALID") || responseContent.Contains("API key not valid"))
            {
                return "❌ Lỗi 400: API Key không hợp lệ. Vui lòng kiểm tra lại mã khóa đã copy.";
            }
            return $"❌ Lỗi 400: {ExtractErrorMessage(responseContent)}";
        }

        if (statusCode == 403)
        {
            return "❌ Lỗi 403 (Forbidden): Key chưa được cấp quyền Generative Language API hoặc bị giới hạn vùng.";
        }

        if (statusCode == 404)
        {
            return "❌ Lỗi 404: Model này không tồn tại hoặc đã ngừng hỗ trợ. Hãy chọn model khác.";
        }

        return $"❌ Lỗi kết nối (Mã {statusCode}): {ExtractErrorMessage(responseContent)}";
    }

    private static string ExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
            {
                var text = msg.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var firstLine = text.Split('\n')[0].Trim();
                    return firstLine;
                }
            }
        }
        catch { }
        return "Kiểm tra lại API Key hoặc quyền truy cập.";
    }

}
