using System.Windows;
using System.Windows.Threading;
using SocketIOClient;
using SocketIOClient.Transport;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _realtimeMachineRefreshDebounceTimer = new();
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

            _billingSocket = socket;
            await _billingSocket.ConnectAsync();
        }
        catch (Exception ex)
        {
            AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Khong ket noi realtime may tram: {ex.Message}");
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
}
