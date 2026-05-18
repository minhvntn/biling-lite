using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using SocketIOClient;
using SocketIOClient.Transport;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _realtimeMachineRefreshDebounceTimer = new();
    private readonly DispatcherTimer _memberWithdrawPendingPollTimer = new();
    private readonly DispatcherTimer _memberTopupPendingPollTimer = new();
    private global::SocketIOClient.SocketIO? _billingSocket;
    private bool _isRealtimeBridgeInitialized;
    private bool _realtimeMachineRefreshQueued;
    private readonly DispatcherTimer _serviceOrderPopupBatchTimer = new();
    private readonly Dictionary<string, List<PendingServiceOrderPopupItem>> _pendingServiceOrderPopupByPcId = new(StringComparer.OrdinalIgnoreCase);

    private void InitializeRealtimeMachineRefreshBridge()
    {
        if (_isRealtimeBridgeInitialized)
        {
            return;
        }

        _realtimeMachineRefreshDebounceTimer.Interval = TimeSpan.FromMilliseconds(120);
        _realtimeMachineRefreshDebounceTimer.Tick += RealtimeMachineRefreshDebounceTimer_Tick;

        _memberWithdrawPendingPollTimer.Interval = TimeSpan.FromSeconds(8);
        _memberWithdrawPendingPollTimer.Tick += MemberWithdrawPendingPollTimer_Tick;
        _memberWithdrawPendingPollTimer.Start();

        _memberTopupPendingPollTimer.Interval = TimeSpan.FromSeconds(8);
        _memberTopupPendingPollTimer.Tick += MemberTopupPendingPollTimer_Tick;
        _memberTopupPendingPollTimer.Start();

        _serviceOrderPopupBatchTimer.Interval = TimeSpan.FromMilliseconds(900);
        _serviceOrderPopupBatchTimer.Tick += ServiceOrderPopupBatchTimer_Tick;

        _isRealtimeBridgeInitialized = true;
    }

    private async Task ConnectRealtimeMachineRefreshAsync()
    {
        await DisconnectRealtimeMachineRefreshAsync();

        Uri endpoint;
        try
        {
            endpoint = BuildBillingRealtimeEndpoint();
        }
        catch (Exception ex)
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Realtime may tram loi URL: {ex.Message}");
            _ = LoadPendingMemberWithdrawRequestsAsync();
            _ = LoadPendingMemberTopupRequestsAsync();
            return;
        }

        try
        {
            var socket = new global::SocketIOClient.SocketIO(endpoint, new SocketIOOptions
            {
                Transport = TransportProtocol.WebSocket,
                Reconnection = true,
                ReconnectionAttempts = int.MaxValue,
                ReconnectionDelay = 2000,
            });

            socket.OnConnected += (_, _) =>
            {
                QueueRealtimeUi(() =>
                    AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Realtime may tram: da ket noi"));
                _ = LoadPendingMemberWithdrawRequestsAsync();
                _ = LoadPendingMemberTopupRequestsAsync();
            };

            socket.OnDisconnected += (_, reason) =>
            {
                QueueRealtimeUi(() =>
                    AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Realtime may tram: mat ket noi ({reason})"));
            };

            socket.On("pc.status.changed", _ =>
            {
                QueueRealtimeMachineRefresh();
            });

            socket.On("member.withdraw.requested", response =>
            {
                try
                {
                    var payload = TryParseMemberWithdrawRequest(response);
                    if (payload is null || string.IsNullOrWhiteSpace(payload.RequestId))
                    {
                        QueueRealtimeUi(() =>
                            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Bỏ qua member.withdraw.requested do payload không hợp lệ."));
                        return;
                    }

                    QueueRealtimeUi(() =>
                        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Nhận yêu cầu rút tiền realtime: {payload.RequestId}"));
                    QueueRealtimeUi(() => HandleRealtimeMemberWithdrawRequested(payload));
                }
                catch (Exception ex)
                {
                    QueueRealtimeUi(() =>
                        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi parse member.withdraw.requested: {ex.Message}"));
                }
            });

            socket.On("member.topup.requested", response =>
            {
                try
                {
                    var payload = TryParseMemberTopupRequest(response);
                    if (payload is null || string.IsNullOrWhiteSpace(payload.RequestId))
                    {
                        QueueRealtimeUi(() =>
                            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Bỏ qua member.topup.requested do payload không hợp lệ."));
                        return;
                    }

                    QueueRealtimeUi(() =>
                        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Nhận yêu cầu nạp tiền realtime: {payload.RequestId}"));
                    QueueRealtimeUi(() => HandleRealtimeMemberTopupRequested(payload));
                }
                catch (Exception ex)
                {
                    QueueRealtimeUi(() =>
                        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi parse member.topup.requested: {ex.Message}"));
                }
            });

            socket.On("service.order.created", response =>
            {
                try
                {
                    var payload = TryParseRealtimeServiceOrderCreated(response);
                    if (payload is null || payload.Order is null || string.IsNullOrWhiteSpace(payload.PcId))
                    {
                        QueueRealtimeUi(() =>
                            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Bỏ qua service.order.created do payload không hợp lệ."));
                        return;
                    }

                    var createdBy = payload.Order.CreatedBy?.Trim() ?? string.Empty;
                    var fromClient = string.Equals(payload.Source?.Trim(), "client", StringComparison.OrdinalIgnoreCase)
                                     || IsClientServiceRequester(createdBy);
                    if (!fromClient)
                    {
                        return;
                    }

                    ClearClientServiceOrderAcknowledgement(payload.Order.Id);
                    InvalidateServiceAmountCacheForPcId(payload.PcId);
                    QueueRealtimeMachineRefresh();
                    QueueRealtimeUi(() => HandleRealtimeServiceOrderCreated(payload));
                    MarkClientServiceOrderNotified(payload.Order.Id);
                }
                catch (Exception ex)
                {
                    QueueRealtimeUi(() =>
                        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Lỗi parse service.order.created: {ex.Message}"));
                }
            });
            _billingSocket = socket;
            await _billingSocket.ConnectAsync();
        }
        catch (Exception ex)
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Không kết nối realtime máy trạm: {ex.Message}");
            _ = LoadPendingMemberWithdrawRequestsAsync();
            _ = LoadPendingMemberTopupRequestsAsync();
        }
    }

    private async Task DisconnectRealtimeMachineRefreshAsync()
    {
        if (_billingSocket is null)
        {
            return;
        }

        var socket = _billingSocket;
        _billingSocket = null;
        try
        {
            if (socket.Connected)
            {
                await socket.DisconnectAsync();
            }
        }
        catch
        {
            // Ignore disconnect errors to keep UI responsive.
        }

        socket.Dispose();
    }

    private Uri BuildBillingRealtimeEndpoint()
    {
        var apiBaseUri = new Uri(_settings.BackendApiBaseUrl.TrimEnd('/') + "/");
        var builder = new UriBuilder(apiBaseUri.Scheme, apiBaseUri.Host, apiBaseUri.Port)
        {
            Path = "/billing",
            Query = string.Empty,
        };

        return builder.Uri;
    }

    private void QueueRealtimeMachineRefresh()
    {
        if (!IsLoaded)
        {
            return;
        }

        QueueRealtimeUi(() =>
        {
            _realtimeMachineRefreshQueued = true;
            if (!_realtimeMachineRefreshDebounceTimer.IsEnabled)
            {
                _realtimeMachineRefreshDebounceTimer.Start();
            }
        });
    }

    private async void RealtimeMachineRefreshDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _realtimeMachineRefreshDebounceTimer.Stop();

        if (!_realtimeMachineRefreshQueued)
        {
            return;
        }

        _realtimeMachineRefreshQueued = false;

        if (_isRefreshingMachines)
        {
            _realtimeMachineRefreshQueued = true;
            _realtimeMachineRefreshDebounceTimer.Start();
            return;
        }

        await RefreshMachinesAsync();
    }

    private void ShutdownRealtimeMachineRefreshBridge()
    {
        if (_isRealtimeBridgeInitialized)
        {
            _realtimeMachineRefreshDebounceTimer.Stop();
            _realtimeMachineRefreshDebounceTimer.Tick -= RealtimeMachineRefreshDebounceTimer_Tick;

            _memberWithdrawPendingPollTimer.Stop();
            _memberWithdrawPendingPollTimer.Tick -= MemberWithdrawPendingPollTimer_Tick;

            _memberTopupPendingPollTimer.Stop();
            _memberTopupPendingPollTimer.Tick -= MemberTopupPendingPollTimer_Tick;

            _serviceOrderPopupBatchTimer.Stop();
            _serviceOrderPopupBatchTimer.Tick -= ServiceOrderPopupBatchTimer_Tick;
            _pendingServiceOrderPopupByPcId.Clear();
            _isRealtimeBridgeInitialized = false;
        }

        _ = DisconnectRealtimeMachineRefreshAsync();
    }

    private void QueueRealtimeUi(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    private async void MemberWithdrawPendingPollTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await LoadPendingMemberWithdrawRequestsAsync();
    }

    private async void MemberTopupPendingPollTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await LoadPendingMemberTopupRequestsAsync();
    }

    private MemberWithdrawRequestItem? TryParseMemberWithdrawRequest(SocketIOResponse response)
    {
        try
        {
            var direct = response.GetValue<MemberWithdrawRequestItem>(0);
            if (direct is not null && !string.IsNullOrWhiteSpace(direct.RequestId))
            {
                return direct;
            }
        }
        catch
        {
            // Fallback to raw parsing below.
        }

        try
        {
            var rawElement = response.GetValue<JsonElement>(0);
            return JsonSerializer.Deserialize<MemberWithdrawRequestItem>(
                rawElement.GetRawText(),
                JsonOptions());
        }
        catch
        {
            return null;
        }
    }

    private RealtimeServiceOrderCreatedEvent? TryParseRealtimeServiceOrderCreated(SocketIOResponse response)
    {
        try
        {
            var direct = response.GetValue<RealtimeServiceOrderCreatedEvent>(0);
            if (direct is not null && !string.IsNullOrWhiteSpace(direct.PcId))
            {
                return direct;
            }
        }
        catch
        {
            // Fallback to raw parsing below.
        }

        try
        {
            var rawElement = response.GetValue<JsonElement>(0);
            return JsonSerializer.Deserialize<RealtimeServiceOrderCreatedEvent>(
                rawElement.GetRawText(),
                JsonOptions());
        }
        catch
        {
            try
            {
                var rawText = response.GetValue<string>(0);
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<RealtimeServiceOrderCreatedEvent>(
                    rawText,
                    JsonOptions());
            }
            catch
            {
                return null;
            }
        }
    }

    private void HandleRealtimeServiceOrderCreated(RealtimeServiceOrderCreatedEvent payload)
    {
        var order = payload.Order;
        if (order is null)
        {
            return;
        }

        var serviceName = string.IsNullOrWhiteSpace(order.ServiceName) ? "Dịch vụ" : order.ServiceName.Trim();
        var quantity = Math.Max(0, order.Quantity);
        var lineTotal = Math.Max(0m, order.LineTotal);
        var machineName = ResolveMachineDisplayName(payload.PcId);

        AppendServiceLog(
            $"[{DateTime.Now:HH:mm:ss}] Máy trạm {machineName} gọi dịch vụ: {serviceName} x{quantity} ({lineTotal:N0} VND)."
        );

        EnqueueServiceOrderPopupItem(new PendingServiceOrderPopupItem
        {
            PcId = payload.PcId,
            MachineName = machineName,
            ServiceName = serviceName,
            Quantity = quantity,
            LineTotal = lineTotal,
            CreatedAt = payload.At,
            OrderId = order.Id,
        });
    }

    private void EnqueueServiceOrderPopupItem(PendingServiceOrderPopupItem item)
    {
        if (string.IsNullOrWhiteSpace(item.PcId))
        {
            return;
        }

        if (!_pendingServiceOrderPopupByPcId.TryGetValue(item.PcId, out var items))
        {
            items = new List<PendingServiceOrderPopupItem>();
            _pendingServiceOrderPopupByPcId[item.PcId] = items;
        }

        items.Add(item);
        _serviceOrderPopupBatchTimer.Stop();
        _serviceOrderPopupBatchTimer.Start();
    }

    private void ServiceOrderPopupBatchTimer_Tick(object? sender, EventArgs e)
    {
        _serviceOrderPopupBatchTimer.Stop();
        if (_pendingServiceOrderPopupByPcId.Count == 0)
        {
            return;
        }

        var batches = _pendingServiceOrderPopupByPcId
            .Select(x => new
            {
                PcId = x.Key,
                Items = x.Value
                    .OrderBy(v => ParseDateLocal(v.CreatedAt) ?? DateTime.MaxValue)
                    .ToList(),
            })
            .ToList();

        _pendingServiceOrderPopupByPcId.Clear();

        foreach (var batch in batches)
        {
            ShowServiceOrderPopupBatch(batch.PcId, batch.Items);
        }
    }

    private async Task PlayServiceOrderNotificationAudioAsync()
    {
        await _guestLoginSpeechLock.WaitAsync();
        try
        {
            var fileNames = new[] { "order-dich-vu.mp3", "order-dich-vu.wav" };
            var roots = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "audio"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ServerManagerBilling", "audio"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Music"),
            };

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                foreach (var fileName in fileNames)
                {
                    var path = Path.Combine(root, fileName);
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    if (!TryPlayAudioFile(path))
                    {
                        continue;
                    }

                    AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Played service-order audio: {path}");
                    return;
                }
            }

            System.Media.SystemSounds.Asterisk.Play();
        }
        catch
        {
            // Keep notification flow resilient when audio is unavailable.
        }
        finally
        {
            _guestLoginSpeechLock.Release();
        }
    }

    private void ShowServiceOrderPopupBatch(string pcId, IReadOnlyList<PendingServiceOrderPopupItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var machineName = string.IsNullOrWhiteSpace(items[0].MachineName)
            ? ResolveMachineDisplayName(pcId)
            : items[0].MachineName;

        var totalQuantity = items.Sum(x => Math.Max(0, x.Quantity));
        var totalAmount = items.Sum(x => Math.Max(0m, x.LineTotal));
        var atText = FormatDateTime(items.Last().CreatedAt);

        _ = PlayServiceOrderNotificationAudioAsync();

        var window = new Window
        {
            Title = $"Thông báo order dịch vụ - {machineName}",
            Width = 560,
            Height = 360,
            MinWidth = 520,
            MinHeight = 320,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Owner = this,
            Topmost = true,
            Background = Brushes.White,
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 10),
        };
        headerBorder.Child = new TextBlock
        {
            Text = items.Count == 1 ? "Máy trạm vừa gửi order dịch vụ" : $"Máy trạm vừa gửi {items.Count} order dịch vụ",
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
        };
        Grid.SetRow(headerBorder, 0);
        root.Children.Add(headerBorder);

        var contentGrid = new Grid();
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var infoPanel = new StackPanel { Margin = new Thickness(4, 0, 4, 8) };
        AddServiceOrderInfoRow(infoPanel, "Nguồn gọi:", "Máy trạm");
        AddServiceOrderInfoRow(infoPanel, "Máy:", machineName);
        AddServiceOrderInfoRow(infoPanel, "Tổng món:", $"{totalQuantity:N0}");
        AddServiceOrderInfoRow(infoPanel, "Tổng tiền:", $"{totalAmount:N0} VND");
        AddServiceOrderInfoRow(infoPanel, "Thời gian:", atText);
        Grid.SetRow(infoPanel, 0);
        contentGrid.Children.Add(infoPanel);

        var listBox = new ListBox
        {
            Margin = new Thickness(4, 0, 4, 0),
            FontSize = 13,
            ItemsSource = items.Select(x => $"- {x.ServiceName}: {x.Quantity:N0} ({x.LineTotal:N0} VND)").ToList(),
            IsHitTestVisible = false,
            Focusable = false,
        };
        Grid.SetRow(listBox, 1);
        contentGrid.Children.Add(listBox);

        Grid.SetRow(contentGrid, 1);
        root.Children.Add(contentGrid);

        var closeButton = new Button
        {
            Content = "Đóng",
            Width = 120,
            Height = 38,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            Margin = new Thickness(0, 10, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(29, 78, 216)),
            Foreground = Brushes.White,
        };
        closeButton.Click += (_, _) => window.Close();
        Grid.SetRow(closeButton, 2);
        root.Children.Add(closeButton);

        window.Content = root;
        window.Show();
        window.Activate();
    }

    private string ResolveMachineDisplayName(string? pcId)
    {
        if (string.IsNullOrWhiteSpace(pcId))
        {
            return "-";
        }

        var row = FindMachineRowById(pcId.Trim());
        if (row is null)
        {
            return pcId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(row.Name))
        {
            return row.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(row.AgentId))
        {
            return row.AgentId.Trim();
        }

        return pcId.Trim();
    }

    private static void AddServiceOrderInfoRow(Panel panel, string label, string value)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 7),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(labelBlock, 0);
        row.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);

        panel.Children.Add(row);
    }

    private sealed class PendingServiceOrderPopupItem
    {
        public string OrderId { get; set; } = string.Empty;
        public string PcId { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal LineTotal { get; set; }
        public string? CreatedAt { get; set; }
    }
    private sealed class RealtimeServiceOrderCreatedEvent
    {
        public string PcId { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string? Source { get; set; }
        public string? At { get; set; }
        public RealtimeServiceOrderInfo? Order { get; set; }
    }

    private sealed class RealtimeServiceOrderInfo
    {
        public string Id { get; set; } = string.Empty;
        public string ServiceItemId { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
    }
    private MemberTopupRequestItem? TryParseMemberTopupRequest(SocketIOResponse response)
    {
        try
        {
            var direct = response.GetValue<MemberTopupRequestItem>(0);
            if (direct is not null && !string.IsNullOrWhiteSpace(direct.RequestId))
            {
                return direct;
            }
        }
        catch
        {
            // Fallback to raw parsing below.
        }

        try
        {
            var rawElement = response.GetValue<JsonElement>(0);
            return JsonSerializer.Deserialize<MemberTopupRequestItem>(
                rawElement.GetRawText(),
                JsonOptions());
        }
        catch
        {
            return null;
        }
    }
}

