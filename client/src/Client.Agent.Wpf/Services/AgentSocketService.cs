using Client.Agent.Wpf.Models;
using SocketIOClient;
using SocketIOClient.Transport;

namespace Client.Agent.Wpf.Services;

public sealed class AgentSocketService : IAsyncDisposable
{
    private readonly AgentSettings _settings;
    private readonly FileLogger _logger;
    private readonly Func<CommandExecutePayload, Task<(bool Success, string? Message)>> _commandHandler;
    private readonly Action<string> _connectionStatusChanged;
    private readonly Action<string, string?>? _notificationHandler;

    private global::SocketIOClient.SocketIO? _socket;
    private CancellationTokenSource? _heartbeatCts;

    public AgentSocketService(
        AgentSettings settings,
        FileLogger logger,
        Func<CommandExecutePayload, Task<(bool Success, string? Message)>> commandHandler,
        Action<string> connectionStatusChanged,
        Action<string, string?>? notificationHandler = null)
    {
        _settings = settings;
        _logger = logger;
        _commandHandler = commandHandler;
        _connectionStatusChanged = connectionStatusChanged;
        _notificationHandler = notificationHandler;
    }

    public async Task StartAsync()
    {
        if (_socket is not null)
        {
            return;
        }

        var endpoint = new Uri($"{_settings.ServerUrl.TrimEnd('/')}/billing");
        _socket = new global::SocketIOClient.SocketIO(endpoint, new SocketIOOptions
        {
            Transport = TransportProtocol.WebSocket,
            Reconnection = true,
            ReconnectionAttempts = int.MaxValue,
            ReconnectionDelay = 3000,
        });

        _socket.OnConnected += async (_, _) =>
        {
            _connectionStatusChanged("Connected");
            await _logger.InfoAsync("Connected to billing server");
            await EmitHelloAsync();
            StartHeartbeatLoop();
        };

        _socket.OnDisconnected += async (_, reason) =>
        {
            _connectionStatusChanged("Disconnected");
            await _logger.ErrorAsync($"Disconnected from billing server. Reason: {reason}");
            StopHeartbeatLoop();
        };

        _socket.OnReconnectAttempt += async (_, attempt) =>
        {
            _connectionStatusChanged($"Reconnecting ({attempt})");
            await _logger.InfoAsync($"Reconnect attempt #{attempt}");
        };

        _socket.On("command.execute", async response =>
        {
            var payload = response.GetValue<CommandExecutePayload>();
            await HandleCommandAsync(payload);
        });

        _socket.On("admin.notify", async response =>
        {
            var payload = response.GetValue<AdminNotifyPayload>();
            await _logger.InfoAsync($"Received admin.notify: {payload.Message}");
            _notificationHandler?.Invoke(payload.Message, payload.RequestedBy);
        });

        await _socket.ConnectAsync();
    }

    private async Task HandleCommandAsync(CommandExecutePayload payload)
    {
        if (_socket is null)
        {
            return;
        }

        await _logger.InfoAsync(
            $"Received command.execute {payload.Type} (commandId={payload.CommandId})");

        var result = await _commandHandler(payload);
        await _socket.EmitAsync("command.ack", new
        {
            commandId = payload.CommandId,
            agentId = _settings.AgentId,
            result = result.Success ? "SUCCESS" : "FAILED",
            message = result.Message,
        });
    }

    private async Task EmitHelloAsync()
    {
        if (_socket is null)
        {
            return;
        }

        await _socket.EmitAsync("agent.hello", new
        {
            agentId = _settings.AgentId,
            hostname = Environment.MachineName,
            ip = string.Empty,
            version = "0.1.0",
            at = DateTimeOffset.UtcNow.ToString("O"),
        });
    }

    private async Task EmitHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (_socket is null || !_socket.Connected)
        {
            return;
        }

        await _socket.EmitAsync("agent.heartbeat", new
        {
            agentId = _settings.AgentId,
            at = DateTimeOffset.UtcNow.ToString("O"),
        }, cancellationToken);
    }

    private void StartHeartbeatLoop()
    {
        StopHeartbeatLoop();
        _heartbeatCts = new CancellationTokenSource();
        var token = _heartbeatCts.Token;

        _ = Task.Run(async () =>
        {
            var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.HeartbeatIntervalSeconds));
            using var timer = new PeriodicTimer(interval);

            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    await EmitHeartbeatAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _logger.ErrorAsync("Heartbeat failed", ex);
                }
            }
        }, token);
    }

    private void StopHeartbeatLoop()
    {
        if (_heartbeatCts is null)
        {
            return;
        }

        _heartbeatCts.Cancel();
        _heartbeatCts.Dispose();
        _heartbeatCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        StopHeartbeatLoop();

        if (_socket is not null)
        {
            try
            {
                await _socket.DisconnectAsync();
            }
            catch
            {
                // ignore on shutdown
            }

            _socket.Dispose();
            _socket = null;
        }
    }
}

public sealed class AdminNotifyPayload
{
    public string Message { get; set; } = string.Empty;
    public string? RequestedBy { get; set; }
    public string? SentAt { get; set; }
}
