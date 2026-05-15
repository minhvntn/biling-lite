using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
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
