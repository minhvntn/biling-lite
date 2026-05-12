using Client.Agent.Wpf.Models;
using SocketIOClient;
using SocketIOClient.Transport;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Client.Agent.Wpf.Services;

public sealed class AgentSocketService : IAsyncDisposable
{
    private readonly AgentSettings _settings;
    private readonly FileLogger _logger;
    private readonly Func<CommandExecutePayload, Task<(bool Success, string? Message)>> _commandHandler;
    private readonly Action<string> _connectionStatusChanged;
    private readonly Action<string, string?>? _notificationHandler;
    private readonly Func<AdminCaptureScreenshotPayload, Task>? _captureScreenshotHandler;
    private readonly Func<AdminLiveFrameRequestPayload, Task>? _liveFrameHandler;
    private readonly Func<AdminRemoteInputPayload, Task>? _remoteInputHandler;
    private readonly Action<decimal>? _hourlyRateHandler;
    private readonly Action<bool>? _guestLoginEnabledHandler;
    private readonly Action<int>? _elapsedSecondsHandler;
    private readonly Action<bool>? _resumeGuestSessionHandler;

    private global::SocketIOClient.SocketIO? _socket;
    private CancellationTokenSource? _heartbeatCts;

    public Func<AdminGetRunningAppsPayload, Task>? GetRunningAppsHandler { get; set; }
    public Func<AdminKillProcessPayload, Task>? KillProcessHandler { get; set; }

    public AgentSocketService(
        AgentSettings settings,
        FileLogger logger,
        Func<CommandExecutePayload, Task<(bool Success, string? Message)>> commandHandler,
        Action<string> connectionStatusChanged,
        Action<string, string?>? notificationHandler = null,
        Func<AdminCaptureScreenshotPayload, Task>? captureScreenshotHandler = null,
        Func<AdminLiveFrameRequestPayload, Task>? liveFrameHandler = null,
        Func<AdminRemoteInputPayload, Task>? remoteInputHandler = null,
        Action<decimal>? hourlyRateHandler = null,
        Action<bool>? guestLoginEnabledHandler = null,
        Action<int>? elapsedSecondsHandler = null,
        Action<bool>? resumeGuestSessionHandler = null)
    {
        _settings = settings;
        _logger = logger;
        _commandHandler = commandHandler;
        _connectionStatusChanged = connectionStatusChanged;
        _notificationHandler = notificationHandler;
        _captureScreenshotHandler = captureScreenshotHandler;
        _liveFrameHandler = liveFrameHandler;
        _remoteInputHandler = remoteInputHandler;
        _hourlyRateHandler = hourlyRateHandler;
        _guestLoginEnabledHandler = guestLoginEnabledHandler;
        _elapsedSecondsHandler = elapsedSecondsHandler;
        _resumeGuestSessionHandler = resumeGuestSessionHandler;
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
            CommandExecutePayload payload;
            try
            {
                payload = TryParseCommandPayload(response);
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("Failed to parse command.execute payload", ex);
                return;
            }

            if (string.IsNullOrWhiteSpace(payload.CommandId))
            {
                await _logger.ErrorAsync("command.execute payload missing commandId");
                return;
            }

            await HandleCommandAsync(payload);
        });

        _socket.On("admin.notify", async response =>
        {
            var payload = response.GetValue<AdminNotifyPayload>();
            await _logger.InfoAsync($"Received admin.notify: {payload.Message}");
            _notificationHandler?.Invoke(payload.Message, payload.RequestedBy);
        });

        _socket.On("admin.capture_screenshot", async response =>
        {
            var payload = response.GetValue<AdminCaptureScreenshotPayload>();
            await _logger.InfoAsync(
                $"Received admin.capture_screenshot requestId={payload.RequestId} pcId={payload.PcId}");
            if (_captureScreenshotHandler is not null)
            {
                await _captureScreenshotHandler(payload);
            }
        });

        _socket.On("admin.live_frame.request", async response =>
        {
            var payload = response.GetValue<AdminLiveFrameRequestPayload>();
            if (_liveFrameHandler is not null)
            {
                await _liveFrameHandler(payload);
            }
        });

        _socket.On("admin.remote_input", async response =>
        {
            var payload = response.GetValue<AdminRemoteInputPayload>();
            if (_remoteInputHandler is not null)
            {
                await _remoteInputHandler(payload);
            }
        });

        _socket.On("admin.get_running_apps", async response =>
        {
            var payload = response.GetValue<AdminGetRunningAppsPayload>();
            await _logger.InfoAsync($"Received admin.get_running_apps requestId={payload.RequestId} pcId={payload.PcId}");
            if (GetRunningAppsHandler is not null)
            {
                await GetRunningAppsHandler(payload);
            }
        });

        _socket.On("admin.kill_process", async response =>
        {
            var payload = response.GetValue<AdminKillProcessPayload>();
            await _logger.InfoAsync($"Received admin.kill_process pid={payload.Pid} name={payload.Name}");
            if (KillProcessHandler is not null)
            {
                await KillProcessHandler(payload);
            }
        });

        _socket.On("agent.hello.ack", response =>
        {
            var element = response.GetValue<System.Text.Json.JsonElement>();
            var rate = element.GetProperty("hourlyRate").GetDecimal();
            _hourlyRateHandler?.Invoke(rate);

            if (element.TryGetProperty("isGuestLoginEnabled", out var guestEnabled))
            {
                _guestLoginEnabledHandler?.Invoke(guestEnabled.GetBoolean());
            }

            if (element.TryGetProperty("elapsedSeconds", out var elapsed))
            {
                _elapsedSecondsHandler?.Invoke(elapsed.GetInt32());
            }

            if (element.TryGetProperty("resumeGuestSession", out var resumeGuest))
            {
                _resumeGuestSessionHandler?.Invoke(resumeGuest.GetBoolean());
            }
        });

        _socket.On("agent.heartbeat.ack", response =>
        {
            var element = response.GetValue<System.Text.Json.JsonElement>();
            var rate = element.GetProperty("hourlyRate").GetDecimal();
            _hourlyRateHandler?.Invoke(rate);

            if (element.TryGetProperty("isGuestLoginEnabled", out var guestEnabled))
            {
                _guestLoginEnabledHandler?.Invoke(guestEnabled.GetBoolean());
            }

            if (element.TryGetProperty("elapsedSeconds", out var elapsed))
            {
                _elapsedSecondsHandler?.Invoke(elapsed.GetInt32());
            }
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

        (bool Success, string? Message) result;
        try
        {
            result = await _commandHandler(payload);
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync(
                $"Command handler crashed for {payload.Type} (commandId={payload.CommandId})",
                ex);
            result = (false, ex.Message);
        }

        try
        {
            await _socket.EmitAsync("command.ack", new
            {
                commandId = payload.CommandId,
                agentId = _settings.AgentId,
                result = result.Success ? "SUCCESS" : "FAILED",
                message = result.Message,
            });
            await _logger.InfoAsync(
                $"Sent command.ack {payload.Type} (commandId={payload.CommandId}, result={(result.Success ? "SUCCESS" : "FAILED")})");
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync(
                $"Failed to emit command.ack for commandId={payload.CommandId}",
                ex);
        }
    }

    private static CommandExecutePayload TryParseCommandPayload(SocketIOResponse response)
    {
        var payload = response.GetValue<CommandExecutePayload>();
        if (!string.IsNullOrWhiteSpace(payload.CommandId) &&
            !string.IsNullOrWhiteSpace(payload.Type))
        {
            payload.Type = payload.Type.Trim().ToUpperInvariant();
            return payload;
        }

        var element = response.GetValue<JsonElement>();
        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
        {
            element = element[0];
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            payload.CommandId = TryReadString(element, "commandId", "CommandId");
            payload.Type = TryReadString(element, "type", "Type").ToUpperInvariant();
            payload.IssuedAt = TryReadStringOrNull(element, "issuedAt", "IssuedAt");
            payload.AgentId = TryReadStringOrNull(element, "agentId", "AgentId");
            payload.PcId = TryReadStringOrNull(element, "pcId", "PcId");

            if (TryReadDecimal(element, out var hourlyRate))
            {
                payload.HourlyRate = hourlyRate;
            }
        }

        return payload;
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        if (!TryGetProperty(element, out var property, "hourlyRate", "HourlyRate"))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetDecimal(out var decimalValue))
        {
            value = decimalValue;
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), out decimalValue))
        {
            value = decimalValue;
            return true;
        }

        return false;
    }

    private static string TryReadString(JsonElement element, params string[] names)
    {
        if (TryGetProperty(element, out var property, names) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string? TryReadStringOrNull(JsonElement element, params string[] names)
    {
        var value = TryReadString(element, names);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
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

    private async Task EmitHeartbeatAsync()
    {
        if (_socket is null || !_socket.Connected)
        {
            return;
        }

        await _socket.EmitAsync("agent.heartbeat", new
        {
            agentId = _settings.AgentId,
            at = DateTimeOffset.UtcNow.ToString("O"),
        });
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
                    await EmitHeartbeatAsync();
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
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("requestedBy")]
    public string? RequestedBy { get; set; }

    [JsonPropertyName("sentAt")]
    public string? SentAt { get; set; }
}

public sealed class AdminCaptureScreenshotPayload
{
    [JsonPropertyName("pcId")]
    public string PcId { get; set; } = string.Empty;

    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("requestedBy")]
    public string? RequestedBy { get; set; }

    [JsonPropertyName("requestedAt")]
    public string? RequestedAt { get; set; }
}

public sealed class AdminGetRunningAppsPayload
{
    [JsonPropertyName("pcId")]
    public string PcId { get; set; } = string.Empty;

    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("requestedBy")]
    public string? RequestedBy { get; set; }
}

public sealed class AdminLiveFrameRequestPayload
{
    [JsonPropertyName("pcId")]
    public string PcId { get; set; } = string.Empty;

    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("requestedBy")]
    public string? RequestedBy { get; set; }
}

public sealed class AdminRemoteInputPayload
{
    [JsonPropertyName("pcId")]
    public string PcId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double? X { get; set; }

    [JsonPropertyName("y")]
    public double? Y { get; set; }

    [JsonPropertyName("button")]
    public string? Button { get; set; }

    [JsonPropertyName("delta")]
    public int? Delta { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("issuedAt")]
    public string? IssuedAt { get; set; }
}

public sealed class AdminKillProcessPayload
{
    [JsonPropertyName("pcId")]
    public string PcId { get; set; } = string.Empty;

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
