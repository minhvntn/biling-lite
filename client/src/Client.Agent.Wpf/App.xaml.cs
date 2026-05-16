using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Client.Agent.Wpf.Models;
using Client.Agent.Wpf.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Drawing = System.Drawing;
using DrawingDrawing2D = System.Drawing.Drawing2D;
using DrawingImaging = System.Drawing.Imaging;

namespace Client.Agent.Wpf;

public partial class App : Application
{
    private const string WebFilterStartMarker = "# SMB_WEB_FILTER_START";
    private const string WebFilterEndMarker = "# SMB_WEB_FILTER_END";
    private static readonly string HostsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers",
        "etc",
        "hosts");

    private readonly HttpClient _httpClient = new();
    private Mutex? _singleInstanceMutex;
    private FileLogger? _logger;
    private AgentSettings _settings = new();
    private decimal _currentHourlyRate;
    private AgentSocketService? _socketService;
    private LockScreenWindow? _lockScreenWindow;
    private MainWindow? _mainWindow;
    private ActiveMemberSession? _activeMemberSession;
    private bool _isPostpaidGuestSession;
    private bool _isAdminSession;
    private bool _isMemberWithdrawEnabled = true;
    private bool _isMemberTopupRequestEnabled = true;
    private string? _manualLockPassword;
    private bool _skipSessionClearOnExit;
    private int _lastSyncedMemberUsedSeconds;
    private int? _lastMemberRemainingMinutes;
    private readonly HashSet<int> _memberRemainingWarningSent = new();
    private readonly SemaphoreSlim _memberUsageSyncLock = new(1, 1);
    private readonly DispatcherTimer _backgroundSyncTimer = new();
    private DateTime? _readyIdleSinceUtc;
    private bool _readyAutoShutdownTriggered;
    private bool _isReadyAutoShutdownTickRunning;
    private int _readyAutoShutdownMinutes = 3;
    private string _lockScreenBackgroundMode = "none";
    private string _lockScreenBackgroundUrl = string.Empty;
    private DateTime _lastRuntimeSettingsFetchUtc = DateTime.MinValue;
    private string _currentMachineState = "LOCKED";
    private readonly DispatcherTimer _webFilterSyncTimer = new();
    private bool _isWebFilterSyncRunning;
    private bool _isMemberAutoLockInProgress;
    private DateTime _lastWebFilterFetchUtc = DateTime.MinValue;
    private string _lastWebFilterSignature = string.Empty;
    private readonly DispatcherTimer _websiteLogSyncTimer = new();
    private readonly DispatcherTimer _serviceCostSyncTimer = new();
    private bool _isWebsiteLogSyncRunning;
    private bool _isServiceCostSyncRunning;
    private bool _websiteLogEnabled;
    private DateTime _lastServiceCostRefreshUtc = DateTime.MinValue;
    private string? _cachedPcId;
    private DateTime _lastWebsiteLogSettingsFetchUtc = DateTime.MinValue;
    private DateTime _lastWebsiteHistoryScanUtc = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _websiteDomainLastSentAt =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly int[] MemberRemainingWarningThresholds = [5];
    private static readonly object MemberWarningAudioPlaybackSync = new();
    private static MediaPlayer? _memberWarningAudioPlayer;
    private static readonly DateTime WebKitEpochUtc = new(
        1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var isSingleInstance = EnsureSingleInstance();
        if (!isSingleInstance)
        {
            Shutdown();
            return;
        }

        _settings = LoadSettings();
        if (!EnsureServerEndpointConfigured())
        {
            Shutdown();
            return;
        }

        _currentHourlyRate = _settings.HourlyRate > 0 ? _settings.HourlyRate : 12000;
        _logger = new FileLogger(Path.Combine(GetLogDirectory(), "client-agent.log"));
        _ = _logger.InfoAsync("Client agent starting");

        if (_settings.EnableAutoStartup)
        {
            try
            {
                var executablePath = Environment.ProcessPath
                                     ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(executablePath))
                {
                    new StartupRegistrationService().EnsureCurrentUserStartup(executablePath);
                }
            }
            catch (Exception ex)
            {
                _ = _logger.ErrorAsync("Failed to register startup", ex);
            }
        }

        _mainWindow = new MainWindow();
        _mainWindow.SetAgentId(_settings.AgentId);
        _mainWindow.ConfigureBilling(_settings.TotalSessionMinutes, _currentHourlyRate, true);
        _mainWindow.SetWithdrawActionVisible(_isMemberWithdrawEnabled);
        _mainWindow.SetTopupRequestActionVisible(_isMemberTopupRequestEnabled);
        _mainWindow.SetConnectionStatus("Connecting...");
        _mainWindow.SetMachineState("LOCKED");
        _mainWindow.SetLastCommand("Boot sequence");
        MainWindow = _mainWindow;
        _mainWindow.Show();
        TrackMachineState("LOCKED");

        _lockScreenWindow = new LockScreenWindow();
        _lockScreenWindow.ApplyBackgroundConfiguration(_lockScreenBackgroundMode, _lockScreenBackgroundUrl);
        _lockScreenWindow.SetCurrentServerUrl(_settings.ServerUrl);
        _lockScreenWindow.PrepareForLock();

        StartSocketService();

        // Defer non-critical startup work so lock screen becomes interactive sooner.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(StartDeferredStartupTasks));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (!_skipSessionClearOnExit)
            {
                Task.Run(() => TrackAndClearMemberSessionAsync("APP_EXIT")).GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Keep shutdown path resilient.
        }

        _mainWindow?.AllowShutdown();
        _lockScreenWindow?.AllowShutdown();
        _backgroundSyncTimer.Stop();
        _webFilterSyncTimer.Stop();
        _websiteLogSyncTimer.Stop();
        _serviceCostSyncTimer.Stop();

        try
        {
            _socketService?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // swallow during shutdown
        }

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _httpClient.Dispose();
        _memberUsageSyncLock.Dispose();

        base.OnExit(e);
    }

    private void StartDeferredStartupTasks()
    {
        _backgroundSyncTimer.Interval = TimeSpan.FromSeconds(10);
        _backgroundSyncTimer.Tick += BackgroundSyncTimer_Tick;
        _backgroundSyncTimer.Start();
        _ = RefreshClientRuntimeSettingsAsync();

        _webFilterSyncTimer.Interval = TimeSpan.FromSeconds(180);
        _webFilterSyncTimer.Tick += WebFilterSyncTimer_Tick;
        _webFilterSyncTimer.Start();
        _ = RefreshAndApplyWebFilterAsync(true);

        _websiteLogSyncTimer.Interval = TimeSpan.FromSeconds(600);
        _websiteLogSyncTimer.Tick += WebsiteLogSyncTimer_Tick;
        _websiteLogSyncTimer.Start();
        _ = SyncWebsiteLogsAsync(true);

        _serviceCostSyncTimer.Interval = TimeSpan.FromSeconds(60);
        _serviceCostSyncTimer.Tick += ServiceCostSyncTimer_Tick;
        _serviceCostSyncTimer.Start();
        _ = RefreshServiceCostUiAsync(force: true);

        // Clean up stale website-log snapshot files from previous runs
        _ = Task.Run(CleanupStaleWebsiteLogSnapshots);
    }

    private void StartSocketService()
    {
        if (_logger is null)
        {
            return;
        }

        _socketService = new AgentSocketService(
            _settings,
            _logger,
            HandleCommandAsync,
            OnConnectionStatusChanged,
            OnAdminNotificationReceived,
            HandleCaptureScreenshotRequestedAsync,
            HandleLiveFrameRequestedAsync,
            HandleRemoteInputRequestedAsync,
            rate =>
            {
                _currentHourlyRate = rate;
                Dispatcher.Invoke(() => _mainWindow?.UpdateHourlyRate(_currentHourlyRate));
            },
            isEnabled =>
            {
                Dispatcher.Invoke(() => _lockScreenWindow?.SetGuestLoginEnabled(isEnabled));
            },
            elapsed =>
            {
                Dispatcher.Invoke(() => _mainWindow?.SynchronizeUsedDuration(elapsed));
                _ = RefreshServiceCostUiAsync(force: false);
            },
            resumeGuest =>
            {
                if (!resumeGuest)
                {
                    return;
                }

                Dispatcher.Invoke(ResumeGuestSessionFromServer);
            },
            OnMemberAccountChangedFromServer);

        _socketService.GetRunningAppsHandler = HandleGetRunningAppsRequestedAsync;
        _socketService.KillProcessHandler = HandleKillProcessRequestedAsync;

        _ = Task.Run(async () =>
        {
            try
            {
                await _socketService.StartAsync();
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("Socket initialization failed", ex);
                Dispatcher.Invoke(() => _mainWindow?.SetConnectionStatus("Disconnected"));
            }
        });
    }

    private async Task ReconnectSocketServiceAsync()
    {
        if (_socketService is not null)
        {
            try
            {
                await _socketService.DisposeAsync();
            }
            catch
            {
                // ignore reconnect teardown error
            }

            _socketService = null;
        }

        Dispatcher.Invoke(() => _mainWindow?.SetConnectionStatus("Reconnecting..."));
        StartSocketService();
    }

    private bool EnsureSingleInstance()
    {
        var mutexName = $"Global\\ServerManagerBilling.Agent.{Environment.MachineName}";
        _singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        return createdNew;
    }

    private AgentSettings LoadSettings()
    {
        var writableSettingsPath = GetWritableSettingsPath();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile(writableSettingsPath, optional: true, reloadOnChange: true)
            .Build();

        var settings = new AgentSettings();
        configuration.GetSection("Agent").Bind(settings);

        if (string.IsNullOrWhiteSpace(settings.AgentId))
        {
            settings.AgentId = Environment.MachineName;
        }

        return settings;
    }

    private async Task<(bool Success, string? Message)> HandleCommandAsync(CommandExecutePayload payload)
    {
        try
        {
            var commandType = payload.Type?.Trim().ToUpperInvariant();
            switch (commandType)
            {
                case "OPEN":
                    await TrackAndClearMemberSessionAsync("SERVER_OPEN");
                    _isPostpaidGuestSession = true;
                    if (payload.HourlyRate is > 0)
                    {
                        _currentHourlyRate = payload.HourlyRate.Value;
                        Dispatcher.Invoke(() =>
                            _mainWindow?.ConfigureBilling(
                                _settings.TotalSessionMinutes,
                                _currentHourlyRate,
                                true));
                    }
                    Dispatcher.Invoke(UnlockMachine);
                    return (true, "opened");

                case "LOCK":
                    await TrackAndClearMemberSessionAsync("SERVER_LOCK");
                    _isPostpaidGuestSession = false;
                    Dispatcher.Invoke(() => LockMachine(force: true));
                    return (true, "locked");

                case "RESTART":
                    var shouldPreserveGuestSession =
                        _isPostpaidGuestSession &&
                        _activeMemberSession is null &&
                        !_isAdminSession;
                    if (shouldPreserveGuestSession)
                    {
                        _skipSessionClearOnExit = true;
                        _ = _logger?.InfoAsync(
                            "Preserving guest session state on restart for auto-resume");
                    }
                    else
                    {
                        await TrackAndClearMemberSessionAsync("SERVER_RESTART");
                    }
                    _ = _logger?.InfoAsync("Executing RESTART command");
                    TriggerSystemRestart();
                    return (true, "restart triggered");

                case "SHUTDOWN":
                    await TrackAndClearMemberSessionAsync("SERVER_SHUTDOWN");
                    _ = _logger?.InfoAsync("Executing SHUTDOWN command");
                    TriggerSystemShutdown();
                    return (true, "shutdown triggered");

                case "CLOSE_APPS":
                    var closedCount = CloseUserApplications();
                    Dispatcher.Invoke(() =>
                        _mainWindow?.SetLastCommand($"CLOSE_APPS @ {DateTime.Now:HH:mm:ss}"));
                    return (true, $"closed {closedCount} app(s)");

                case "PAUSE":
                    await SyncActiveMemberUsageAsync("SERVER_PAUSE", true);
                    Dispatcher.Invoke(PauseMachine);
                    return (true, "paused");

                case "RESUME":
                    _isPostpaidGuestSession = false;
                    if (payload.HourlyRate is > 0)
                    {
                        _currentHourlyRate = payload.HourlyRate.Value;
                        Dispatcher.Invoke(() =>
                            _mainWindow?.ConfigureBilling(
                                _settings.TotalSessionMinutes,
                                _currentHourlyRate));
                    }
                    Dispatcher.Invoke(UnlockMachine);
                    Dispatcher.Invoke(() =>
                        _mainWindow?.SetLastCommand($"RESUME @ {DateTime.Now:HH:mm:ss}"));
                    return (true, "resumed");

                default:
                    return (false, "Unsupported command type");
            }
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("Failed to execute command", ex);
            return (false, ex.Message);
        }
    }

    public async void RequestLockFromClientUi(string reason)
    {
        if (_isPostpaidGuestSession)
        {
            Dispatcher.Invoke(() =>
            {
                _mainWindow?.SetLastCommand($"B? QUA KHÓA (khách tr? sau) @ {DateTime.Now:HH:mm:ss}");
            });
            return;
        }

        ClearManualLockState();
        await TrackAndClearMemberSessionAsync(reason);
        Dispatcher.Invoke(() =>
        {
            LockMachine(force: false);
            _mainWindow?.SetLastCommand($"{reason} @ {DateTime.Now:HH:mm:ss}");
        });
    }

    public void RequestManualLockFromClientUi()
    {
        var lockPassword = PromptForManualLockPassword();
        if (lockPassword is null)
        {
            return;
        }

        _manualLockPassword = lockPassword;
        _lockScreenWindow?.SetManualUnlockMode(true);
        _lockScreenWindow?.PrepareForLock();
        _mainWindow?.SetLastCommand($"KHÓA TH? CÔNG @ {DateTime.Now:HH:mm:ss}");
    }

    public LoginAttemptResult TryUnlockWithManualPassword(string password)
    {
        if (string.IsNullOrEmpty(_manualLockPassword))
        {
            return new LoginAttemptResult(false, "Không có khóa th? công dang ho?t d?ng.");
        }

        if (string.IsNullOrEmpty(password))
        {
            return new LoginAttemptResult(false, "Vui lòng nh?p m?t mã.");
        }

        if (!string.Equals(_manualLockPassword, password, StringComparison.Ordinal))
        {
            return new LoginAttemptResult(false, "M?t mã không dúng.");
        }

        ClearManualLockState();
        _lockScreenWindow?.Hide();
        _mainWindow?.SetLastCommand($"M? KHÓA TH? CÔNG @ {DateTime.Now:HH:mm:ss}");
        return new LoginAttemptResult(true, "M? khóa thành công.");
    }

    public async Task<LoginAttemptResult> TryUnlockFromLockScreenAsync(
        string username,
        string password)
    {
        if (!string.IsNullOrEmpty(_manualLockPassword))
        {
            return new LoginAttemptResult(false, "Vui lòng nh?p m?t mã khóa máy dã d?t.");
        }

        var normalizedUsername = username.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrEmpty(password))
        {
            return new LoginAttemptResult(false, "Vui long nhap ten dang nhap va mat khau.");
        }

        // Check if this is an agent-admin login (verified by server).
        bool isAgentAdmin = false;
        var adminEndpointUnavailable = false;
        var adminCheckException = false;

        try
        {
            using var adminCheckResp = await _httpClient.PostAsJsonAsync(
                BuildApiUrl("/settings/agent-admin/login"),
                new { username = normalizedUsername, password });

            if (adminCheckResp.IsSuccessStatusCode)
            {
                isAgentAdmin = true;
            }
            else if (adminCheckResp.StatusCode is HttpStatusCode.NotFound or
                     HttpStatusCode.MethodNotAllowed or
                     HttpStatusCode.NotImplemented)
            {
                adminEndpointUnavailable = true;
                if (_logger is not null)
                {
                    await _logger.InfoAsync(
                        $"Agent admin endpoint unavailable: {(int)adminCheckResp.StatusCode}");
                }
            }
        }
        catch (Exception ex)
        {
            adminCheckException = true;
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Agent admin login check failed", ex);
            }
        }

        // Backward-compatible fallback when server is unreachable
        // or backend is old and does not expose /settings/agent-admin/login.
        if (!isAgentAdmin && (adminCheckException || adminEndpointUnavailable))
        {
            if (normalizedUsername.Equals("administrator", StringComparison.OrdinalIgnoreCase) && password == "isadmin")
            {
                isAgentAdmin = true;
                if (_logger is not null)
                {
                    await _logger.InfoAsync("Using local fallback admin login (administrator/isadmin)");
                }
            }
        }

        if (isAgentAdmin)
        {
            await TrackAndClearMemberSessionAsync("ADMIN_LOCKSCREEN_LOGIN");
            _isAdminSession = true;
            await ReportAdminPresenceAsync(true, normalizedUsername);
            _activeMemberSession = null;
            _isPostpaidGuestSession = false;
            _lastSyncedMemberUsedSeconds = 0;
            ResetMemberRemainingWarnings();
            Dispatcher.Invoke(() =>
            {
                _mainWindow?.ConfigureBilling(
                    _settings.TotalSessionMinutes,
                    _currentHourlyRate,
                    true);
                _mainWindow?.SetMemberInfo("Admin", "ADMIN");
                UnlockMachine();
                _mainWindow?.SetLastCommand($"ADMIN LOGIN @ {DateTime.Now:HH:mm:ss}");
            });

            return new LoginAttemptResult(true, "Dang nhap quan tri thanh cong.");
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl("/members/login"),
                new
                {
                    username = normalizedUsername,
                    password,
                    agentId = _settings.AgentId,
                });

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await ReadErrorMessageAsync(response);
                if (_logger is not null)
                {
                    await _logger.InfoAsync(
                        $"Member login failed: {(int)response.StatusCode} {response.StatusCode} user={normalizedUsername}");
                }

                return new LoginAttemptResult(
                    false,
                    string.IsNullOrWhiteSpace(errorText)
                        ? "Dang nhap khong thanh cong."
                        : errorText);
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (payload.TryGetProperty("hourlyRate", out var rateElement) &&
                TryReadDecimal(rateElement, out var hourlyRate))
            {
                _currentHourlyRate = hourlyRate;
            }

            if (!payload.TryGetProperty("member", out var memberElement))
            {
                return new LoginAttemptResult(false, "Khong doc duoc thong tin hoi vien.");
            }

            var member = JsonSerializer.Deserialize<MemberLoginItem>(
                memberElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (member is null)
            {
                return new LoginAttemptResult(false, "Khong giai ma duoc thong tin hoi vien.");
            }

            if (string.IsNullOrWhiteSpace(member.Id))
            {
                return new LoginAttemptResult(false, "Thieu ma hoi vien tu may chu.");
            }

            _activeMemberSession = new ActiveMemberSession
            {
                MemberId = member.Id,
                Username = member.Username,
                FullName = member.FullName,
                Rank = member.Rank,
            };
            _isAdminSession = false;
            _isPostpaidGuestSession = false;
            _lastSyncedMemberUsedSeconds = 60;
            ResetMemberRemainingWarnings();

            var presenceResult = await ReportMemberPresenceAsync(
                _activeMemberSession,
                true);
            if (!presenceResult.Success)
            {
                _activeMemberSession = null;
                _isPostpaidGuestSession = false;
                _isAdminSession = false;
                _lastSyncedMemberUsedSeconds = 0;
                ResetMemberRemainingWarnings();
                return new LoginAttemptResult(false, presenceResult.Message);
            }

            Dispatcher.Invoke(() =>
            {
                var totalMinutes = ComputeMinutesFromBalance(member.Balance, _currentHourlyRate);
                if (totalMinutes <= 0)
                {
                    totalMinutes = Math.Max(1, member.PlaySeconds / 60);
                }

                _mainWindow?.ConfigureBilling(totalMinutes, _currentHourlyRate, true);
                _mainWindow?.SetUpfrontUsedDuration();
                _mainWindow?.SetMemberInfo(member.Username, member.Rank);
                UnlockMachine();
                _mainWindow?.SetLastCommand(
                    $"MEMBER LOGIN {member.Username} @ {DateTime.Now:HH:mm:ss}");
            });
            EvaluateMemberRemainingTimeWarnings();
            _ = EnforceMemberAutoLockIfNoRemainingTimeAsync("MEMBER_LOGIN");

            return new LoginAttemptResult(
                true,
                $"Dang nhap thanh cong: {member.Username}");
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Lock screen member login failed", ex);
            }

            return new LoginAttemptResult(false, $"Khong the ket noi server: {ex.Message}");
        }
    }
public async Task<LoginAttemptResult> TryUnlockAsGuestAsync()
    {
        var primaryAgentId = (_settings.AgentId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(primaryAgentId))
        {
            primaryAgentId = Environment.MachineName;
        }

        var primaryResult = await TryUnlockAsGuestWithAgentIdAsync(primaryAgentId);
        if (primaryResult.Success)
        {
            return primaryResult;
        }

        var fallbackAgentId = Environment.MachineName.Trim();
        if (!primaryAgentId.Equals(fallbackAgentId, StringComparison.OrdinalIgnoreCase) &&
            ShouldRetryGuestLoginWithFallback(primaryResult.Message))
        {
            if (_logger is not null)
            {
                await _logger.InfoAsync(
                    $"Guest login fallback: primaryAgentId={primaryAgentId}, fallbackAgentId={fallbackAgentId}");
            }

            var fallbackResult = await TryUnlockAsGuestWithAgentIdAsync(fallbackAgentId);
            if (fallbackResult.Success)
            {
                return fallbackResult;
            }

            return new LoginAttemptResult(
                false,
                $"{fallbackResult.Message} (Thu lai bang AgentId khac: {fallbackAgentId})");
        }

        return primaryResult;
    }

    private async Task<LoginAttemptResult> TryUnlockAsGuestWithAgentIdAsync(string agentId)
    {
        try
        {
            using var response = await _httpClient.PostAsync(
                BuildApiUrl($"/pcs/{agentId}/guest-login"),
                null);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await ReadErrorMessageAsync(response);
                if (_logger is not null)
                {
                    await _logger.InfoAsync(
                        $"Guest login failed: {(int)response.StatusCode} {response.StatusCode} agentId={agentId}");
                }

                return new LoginAttemptResult(
                    false,
                    string.IsNullOrWhiteSpace(errorText)
                        ? "Khong the dang nhap khach vang lai."
                        : errorText);
            }

            _activeMemberSession = null;
            _isAdminSession = false;
            _isPostpaidGuestSession = true;
            _lastSyncedMemberUsedSeconds = 0;
            ResetMemberRemainingWarnings();

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (payload.TryGetProperty("pc", out var pcElement) &&
                pcElement.TryGetProperty("group", out var groupElement) &&
                groupElement.TryGetProperty("hourlyRate", out var rateElement) &&
                TryReadDecimal(rateElement, out var guestHourlyRate))
            {
                _currentHourlyRate = guestHourlyRate;
            }

            Dispatcher.Invoke(() =>
            {
                _mainWindow?.ConfigureBilling(
                    _settings.TotalSessionMinutes,
                    _currentHourlyRate,
                    true);

                _mainWindow?.SetMemberInfo(null, null);

                UnlockMachine();
                _mainWindow?.SetLastCommand($"GUEST LOGIN @ {DateTime.Now:HH:mm:ss}");
            });

            _ = ReportGuestPresenceAsync(true);

            return new LoginAttemptResult(true, "Dang nhap khach vang lai thanh cong.");
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Lock screen guest login failed", ex);
            }

            return new LoginAttemptResult(false, $"Khong the ket noi server: {ex.Message}");
        }
    }

    private static bool ShouldRetryGuestLoginWithFallback(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.ToLowerInvariant();
        return normalized.Contains("khong tim thay may") ||
               normalized.Contains("not found") ||
               normalized.Contains("machine not found");
    }

    private static int ComputeMinutesFromBalance(decimal balance, decimal hourlyRate)
    {
        if (balance <= 0 || hourlyRate <= 0)
        {
            return 0;
        }

        var minutes = (int)Math.Floor((balance / hourlyRate) * 60m);
        return Math.Max(0, minutes);
    }

    private static int ComputeMinutesFromPlaySeconds(int playSeconds)
    {
        if (playSeconds <= 0)
        {
            return 0;
        }

        return Math.Max(0, (int)Math.Floor(playSeconds / 60d));
    }

    private static int ComputeUsedMinutesFromSeconds(int usedSeconds)
    {
        if (usedSeconds <= 0)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(usedSeconds / 60d));
    }

    private int ComputeRemainingMinutesFromMemberSnapshot(MemberLoginItem member)
    {
        var balanceMinutes = ComputeMinutesFromBalance(member.Balance, _currentHourlyRate);
        var playSecondsMinutes = ComputeMinutesFromPlaySeconds(member.PlaySeconds);
        return Math.Max(0, balanceMinutes + playSecondsMinutes);
    }

    private void SynchronizeMemberBillingFromServer(MemberLoginItem member, int usedSecondsNow)
    {
        var remainingMinutes = ComputeRemainingMinutesFromMemberSnapshot(member);
        var usedMinutes = ComputeUsedMinutesFromSeconds(usedSecondsNow);
        var totalMinutes = Math.Max(1, remainingMinutes + usedMinutes);

        Dispatcher.Invoke(() =>
        {
            _mainWindow?.ConfigureBilling(totalMinutes, _currentHourlyRate, false);
        });
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetDecimal(out var numericValue))
        {
            value = numericValue;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var raw = element.GetString();
            if (!string.IsNullOrWhiteSpace(raw) &&
                (decimal.TryParse(
                    raw,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var invariantValue) ||
                 decimal.TryParse(
                     raw,
                     NumberStyles.Number,
                     CultureInfo.CurrentCulture,
                     out invariantValue)))
            {
                value = invariantValue;
                return true;
            }
        }

        value = 0;
        return false;
    }

    public async void OpenServicesPanelFromClientUi()
    {
        try
        {
            var pcContext = await ResolveCurrentPcContextAsync();
            if (pcContext is null)
            {
                MessageBox.Show(
                    "Không xác d?nh du?c máy tr?m hi?n t?i d? g?i d?ch v?.",
                    "D?ch v?",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var (pcId, pcName, activeSessionId) = pcContext.Value;
            if (string.IsNullOrWhiteSpace(activeSessionId))
            {
                MessageBox.Show(
                    "Máy chua có phiên dang s? d?ng d? g?i d?ch v?.",
                    "D?ch v?",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await ShowServiceOrderDialogAsync(pcId, pcName, activeSessionId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không th? m? màn hình d?ch v?: {ex.Message}",
                "D?ch v?",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task<(string PcId, string PcName, string? ActiveSessionId)?> ResolveCurrentPcContextAsync()
    {
        var response = await _httpClient.GetFromJsonAsync<ClientPcListResponse>(BuildApiUrl("/pcs"));
        if (response?.Items is null || response.Items.Count == 0)
        {
            return null;
        }

        var preferredAgentId = (_settings.AgentId ?? string.Empty).Trim();
        var fallbackAgentId = Environment.MachineName.Trim();

        ClientPcItemDto? selected = null;
        if (!string.IsNullOrWhiteSpace(preferredAgentId))
        {
            selected = response.Items.FirstOrDefault(x =>
                string.Equals(x.AgentId, preferredAgentId, StringComparison.OrdinalIgnoreCase));
        }

        if (selected is null && !string.IsNullOrWhiteSpace(fallbackAgentId))
        {
            selected = response.Items.FirstOrDefault(x =>
                string.Equals(x.AgentId, fallbackAgentId, StringComparison.OrdinalIgnoreCase));
        }

        if (selected is null && !string.IsNullOrWhiteSpace(_cachedPcId))
        {
            selected = response.Items.FirstOrDefault(x =>
                string.Equals(x.Id, _cachedPcId, StringComparison.OrdinalIgnoreCase));
        }

        if (selected is null)
        {
            return null;
        }

        _cachedPcId = selected.Id;
        var pcName = string.IsNullOrWhiteSpace(selected.Name) ? selected.AgentId : selected.Name;
        return (selected.Id, pcName, selected.ActiveSession?.Id);
    }

    private async Task<List<ClientServiceItemDto>> LoadActiveServiceItemsAsync()
    {
        var response = await _httpClient.GetFromJsonAsync<ClientServiceItemsResponse>(
            BuildApiUrl("/services/items"));

        if (response?.Items is null || response.Items.Count == 0)
        {
            return new List<ClientServiceItemDto>();
        }

        return response.Items
            .Where(x => x.IsActive)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.Category) ? "-" : x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<ClientPcServiceOrderItem>> LoadUnpaidServiceOrdersAsync(
        string pcId,
        string activeSessionId)
    {
        if (string.IsNullOrWhiteSpace(pcId) || string.IsNullOrWhiteSpace(activeSessionId))
        {
            return new List<ClientPcServiceOrderItem>();
        }

        var response = await _httpClient.GetFromJsonAsync<ClientPcServiceOrdersResponse>(
            BuildApiUrl($"/services/pcs/{pcId}/orders?limit=200"));

        if (response?.Items is null || response.Items.Count == 0)
        {
            return new List<ClientPcServiceOrderItem>();
        }

        return response.Items
            .Where(x =>
                !x.IsPaid &&
                string.Equals(x.SessionId, activeSessionId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x =>
            {
                if (DateTime.TryParse(x.CreatedAt, out var createdAt))
                {
                    return createdAt;
                }

                return DateTime.MaxValue;
            })
            .ThenBy(x => x.ServiceItem?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ResolveServiceOrderRequester()
    {
        var requester = (_settings.AgentId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requester))
        {
            requester = Environment.MachineName.Trim();
        }

        return string.IsNullOrWhiteSpace(requester)
            ? "client.agent"
            : requester;
    }

    private static bool IsClientOwnedServiceOrder(
        ClientPcServiceOrderItem order,
        string requester)
    {
        var createdBy = (order.CreatedBy ?? string.Empty).Trim();
        var normalizedRequester = (requester ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(createdBy) || string.IsNullOrWhiteSpace(normalizedRequester))
        {
            return false;
        }

        return createdBy.Equals(normalizedRequester, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshServiceCostUiAsync(bool force)
    {
        if (_isServiceCostSyncRunning)
        {
            return;
        }

        if (!force)
        {
            var stateCode = (_currentMachineState ?? string.Empty).Trim().ToUpperInvariant();
            var hasActiveUsageSession = _activeMemberSession is not null || _isPostpaidGuestSession;
            if (stateCode is not "IN_USE" || !hasActiveUsageSession)
            {
                return;
            }
        }

        if (!force && (DateTime.UtcNow - _lastServiceCostRefreshUtc).TotalSeconds < 25)
        {
            return;
        }

        _isServiceCostSyncRunning = true;
        _lastServiceCostRefreshUtc = DateTime.UtcNow;
        try
        {
            decimal serviceCost = 0;
            var serviceOrderCount = 0;
            var pcContext = await ResolveCurrentPcContextAsync();
            if (pcContext is { } context && !string.IsNullOrWhiteSpace(context.ActiveSessionId))
            {
                var unpaidOrders = await LoadUnpaidServiceOrdersAsync(context.PcId, context.ActiveSessionId);
                serviceCost = unpaidOrders.Sum(x => Math.Max(0, x.LineTotal));
                serviceOrderCount = unpaidOrders.Sum(x => Math.Max(0, x.Quantity));
            }

            Dispatcher.Invoke(() =>
            {
                _mainWindow?.SetServiceCost(serviceCost);
                _mainWindow?.SetServiceOrderCount(serviceOrderCount);
            });
        }
        catch
        {
            // Keep UI responsive when service API is temporarily unavailable.
        }
        finally
        {
            _isServiceCostSyncRunning = false;
        }
    }

    private async Task ShowServiceOrderDialogAsync(string pcId, string pcName, string activeSessionId)
    {
        var serviceItemsTask = LoadActiveServiceItemsAsync();
        var existingOrdersTask = LoadUnpaidServiceOrdersAsync(pcId, activeSessionId);
        await Task.WhenAll(serviceItemsTask, existingOrdersTask);

        var activeItems = serviceItemsTask.Result;
        var existingOrders = existingOrdersTask.Result;

        if (activeItems.Count == 0)
        {
            MessageBox.Show(
                "Hi?n chua có d?ch v? dang bán.",
                "D?ch v?",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var serviceRequester = ResolveServiceOrderRequester();

        var existingSummaries = existingOrders
            .Where(x => !string.IsNullOrWhiteSpace(x.ServiceItem?.Id))
            .GroupBy(x => x.ServiceItem!.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new ClientExistingServiceSummary
                {
                    Quantity = g.Sum(x => Math.Max(0, x.Quantity)),
                    Amount = g.Sum(x => Math.Max(0, x.LineTotal)),
                },
                StringComparer.OrdinalIgnoreCase);

        var clientOwnedSummaries = existingOrders
            .Where(x => IsClientOwnedServiceOrder(x, serviceRequester))
            .Where(x => !string.IsNullOrWhiteSpace(x.ServiceItem?.Id))
            .GroupBy(x => x.ServiceItem!.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new ClientExistingServiceSummary
                {
                    Quantity = g.Sum(x => Math.Max(0, x.Quantity)),
                    Amount = g.Sum(x => Math.Max(0, x.LineTotal)),
                },
                StringComparer.OrdinalIgnoreCase);

        var orderedPreview = existingOrders.Count == 0
            ? "Chua g?i d?ch v?."
            : string.Join(
                " | ",
                existingOrders
                    .GroupBy(x => x.ServiceItem?.Name ?? "D?ch v?")
                    .Select(g =>
                    {
                        var quantity = g.Sum(x => Math.Max(0, x.Quantity));
                        var amount = g.Sum(x => Math.Max(0, x.LineTotal));
                        return $"{g.Key} x{quantity} ({amount:N0})";
                    }));

        var rows = new ObservableCollection<ClientServiceOrderSelectionRow>(
            activeItems
                .Select(item =>
                {
                    existingSummaries.TryGetValue(item.Id, out var summary);
                    clientOwnedSummaries.TryGetValue(item.Id, out var clientOwnedSummary);
                    return ClientServiceOrderSelectionRow.FromServiceItem(item, summary, clientOwnedSummary);
                })
                .OrderByDescending(x => x.ExistingQuantity)
                .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase));

        var dialog = new Window
        {
            Title = $"D?ch v? - {pcName}",
            Width = 920,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            MinWidth = 820,
            MinHeight = 520,
            Owner = _mainWindow,
            ShowInTaskbar = false,
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = $"Máy tr?m: {pcName}",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(titleText, 0);
        root.Children.Add(titleText);

        var orderedPreviewText = new TextBlock
        {
            Text = $"Ðã g?i: {orderedPreview}",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(orderedPreviewText, 1);
        root.Children.Add(orderedPreviewText);

        var serviceGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = rows,
            Margin = new Thickness(0, 0, 0, 10),
        };

        serviceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "D?ch v?",
            Width = new DataGridLength(2.0, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(ClientServiceOrderSelectionRow.ServiceName)),
            IsReadOnly = true,
        });
        serviceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Danh m?c",
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(ClientServiceOrderSelectionRow.Category)),
            IsReadOnly = true,
        });
        serviceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ðon giá",
            Width = 110,
            Binding = new Binding(nameof(ClientServiceOrderSelectionRow.UnitPriceText)),
            IsReadOnly = true,
        });
        serviceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ðã g?i",
            Width = 140,
            Binding = new Binding(nameof(ClientServiceOrderSelectionRow.ExistingText)),
            IsReadOnly = true,
        });
        serviceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Nguon",
            Width = 95,
            Binding = new Binding(nameof(ClientServiceOrderSelectionRow.SourceText)),
            IsReadOnly = true,
        });

        var quantityTemplateColumn = new DataGridTemplateColumn
        {
            Header = "S? lu?ng",
            Width = 150,
        };

        var quantityPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
        quantityPanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        quantityPanelFactory.SetValue(StackPanel.HorizontalAlignmentProperty, HorizontalAlignment.Center);

        var decreaseButtonFactory = new FrameworkElementFactory(typeof(Button));
        decreaseButtonFactory.SetValue(Button.ContentProperty, "-");
        decreaseButtonFactory.SetValue(Button.WidthProperty, 30d);
        decreaseButtonFactory.SetValue(Button.HeightProperty, 28d);
        decreaseButtonFactory.SetValue(Button.PaddingProperty, new Thickness(0));
        decreaseButtonFactory.SetValue(Button.MarginProperty, new Thickness(0, 0, 6, 0));
        decreaseButtonFactory.SetValue(Button.FontWeightProperty, FontWeights.SemiBold);
        decreaseButtonFactory.SetValue(Button.FontSizeProperty, 14d);
        decreaseButtonFactory.SetValue(Button.ForegroundProperty, Brushes.White);
        decreaseButtonFactory.SetValue(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(239, 68, 68)));
        decreaseButtonFactory.SetValue(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(185, 28, 28)));
        decreaseButtonFactory.SetValue(Button.ToolTipProperty, "Chi huy mon do may tram da tu goi.");
        decreaseButtonFactory.SetBinding(Button.IsEnabledProperty, new Binding(nameof(ClientServiceOrderSelectionRow.CanDecrease)));
        decreaseButtonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if ((sender as FrameworkElement)?.DataContext is ClientServiceOrderSelectionRow row)
            {
                row.DecreaseQuantity();
            }
        }));

        var quantityValueFactory = new FrameworkElementFactory(typeof(TextBlock));
        quantityValueFactory.SetValue(TextBlock.WidthProperty, 48d);
        quantityValueFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        quantityValueFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        quantityValueFactory.SetValue(TextBlock.FontSizeProperty, 14d);
        quantityValueFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        quantityValueFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ClientServiceOrderSelectionRow.Quantity)));

        var increaseButtonFactory = new FrameworkElementFactory(typeof(Button));
        increaseButtonFactory.SetValue(Button.ContentProperty, "+");
        increaseButtonFactory.SetValue(Button.WidthProperty, 30d);
        increaseButtonFactory.SetValue(Button.HeightProperty, 28d);
        increaseButtonFactory.SetValue(Button.PaddingProperty, new Thickness(0));
        increaseButtonFactory.SetValue(Button.MarginProperty, new Thickness(6, 0, 0, 0));
        increaseButtonFactory.SetValue(Button.FontWeightProperty, FontWeights.SemiBold);
        increaseButtonFactory.SetValue(Button.FontSizeProperty, 14d);
        increaseButtonFactory.SetValue(Button.ForegroundProperty, Brushes.White);
        increaseButtonFactory.SetValue(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(34, 197, 94)));
        increaseButtonFactory.SetValue(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(22, 163, 74)));
        increaseButtonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if ((sender as FrameworkElement)?.DataContext is ClientServiceOrderSelectionRow row)
            {
                row.IncreaseQuantity();
            }
        }));

        quantityPanelFactory.AppendChild(decreaseButtonFactory);
        quantityPanelFactory.AppendChild(quantityValueFactory);
        quantityPanelFactory.AppendChild(increaseButtonFactory);
        quantityTemplateColumn.CellTemplate = new DataTemplate
        {
            VisualTree = quantityPanelFactory,
        };
        serviceGrid.Columns.Add(quantityTemplateColumn);

        serviceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Thành ti?n",
            Width = 130,
            Binding = new Binding(nameof(ClientServiceOrderSelectionRow.LineTotalText)),
            IsReadOnly = true,
        });

        Grid.SetRow(serviceGrid, 2);
        root.Children.Add(serviceGrid);

        var notePanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
        };
        notePanel.Children.Add(new TextBlock
        {
            Text = "Ghi chú (không b?t bu?c):",
            Margin = new Thickness(0, 0, 0, 4),
        });
        var noteTextBox = new TextBox
        {
            Height = 52,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        notePanel.Children.Add(noteTextBox);
        Grid.SetRow(notePanel, 3);
        root.Children.Add(notePanel);

        var summaryTextBlock = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var statusPanel = new StackPanel();
        statusPanel.Children.Add(summaryTextBlock);
        statusPanel.Children.Add(errorTextBlock);
        Grid.SetRow(statusPanel, 4);
        root.Children.Add(statusPanel);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var orderButton = new Button
        {
            Content = "G?i d?ch v?",
            Width = 130,
            Height = 34,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(29, 78, 216)),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        var cancelButton = new Button
        {
            Content = "H?y",
            Width = 90,
            Height = 34,
            FontWeight = FontWeights.SemiBold,
            IsCancel = true,
        };
        buttonPanel.Children.Add(orderButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 5);
        root.Children.Add(buttonPanel);

        void RefreshSummary()
        {
            var selectedRows = rows.Where(x => x.Quantity != 0).ToList();
            var selectedItemCount = selectedRows.Count;
            var totalAdded = selectedRows.Where(x => x.Quantity > 0).Sum(x => x.Quantity);
            var totalCanceled = selectedRows.Where(x => x.Quantity < 0).Sum(x => -x.Quantity);
            var netAmount = selectedRows.Sum(x => x.LineTotal);
            summaryTextBlock.Text =
                $"Da chon {selectedItemCount} mon | Goi them: {totalAdded} | Huy: {totalCanceled} | Chenh lech: {netAmount:N0} VND";
        }

        void RowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ClientServiceOrderSelectionRow.Quantity))
            {
                RefreshSummary();
            }
        }

        foreach (var row in rows)
        {
            row.PropertyChanged += RowPropertyChanged;
        }

        orderButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            var selectedRows = rows
                .Where(x => x.Quantity != 0)
                .ToList();

            if (selectedRows.Count == 0)
            {
                errorTextBlock.Text = "Hay chon so luong de goi (+) hoac huy (-).";
                return;
            }

            orderButton.IsEnabled = false;
            cancelButton.IsEnabled = false;
            try
            {
                var failedItems = new List<string>();
                var successCount = 0;
                var positiveRows = selectedRows.Where(x => x.Quantity > 0).ToList();
                var negativeRows = selectedRows.Where(x => x.Quantity < 0).ToList();

                foreach (var row in positiveRows)
                {
                    using var response = await _httpClient.PostAsJsonAsync(
                        BuildApiUrl($"/services/pcs/{pcId}/orders"),
                        new
                        {
                            serviceItemId = row.ServiceItemId,
                            quantity = row.Quantity,
                            note = string.IsNullOrWhiteSpace(noteTextBox.Text) ? null : noteTextBox.Text.Trim(),
                            requestedBy = serviceRequester,
                        });

                    if (!response.IsSuccessStatusCode)
                    {
                        var err = await ReadErrorMessageAsync(response);
                        failedItems.Add(
                            string.IsNullOrWhiteSpace(err)
                                ? $"{row.ServiceName} ({(int)response.StatusCode})"
                                : $"{row.ServiceName}: {err}");
                        continue;
                    }

                    successCount++;
                }

                foreach (var row in negativeRows)
                {
                    using var response = await _httpClient.PostAsJsonAsync(
                        BuildApiUrl($"/services/pcs/{pcId}/orders/cancel"),
                        new
                        {
                            serviceItemId = row.ServiceItemId,
                            quantity = Math.Abs(row.Quantity),
                            sessionId = activeSessionId,
                            note = string.IsNullOrWhiteSpace(noteTextBox.Text) ? null : noteTextBox.Text.Trim(),
                            requestedBy = serviceRequester,
                        });

                    if (!response.IsSuccessStatusCode)
                    {
                        var err = await ReadErrorMessageAsync(response);
                        failedItems.Add(
                            string.IsNullOrWhiteSpace(err)
                                ? $"{row.ServiceName} ({(int)response.StatusCode})"
                                : $"{row.ServiceName}: {err}");
                        continue;
                    }

                    successCount++;
                }

                if (successCount > 0)
                {
                    _ = RefreshServiceCostUiAsync(force: true);
                    _mainWindow?.SetLastCommand($"CAP NHAT DICH VU @ {DateTime.Now:HH:mm:ss}");
                }

                if (failedItems.Count > 0)
                {
                    var errorPreview = string.Join(
                        Environment.NewLine,
                        failedItems.Take(6).Select(x => $"- {x}"));
                    errorTextBlock.Text =
                        $"Cap nhat thanh cong {successCount}/{selectedRows.Count}.{Environment.NewLine}{errorPreview}";
                    return;
                }

                dialog.DialogResult = true;
                dialog.Close();
            }
            finally
            {
                orderButton.IsEnabled = true;
                cancelButton.IsEnabled = true;
            }
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            RefreshSummary();
            serviceGrid.Focus();
        };
        _ = dialog.ShowDialog();

        foreach (var row in rows)
        {
            row.PropertyChanged -= RowPropertyChanged;
        }
    }

public async void OpenLoyaltyPanelFromClientUi()
    {
        var activeSession = _activeMemberSession;
        if (activeSession is null)
        {
            MessageBox.Show(
                "Vui lòng dang nh?p b?ng tài kho?n h?i viên d? dùng di?m tích luy.",
                "Ði?m tích luy",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!await PromptForPasswordAsync(activeSession.Username))
        {
            return;
        }

        try
        {
            await SyncActiveMemberUsageAsync("LOYALTY_CHECK", true);

            var settings = await GetLoyaltySettingsAsync();
            if (settings is null)
            {
                MessageBox.Show(
                    "Không t?i du?c cài d?t di?m tích luy t? máy ch?.",
                    "Ði?m tích luy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!settings.Enabled)
            {
                MessageBox.Show(
                    "Tính nang di?m tích luy dang t?t ? máy ch?.",
                    "Ði?m tích luy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var loyalty = await GetMemberLoyaltyAsync(activeSession.MemberId);
            if (loyalty is null)
            {
                MessageBox.Show(
                    "Không d?c du?c di?m tích luy c?a h?i viên.",
                    "Ði?m tích luy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ShowLoyaltyDialog(activeSession, settings, loyalty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"L?i khi m? di?m tích luy: {ex.Message}",
                "Ði?m tích luy",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public async void OpenTransferBalancePanelFromClientUi()
    {
        var activeSession = _activeMemberSession;
        if (activeSession is null)
        {
            MessageBox.Show(
                "Vui lòng dang nh?p b?ng tài kho?n h?i viên d? chuy?n ti?n.",
                "Chuy?n ti?n h?i viên",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!await PromptForPasswordAsync(activeSession.Username))
        {
            return;
        }

        try
        {
            var loyalty = await GetMemberLoyaltyAsync(activeSession.MemberId);
            var sourceMember = loyalty?.Member ?? new MemberLoginItem
            {
                Id = activeSession.MemberId,
                Username = activeSession.Username,
                FullName = activeSession.FullName,
            };

            ShowTransferBalanceDialog(activeSession, sourceMember);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không th? m? màn hình chuy?n ti?n: {ex.Message}",
                "Chuy?n ti?n h?i viên",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public async void OpenWithdrawBalancePanelFromClientUi()
    {
        if (!_isMemberWithdrawEnabled)
        {
            MessageBox.Show(
                "Tính nang rút ti?n h?i viên dang t?t t? app server.",
                "Rút ti?n h?i viên",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var activeSession = _activeMemberSession;
        if (activeSession is null)
        {
            MessageBox.Show(
                "Vui lòng dang nh?p b?ng tài kho?n h?i viên d? rút ti?n.",
                "Rút ti?n h?i viên",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!await PromptForPasswordAsync(activeSession.Username))
        {
            return;
        }

        try
        {
            var loyalty = await GetMemberLoyaltyAsync(activeSession.MemberId);
            var sourceMember = loyalty?.Member ?? new MemberLoginItem
            {
                Id = activeSession.MemberId,
                Username = activeSession.Username,
                FullName = activeSession.FullName,
            };

            ShowWithdrawBalanceDialog(activeSession, sourceMember);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không th? m? màn hình rút ti?n: {ex.Message}",
                "Rút ti?n h?i viên",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public async void OpenTopupRequestPanelFromClientUi()
    {
        if (!_isMemberTopupRequestEnabled)
        {
            MessageBox.Show(
                "Tính nang n?p ti?n nhanh h?i viên dang t?t t? app server.",
                "N?p ti?n h?i viên",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var activeSession = _activeMemberSession;
        if (activeSession is null)
        {
            MessageBox.Show(
                "Vui lòng dang nh?p b?ng tài kho?n h?i viên d? n?p ti?n.",
                "N?p ti?n h?i viên",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!await PromptForPasswordAsync(activeSession.Username))
        {
            return;
        }

        try
        {
            var loyalty = await GetMemberLoyaltyAsync(activeSession.MemberId);
            var sourceMember = loyalty?.Member ?? new MemberLoginItem
            {
                Id = activeSession.MemberId,
                Username = activeSession.Username,
                FullName = activeSession.FullName,
            };

            ShowTopupRequestDialog(activeSession, sourceMember);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không th? m? màn hình n?p ti?n: {ex.Message}",
                "N?p ti?n h?i viên",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public async void OpenChangePasswordPanelFromClientUi()
    {
        var activeSession = _activeMemberSession;
        if (activeSession is null)
        {
            MessageBox.Show(
                "Vui lòng dang nh?p d? d?i m?t kh?u.",
                "Ð?i m?t kh?u",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ShowChangePasswordDialog(activeSession);
    }

    private void ShowChangePasswordDialog(ActiveMemberSession activeSession)
    {
        var dialog = new Window
        {
            Title = "Ð?i m?t kh?u h?i viên",
            Width = 400,
            Height = 350,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid { Margin = new Thickness(24) };
        for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Ð?I M?T KH?U",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 90, 168)),
            Margin = new Thickness(0, 0, 0, 20),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        // Current Password
        var curLabel = new TextBlock { Text = "M?t kh?u hi?n t?i:", Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(curLabel, 1);
        root.Children.Add(curLabel);

        var currentPwdBox = new PasswordBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(currentPwdBox, 2);
        root.Children.Add(currentPwdBox);

        // New Password
        var newLabel = new TextBlock { Text = "M?t kh?u m?i:", Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(newLabel, 3);
        root.Children.Add(newLabel);

        var newPwdBox = new PasswordBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(newPwdBox, 4);
        root.Children.Add(newPwdBox);

        // Confirm New Password
        var confirmLabel = new TextBlock { Text = "Xác nh?n m?t kh?u m?i:", Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(confirmLabel, 5);
        root.Children.Add(confirmLabel);

        var confirmPwdBox = new PasswordBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(confirmPwdBox, 6);
        root.Children.Add(confirmPwdBox);

        var errorText = new TextBlock { Text = "", Foreground = Brushes.Red, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(errorText, 7);
        root.Children.Add(errorText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "H?y", Width = 80, Margin = new Thickness(0, 0, 10, 0) };
        var saveBtn = new Button { Content = "C?p nh?t", Width = 100, IsDefault = true, Background = new SolidColorBrush(Color.FromRgb(30, 90, 168)), Foreground = Brushes.White };
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(saveBtn);
        Grid.SetRow(buttons, 8);
        root.Children.Add(buttons);

        cancelBtn.Click += (_, _) => dialog.Close();
        saveBtn.Click += async (_, _) =>
        {
            errorText.Text = "";
            var currentPwd = currentPwdBox.Password;
            var newPwd = newPwdBox.Password;
            var confirmPwd = confirmPwdBox.Password;

            if (string.IsNullOrEmpty(currentPwd)) { errorText.Text = "Vui lòng nh?p m?t kh?u hi?n t?i."; return; }
            if (string.IsNullOrEmpty(newPwd)) { errorText.Text = "Vui lòng nh?p m?t kh?u m?i."; return; }
            if (newPwd.Length < 4) { errorText.Text = "M?t kh?u m?i ph?i t? 4 ký t? tr? lên."; return; }
            if (newPwd != confirmPwd) { errorText.Text = "M?t kh?u xác nh?n không kh?p."; return; }

            saveBtn.IsEnabled = false;
            errorText.Text = "Ðang ki?m tra m?t kh?u hi?n t?i...";
            errorText.Foreground = Brushes.DimGray;

            try
            {
                // 1. Verify current password via login
                using var loginResp = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl("/members/login"),
                    new
                    {
                        username = activeSession.Username,
                        password = currentPwd,
                        agentId = _settings.AgentId,
                    });
                if (!loginResp.IsSuccessStatusCode)
                {
                    errorText.Text = "M?t kh?u hi?n t?i không chính xác.";
                    errorText.Foreground = Brushes.Red;
                    saveBtn.IsEnabled = true;
                    return;
                }

                // 2. Update to new password
                errorText.Text = "Ðang c?p nh?t m?t kh?u m?i...";
                using var updateResp = await _httpClient.PatchAsJsonAsync(BuildApiUrl($"/members/{activeSession.MemberId}"), new { password = newPwd, updatedBy = "client.password.change" });
                
                if (updateResp.IsSuccessStatusCode)
                {
                    MessageBox.Show("Ð?i m?t kh?u thành công!", "M?t kh?u", MessageBoxButton.OK, MessageBoxImage.Information);
                    _mainWindow?.SetLastCommand($"CHANGE_PWD @ {DateTime.Now:HH:mm:ss}");
                    dialog.Close();
                }
                else
                {
                    var msg = await ReadErrorMessageAsync(updateResp);
                    errorText.Text = string.IsNullOrWhiteSpace(msg) ? "L?i khi c?p nh?t m?t kh?u." : msg;
                    errorText.Foreground = Brushes.Red;
                    saveBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                errorText.Text = "L?i k?t n?i: " + ex.Message;
                errorText.Foreground = Brushes.Red;
                saveBtn.IsEnabled = true;
            }
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) => currentPwdBox.Focus();
        dialog.ShowDialog();
    }

    private Task<bool> PromptForPasswordAsync(string username)
    {
        var tcs = new TaskCompletionSource<bool>();

        var dialog = new Window
        {
            Title = "Xác nh?n m?t kh?u",
            Width = 350,
            Height = 180,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = $"Nh?p m?t kh?u tài kho?n '{username}' d? ti?p t?c:",
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(label, 0);
        root.Children.Add(label);

        var passwordBox = new PasswordBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(passwordBox, 1);
        root.Children.Add(passwordBox);

        var errorLabel = new TextBlock
        {
            Text = "",
            Foreground = Brushes.Red,
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0)
        };
        Grid.SetRow(errorLabel, 2);
        root.Children.Add(errorLabel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "H?y", Width = 70, Margin = new Thickness(0, 0, 10, 0) };
        var okBtn = new Button { Content = "Xác nh?n", Width = 80, IsDefault = true, Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)), Foreground = Brushes.White };
        
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(okBtn);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        cancelBtn.Click += (s, e) => { dialog.Close(); };
        okBtn.Click += async (s, e) =>
        {
            var pwd = passwordBox.Password;
            if (string.IsNullOrEmpty(pwd))
            {
                errorLabel.Text = "Vui lòng nh?p m?t kh?u.";
                return;
            }

            okBtn.IsEnabled = false;
            errorLabel.Text = "Ðang xác th?c...";
            errorLabel.Foreground = Brushes.Gray;

            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl("/members/login"),
                    new
                    {
                        username,
                        password = pwd,
                        agentId = _settings.AgentId,
                    });

                if (response.IsSuccessStatusCode)
                {
                    tcs.SetResult(true);
                    dialog.Close();
                }
                else
                {
                    errorLabel.Text = "M?t kh?u không chính xác.";
                    errorLabel.Foreground = Brushes.Red;
                    okBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                errorLabel.Text = "L?i k?t n?i: " + ex.Message;
                errorLabel.Foreground = Brushes.Red;
                okBtn.IsEnabled = true;
            }
        };

        dialog.Content = root;
        dialog.Closed += (s, e) => { if (!tcs.Task.IsCompleted) tcs.SetResult(false); };
        
        dialog.Show();
        passwordBox.Focus();

        return tcs.Task;
    }

    private string? PromptForManualLockPassword()
    {
        if (_mainWindow is null)
        {
            return null;
        }

        string? result = null;
        var dialog = new Window
        {
            Title = "Khóa máy th? công",
            Width = 390,
            Height = 210,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new TextBlock
        {
            Text = "Nh?p m?t mã d? khóa máy t?m th?i:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var passwordBox = new PasswordBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(passwordBox, 1);
        root.Children.Add(passwordBox);

        var errorText = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(errorText, 2);
        root.Children.Add(errorText);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancelButton = new Button
        {
            Content = "H?y",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        var confirmButton = new Button
        {
            Content = "Khóa máy",
            Width = 90,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            Foreground = Brushes.White,
        };
        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(confirmButton);
        Grid.SetRow(buttonPanel, 3);
        root.Children.Add(buttonPanel);

        cancelButton.Click += (_, _) => dialog.Close();
        confirmButton.Click += (_, _) =>
        {
            var password = passwordBox.Password;

            if (string.IsNullOrEmpty(password))
            {
                errorText.Text = "Vui lòng nh?p m?t mã.";
                return;
            }

            result = password;
            dialog.DialogResult = true;
            dialog.Close();
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) => passwordBox.Focus();
        _ = dialog.ShowDialog();
        return result;
    }

    private void ShowTransferBalanceDialog(
        ActiveMemberSession activeSession,
        MemberLoginItem sourceMember)
    {
        var dialog = new Window
        {
            Title = $"Chuy?n ti?n - {activeSession.Username}",
            Width = 460,
            Height = 480,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
        };
        for (var i = 0; i < 11; i++)
        {
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
            });
        }
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });

        var titleBlock = new TextBlock
        {
            Text = "Chuy?n ti?n cho h?i viên khác",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"Tài kho?n g?i: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"S? du hi?n t?i: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var targetLabel = new TextBlock
        {
            Text = "Tài kho?n nh?n:",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(targetLabel, 3);
        root.Children.Add(targetLabel);

        var targetUsernameBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(targetUsernameBox, 4);
        root.Children.Add(targetUsernameBox);

        var amountLabel = new TextBlock
        {
            Text = "S? ti?n chuy?n (VND):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(amountLabel, 5);
        root.Children.Add(amountLabel);

        var amountBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "1000",
        };
        Grid.SetRow(amountBox, 6);
        root.Children.Add(amountBox);

        var noteLabel = new TextBlock
        {
            Text = "Ghi chú (không b?t bu?c):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(noteLabel, 7);
        root.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(noteBox, 8);
        root.Children.Add(noteBox);

        var hintBlock = new TextBlock
        {
            Text = "T?i thi?u 1.000 VND cho m?i l?n chuy?n.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(hintBlock, 9);
        root.Children.Add(hintBlock);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Grid.SetRow(errorTextBlock, 10);
        root.Children.Add(errorTextBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var cancelButton = new Button
        {
            Content = "H?y",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var transferButton = new Button
        {
            Content = "Chuy?n ti?n",
            Width = 100,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
        };

        transferButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;
            var targetUsername = targetUsernameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                errorTextBlock.Text = "Vui lòng nh?p tài kho?n nh?n.";
                return;
            }

            if (string.Equals(targetUsername, sourceMember.Username, StringComparison.OrdinalIgnoreCase))
            {
                errorTextBlock.Text = "Không th? chuy?n ti?n cho chính mình.";
                return;
            }

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "S? ti?n chuy?n không h?p l?.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "S? ti?n chuy?n t?i thi?u là 1.000 VND.";
                return;
            }

            transferButton.IsEnabled = false;
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/transfer"),
                    new
                    {
                        targetUsername,
                        amount = Convert.ToDouble(amount, CultureInfo.InvariantCulture),
                        note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim(),
                        createdBy = "client.member.transfer",
                        agentId = _settings.AgentId,
                    });

                if (!response.IsSuccessStatusCode)
                {
                    var message = await ReadErrorMessageAsync(response);
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(message)
                        ? $"Chuy?n ti?n th?t b?i ({(int)response.StatusCode})"
                        : message;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberTransferBalanceResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                var nextBalance = payload?.SourceMember?.Balance ?? Math.Max(0, sourceMember.Balance - amount);
                _mainWindow?.SetLastCommand(
                    $"CHUY?N TI?N {amount:N0} -> {targetUsername} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Chuy?n ti?n thành công.\n\nÐã chuy?n: {amount:N0} VND\nÐ?n: {targetUsername}\nS? du còn l?i: {nextBalance:N0} VND",
                    "Chuy?n ti?n h?i viên",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                dialog.DialogResult = true;
                dialog.Close();
            }
            finally
            {
                transferButton.IsEnabled = true;
            }
        };

        actionPanel.Children.Add(cancelButton);
        actionPanel.Children.Add(transferButton);
        Grid.SetRow(actionPanel, 12);
        root.Children.Add(actionPanel);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            targetUsernameBox.Focus();
            amountBox.SelectAll();
        };

        dialog.ShowDialog();
    }

    private void ShowWithdrawBalanceDialog(
        ActiveMemberSession activeSession,
        MemberLoginItem sourceMember)
    {
        var dialog = new Window
        {
            Title = $"Rút ti?n - {activeSession.Username}",
            Width = 430,
            Height = 360,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
        };
        for (var i = 0; i < 9; i++)
        {
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
            });
        }
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });

        var titleBlock = new TextBlock
        {
            Text = "Rút ti?n t? tài kho?n h?i viên",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"Tài kho?n: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"S? du hi?n t?i: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var amountLabel = new TextBlock
        {
            Text = "S? ti?n rút (VND):",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(amountLabel, 3);
        root.Children.Add(amountLabel);

        var amountBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "1000",
        };
        Grid.SetRow(amountBox, 4);
        root.Children.Add(amountBox);

        var noteLabel = new TextBlock
        {
            Text = "Ghi chú (không b?t bu?c):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(noteLabel, 5);
        root.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(noteBox, 6);
        root.Children.Add(noteBox);

        var hintBlock = new TextBlock
        {
            Text = "T?i thi?u 1.000 VND cho m?i l?n rút.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(hintBlock, 7);
        root.Children.Add(hintBlock);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Grid.SetRow(errorTextBlock, 8);
        root.Children.Add(errorTextBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var cancelButton = new Button
        {
            Content = "H?y",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var withdrawButton = new Button
        {
            Content = "Rút ti?n",
            Width = 100,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
        };

        withdrawButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "S? ti?n rút không h?p l?.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "S? ti?n rút t?i thi?u là 1.000 VND.";
                return;
            }

            if (amount > sourceMember.Balance)
            {
                errorTextBlock.Text = "S? du hi?n t?i không d? d? rút.";
                return;
            }

            withdrawButton.IsEnabled = false;
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/withdraw"),
                    new
                    {
                        amount = Convert.ToDouble(amount, CultureInfo.InvariantCulture),
                        note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim(),
                        createdBy = "client.member.withdraw",
                        agentId = _settings.AgentId,
                    });

                if (!response.IsSuccessStatusCode)
                {
                    var message = await ReadErrorMessageAsync(response);
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(message)
                        ? $"Rút ti?n th?t b?i ({(int)response.StatusCode})"
                        : message;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberWithdrawRequestResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                var requestId = payload?.Request?.RequestId ?? "-";

                _mainWindow?.SetLastCommand(
                    $"GUI YEU CAU RUT TIEN {amount:N0} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Ðã g?i yêu c?u rút ti?n.\n\nS? ti?n: {amount:N0} VND\nMã yêu c?u: {requestId}\nBên app server s? hi?n popup có nút Ch?p nh?n/H?y.",
                    "Rút ti?n h?i viên",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                dialog.DialogResult = true;
                dialog.Close();
            }
            finally
            {
                withdrawButton.IsEnabled = true;
            }
        };

        actionPanel.Children.Add(cancelButton);
        actionPanel.Children.Add(withdrawButton);
        Grid.SetRow(actionPanel, 10);
        root.Children.Add(actionPanel);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            amountBox.Focus();
            amountBox.SelectAll();
        };

        dialog.ShowDialog();
    }

    private void ShowTopupRequestDialog(
        ActiveMemberSession activeSession,
        MemberLoginItem sourceMember)
    {
        var dialog = new Window
        {
            Title = $"N?p ti?n - {activeSession.Username}",
            Width = 430,
            Height = 360,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
        };
        for (var i = 0; i < 9; i++)
        {
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
            });
        }
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });

        var titleBlock = new TextBlock
        {
            Text = "G?i yêu c?u n?p ti?n h?i viên",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"Tài kho?n: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"S? du hi?n t?i: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var amountLabel = new TextBlock
        {
            Text = "S? ti?n c?n n?p (VND):",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(amountLabel, 3);
        root.Children.Add(amountLabel);

        var amountBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "1000",
        };
        Grid.SetRow(amountBox, 4);
        root.Children.Add(amountBox);

        var noteLabel = new TextBlock
        {
            Text = "Ghi chú (không b?t bu?c):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(noteLabel, 5);
        root.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(noteBox, 6);
        root.Children.Add(noteBox);

        var hintBlock = new TextBlock
        {
            Text = "T?i thi?u 1.000 VND cho m?i yêu c?u.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(hintBlock, 7);
        root.Children.Add(hintBlock);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Grid.SetRow(errorTextBlock, 8);
        root.Children.Add(errorTextBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var cancelButton = new Button
        {
            Content = "H?y",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var requestButton = new Button
        {
            Content = "G?i yêu c?u",
            Width = 100,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
        };

        requestButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "S? ti?n n?p không h?p l?.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "S? ti?n n?p t?i thi?u là 1.000 VND.";
                return;
            }

            requestButton.IsEnabled = false;
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/topup-request"),
                    new
                    {
                        amount = Convert.ToDouble(amount, CultureInfo.InvariantCulture),
                        note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim(),
                        createdBy = "client.member.topup.request",
                        agentId = _settings.AgentId,
                    });

                if (!response.IsSuccessStatusCode)
                {
                    var message = await ReadErrorMessageAsync(response);
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(message)
                        ? $"G?i yêu c?u th?t b?i ({(int)response.StatusCode})"
                        : message;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberTopupRequestResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                var requestId = payload?.Request?.RequestId ?? "-";

                _mainWindow?.SetLastCommand(
                    $"GUI YEU CAU NAP TIEN {amount:N0} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Ðã g?i yêu c?u n?p ti?n.\n\nS? ti?n: {amount:N0} VND\nMã yêu c?u: {requestId}\nBên app server s? hi?n popup có nút Ch?p nh?n/H?y.",
                    "N?p ti?n h?i viên",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                dialog.DialogResult = true;
                dialog.Close();
            }
            finally
            {
                requestButton.IsEnabled = true;
            }
        };

        actionPanel.Children.Add(cancelButton);
        actionPanel.Children.Add(requestButton);
        Grid.SetRow(actionPanel, 10);
        root.Children.Add(actionPanel);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            amountBox.Focus();
            amountBox.SelectAll();
        };

        dialog.ShowDialog();
    }

    private static bool TryParsePositiveMoney(string value, out decimal amount)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) && amount > 0)
        {
            return true;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0)
        {
            return true;
        }

        amount = 0;
        return false;
    }

    /// <summary>
    /// Consolidated background timer (10s): handles auto-shutdown check + member usage sync.
    /// Merging 2 timers into 1 reduces UI thread context switches.
    /// </summary>
    private async void BackgroundSyncTimer_Tick(object? sender, EventArgs e)
    {
        // --- Auto-shutdown check ---
        if (!_isReadyAutoShutdownTickRunning)
        {
            _isReadyAutoShutdownTickRunning = true;
            try
            {
                await RefreshClientRuntimeSettingsIfDueAsync();
                await EvaluateReadyAutoShutdownAsync();
            }
            finally
            {
                _isReadyAutoShutdownTickRunning = false;
            }
        }

        // --- Member usage sync (merged from former _memberUsageSyncTimer) ---
        await SyncActiveMemberUsageAsync("PERIODIC", false);
        EvaluateMemberRemainingTimeWarnings();
        await EnforceMemberAutoLockIfNoRemainingTimeAsync("PERIODIC");
    }

    private async void ServiceCostSyncTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshServiceCostUiAsync(force: false);
    }

    private async Task RefreshClientRuntimeSettingsIfDueAsync()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastRuntimeSettingsFetchUtc).TotalSeconds < 60)
        {
            return;
        }

        await RefreshClientRuntimeSettingsAsync();
    }

    private async Task RefreshClientRuntimeSettingsAsync()
    {
        _lastRuntimeSettingsFetchUtc = DateTime.UtcNow;
        try
        {
            using var response = await _httpClient.GetAsync(BuildApiUrl("/pricing/client-settings"));
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<ClientRuntimeSettingsResponse>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

            if (payload is null)
            {
                return;
            }

            _readyAutoShutdownMinutes = Math.Clamp(payload.ReadyAutoShutdownMinutes, 1, 240);
            _lockScreenBackgroundMode = NormalizeLockScreenBackgroundMode(payload.LockScreenBackgroundMode);
            _lockScreenBackgroundUrl = (payload.LockScreenBackgroundUrl ?? string.Empty).Trim();
            _isMemberWithdrawEnabled = payload.AllowMemberWithdraw;
            _isMemberTopupRequestEnabled = payload.AllowMemberTopupRequest;
            Client.Agent.Wpf.MainWindow.PricingStep = payload.PricingStep;
            Client.Agent.Wpf.MainWindow.MinimumCharge = payload.MinimumCharge;
            Dispatcher.Invoke(() =>
            {
                _lockScreenWindow?.ApplyBackgroundConfiguration(
                    _lockScreenBackgroundMode,
                    _lockScreenBackgroundUrl);
                _mainWindow?.SetWithdrawActionVisible(_isMemberWithdrawEnabled);
                _mainWindow?.SetTopupRequestActionVisible(_isMemberTopupRequestEnabled);
            });
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Fetch client runtime settings failed", ex);
            }
        }
    }

    private async Task EvaluateReadyAutoShutdownAsync()
    {
        if (_readyAutoShutdownTriggered)
        {
            return;
        }

        if (_activeMemberSession is not null)
        {
            _readyIdleSinceUtc = null;
            return;
        }

        if (!IsReadyStateForAutoShutdown())
        {
            _readyIdleSinceUtc = null;
            return;
        }

        if (_readyIdleSinceUtc is null)
        {
            _readyIdleSinceUtc = DateTime.UtcNow;
            return;
        }

        var elapsed = DateTime.UtcNow - _readyIdleSinceUtc.Value;
        if (elapsed.TotalMinutes < _readyAutoShutdownMinutes)
        {
            return;
        }

        _readyAutoShutdownTriggered = true;
        await TrackAndClearMemberSessionAsync("AUTO_SHUTDOWN_IDLE_READY");
        _mainWindow?.SetLastCommand(
            $"T? T?T sau {_readyAutoShutdownMinutes} phút không dang nh?p");

        if (_logger is not null)
        {
            await _logger.InfoAsync(
                $"Auto shutdown triggered after {_readyAutoShutdownMinutes} minute(s) in READY state");
        }

        TriggerSystemShutdown();
    }

    private bool IsReadyStateForAutoShutdown()
    {
        var code = _currentMachineState.Trim().ToUpperInvariant();
        // LOCKED means the usage session is locked behind login screen, not idle-ready for power off.
        return code is "ONLINE";
    }

    private void TrackMachineState(string state)
    {
        _currentMachineState = string.IsNullOrWhiteSpace(state)
            ? _currentMachineState
            : state;

        if (_activeMemberSession is not null || _isPostpaidGuestSession || _isAdminSession)
        {
            _readyIdleSinceUtc = null;
            _readyAutoShutdownTriggered = false;
            return;
        }

        if (IsReadyStateForAutoShutdown())
        {
            _readyIdleSinceUtc ??= DateTime.UtcNow;
            _readyAutoShutdownTriggered = false;
            return;
        }

        _readyIdleSinceUtc = null;
        _readyAutoShutdownTriggered = false;
    }

    private static string NormalizeLockScreenBackgroundMode(string? mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Contains("image") || normalized.Contains("?nh")) return "image";
        if (normalized.Contains("video")) return "video";
        return "none";
    }

    private async void WebFilterSyncTimer_Tick(object? sender, EventArgs e)
    {
        if (_isWebFilterSyncRunning)
        {
            return;
        }

        _isWebFilterSyncRunning = true;
        try
        {
            await RefreshAndApplyWebFilterAsync();
        }
        finally
        {
            _isWebFilterSyncRunning = false;
        }
    }

    private async Task RefreshAndApplyWebFilterAsync(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _lastWebFilterFetchUtc).TotalSeconds < 45)
        {
            return;
        }

        _lastWebFilterFetchUtc = now;

        try
        {
            using var response = await _httpClient.GetAsync(BuildApiUrl("/web-filter/settings"));
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<WebFilterSettingsResponse>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
            if (payload is null)
            {
                return;
            }

            var domains = payload.BlockedDomains
                .Select(NormalizeDomainForHosts)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x => x!)
                .ToList();

            var signature = $"{payload.Enabled}|{string.Join(",", domains)}";
            if (string.Equals(signature, _lastWebFilterSignature, StringComparison.Ordinal))
            {
                return;
            }

            await ApplyHostsWebFilterAsync(payload.Enabled, domains);
            _lastWebFilterSignature = signature;
            _mainWindow?.SetLastCommand(
                $"WEB FILTER {(payload.Enabled ? "ON" : "OFF")} @ {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Refresh web filter settings failed", ex);
            }
        }
    }

    private async Task ApplyHostsWebFilterAsync(bool enabled, IReadOnlyCollection<string> domains)
    {
        try
        {
            if (!File.Exists(HostsFilePath))
            {
                return;
            }

            var currentText = await File.ReadAllTextAsync(HostsFilePath);
            var normalizedCurrent = currentText.Replace("\r\n", "\n");
            var existingLines = normalizedCurrent.Split('\n').ToList();

            var cleanedLines = new List<string>();
            var insideManagedBlock = false;
            foreach (var line in existingLines)
            {
                if (line.Trim().Equals(WebFilterStartMarker, StringComparison.Ordinal))
                {
                    insideManagedBlock = true;
                    continue;
                }

                if (insideManagedBlock)
                {
                    if (line.Trim().Equals(WebFilterEndMarker, StringComparison.Ordinal))
                    {
                        insideManagedBlock = false;
                    }

                    continue;
                }

                cleanedLines.Add(line);
            }

            while (cleanedLines.Count > 0 && string.IsNullOrWhiteSpace(cleanedLines[^1]))
            {
                cleanedLines.RemoveAt(cleanedLines.Count - 1);
            }

            if (enabled && domains.Count > 0)
            {
                cleanedLines.Add(string.Empty);
                cleanedLines.Add(WebFilterStartMarker);

                foreach (var domain in domains)
                {
                    cleanedLines.Add($"0.0.0.0 {domain}");
                    if (!domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanedLines.Add($"0.0.0.0 www.{domain}");
                    }
                }

                cleanedLines.Add(WebFilterEndMarker);
            }

            var nextText = string.Join(Environment.NewLine, cleanedLines) + Environment.NewLine;
            if (string.Equals(nextText, currentText, StringComparison.Ordinal))
            {
                return;
            }

            await File.WriteAllTextAsync(HostsFilePath, nextText);

            if (_logger is not null)
            {
                await _logger.InfoAsync(
                    $"Applied web filter to hosts: enabled={enabled}, domains={domains.Count}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync(
                    "Cannot apply web filter to hosts. Please run client as Administrator.",
                    ex);
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Apply hosts web filter failed", ex);
            }
        }
    }

    private static string? NormalizeDomainForHosts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim().ToLowerInvariant();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            value = value[7..];
        }
        else if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = value[8..];
        }

        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }

        if (value.StartsWith("*.", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        var splitIndex = value.IndexOfAny(new[] { '/', '?', '#', ':', ' ' });
        if (splitIndex >= 0)
        {
            value = value[..splitIndex];
        }

        value = value.Trim('.').Trim();
        if (value.Length is < 3 or > 90)
        {
            return null;
        }

        if (!value.Contains('.'))
        {
            return null;
        }

        foreach (var character in value)
        {
            if ((character is >= 'a' and <= 'z') ||
                (character is >= '0' and <= '9') ||
                character is '.' or '-')
            {
                continue;
            }

            return null;
        }

        if (value.StartsWith('-') || value.EndsWith('-'))
        {
            return null;
        }

        return value;
    }

    private async void WebsiteLogSyncTimer_Tick(object? sender, EventArgs e)
    {
        if (_isWebsiteLogSyncRunning)
        {
            return;
        }

        _isWebsiteLogSyncRunning = true;
        try
        {
            await SyncWebsiteLogsAsync();
        }
        finally
        {
            _isWebsiteLogSyncRunning = false;
        }
    }

    private async Task SyncWebsiteLogsAsync(bool force = false)
    {
        try
        {
            var settings = await GetWebsiteLogSettingsAsync(force);
            if (settings is null)
            {
                return;
            }

            _websiteLogEnabled = settings.Enabled;
            _websiteLogSyncTimer.Interval = _websiteLogEnabled
                ? TimeSpan.FromSeconds(600)
                : TimeSpan.FromMinutes(10);
            if (!_websiteLogEnabled)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var fromUtc = _lastWebsiteHistoryScanUtc == DateTime.MinValue
                ? now.AddMinutes(-3)
                : _lastWebsiteHistoryScanUtc.AddSeconds(-20);
            if (fromUtc > now)
            {
                fromUtc = now.AddMinutes(-1);
            }

            var browserEntries = await CollectBrowserHistoryEntriesAsync(fromUtc, 240);
            _lastWebsiteHistoryScanUtc = now;
            if (browserEntries.Count == 0)
            {
                return;
            }

            var payloadEntries = new List<object>();
            var stagedSentAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in browserEntries.OrderByDescending(x => x.VisitedAtUtc))
            {
                var dedupeKey = $"{entry.Browser}|{entry.Domain}";
                if (_websiteDomainLastSentAt.TryGetValue(dedupeKey, out var lastSentAt) &&
                    (now - lastSentAt).TotalMinutes < 10)
                {
                    continue;
                }

                payloadEntries.Add(new
                {
                    domain = entry.Domain,
                    url = entry.Url,
                    title = entry.Title,
                    browser = entry.Browser,
                    visitedAt = entry.VisitedAtUtc.ToString("o"),
                });
                stagedSentAt[dedupeKey] = now;

                if (payloadEntries.Count >= 120)
                {
                    break;
                }
            }

            if (payloadEntries.Count == 0)
            {
                return;
            }

            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl("/website-logs/ingest"),
                new
                {
                    agentId = _settings.AgentId,
                    entries = payloadEntries,
                });

            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            foreach (var item in stagedSentAt)
            {
                _websiteDomainLastSentAt[item.Key] = item.Value;
            }

            var staleKeys = _websiteDomainLastSentAt
                .Where(x => (now - x.Value).TotalHours > 8)
                .Select(x => x.Key)
                .ToList();
            foreach (var staleKey in staleKeys)
            {
                _websiteDomainLastSentAt.Remove(staleKey);
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Website log sync failed", ex);
            }
        }
    }

    private async Task<WebsiteLogSettingsResponse?> GetWebsiteLogSettingsAsync(
        bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _lastWebsiteLogSettingsFetchUtc).TotalSeconds < 300)
        {
            return new WebsiteLogSettingsResponse
            {
                Enabled = _websiteLogEnabled,
            };
        }

        _lastWebsiteLogSettingsFetchUtc = now;

        try
        {
            using var response = await _httpClient.GetAsync(
                BuildApiUrl("/website-logs/settings"));
            if (!response.IsSuccessStatusCode)
            {
                return new WebsiteLogSettingsResponse
                {
                    Enabled = _websiteLogEnabled,
                };
            }

            var payload = await response.Content.ReadFromJsonAsync<WebsiteLogSettingsResponse>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

            if (payload is null)
            {
                return new WebsiteLogSettingsResponse
                {
                    Enabled = _websiteLogEnabled,
                };
            }

            return payload;
        }
        catch
        {
            return new WebsiteLogSettingsResponse
            {
                Enabled = _websiteLogEnabled,
            };
        }
    }

    private async Task<List<BrowserVisitEntry>> CollectBrowserHistoryEntriesAsync(
        DateTime fromUtc,
        int maxItems)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var allEntries = new List<BrowserVisitEntry>();

        allEntries.AddRange(await CollectChromiumHistoryEntriesAsync(
            "edge",
            Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
            fromUtc,
            180));
        allEntries.AddRange(await CollectChromiumHistoryEntriesAsync(
            "chrome",
            Path.Combine(localAppData, "Google", "Chrome", "User Data"),
            fromUtc,
            180));
        allEntries.AddRange(await CollectChromiumHistoryEntriesAsync(
            "brave",
            Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"),
            fromUtc,
            180));
        allEntries.AddRange(await CollectFirefoxHistoryEntriesAsync(
            "firefox",
            Path.Combine(roamingAppData, "Mozilla", "Firefox", "Profiles"),
            fromUtc,
            180));

        var deduped = allEntries
            .GroupBy(x => $"{x.Browser}|{x.Domain}|{x.Url}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.VisitedAtUtc).First())
            .OrderByDescending(x => x.VisitedAtUtc)
            .Take(Math.Max(20, maxItems))
            .ToList();

        return deduped;
    }

    private async Task<List<BrowserVisitEntry>> CollectChromiumHistoryEntriesAsync(
        string browserName,
        string userDataRoot,
        DateTime fromUtc,
        int limitPerBrowser)
    {
        if (!Directory.Exists(userDataRoot))
        {
            return new List<BrowserVisitEntry>();
        }

        var profileDirectories = Directory.GetDirectories(userDataRoot)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (profileDirectories.Count == 0)
        {
            return new List<BrowserVisitEntry>();
        }

        var entries = new List<BrowserVisitEntry>();
        foreach (var profile in profileDirectories)
        {
            var historyPath = Path.Combine(profile, "History");
            if (!File.Exists(historyPath))
            {
                continue;
            }

            entries.AddRange(await ReadChromiumHistorySnapshotAsync(
                browserName,
                historyPath,
                fromUtc,
                120));

            if (entries.Count >= limitPerBrowser)
            {
                break;
            }
        }

        return entries
            .OrderByDescending(x => x.VisitedAtUtc)
            .Take(limitPerBrowser)
            .ToList();
    }

    private async Task<List<BrowserVisitEntry>> ReadChromiumHistorySnapshotAsync(
        string browserName,
        string sourceHistoryPath,
        DateTime fromUtc,
        int limit)
    {
        var snapshotPath = CreateSqliteSnapshot(sourceHistoryPath);
        if (snapshotPath is null)
        {
            return new List<BrowserVisitEntry>();
        }

        try
        {
            var threshold = ToChromiumTimestamp(fromUtc);
            var results = new List<BrowserVisitEntry>();

            await using var connection = new SqliteConnection(
                $"Data Source={snapshotPath};Mode=ReadOnly;");
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT urls.url, COALESCE(urls.title, ''), visits.visit_time
FROM visits
INNER JOIN urls ON visits.url = urls.id
WHERE visits.visit_time >= $threshold
ORDER BY visits.visit_time DESC
LIMIT $limit;";
            command.Parameters.AddWithValue("$threshold", threshold);
            command.Parameters.AddWithValue("$limit", limit);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var url = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var domain = NormalizeDomainForHosts(ExtractDomainFromUrl(url));
                if (string.IsNullOrWhiteSpace(domain) || ShouldIgnoreWebsiteLogDomain(domain))
                {
                    continue;
                }

                var title = reader.IsDBNull(1) ? null : reader.GetString(1);
                var rawVisited = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
                var visitedAtUtc = FromChromiumTimestamp(rawVisited);

                if (visitedAtUtc < fromUtc.AddMinutes(-1))
                {
                    continue;
                }

                results.Add(new BrowserVisitEntry
                {
                    Domain = domain,
                    Url = url,
                    Title = title,
                    Browser = browserName,
                    VisitedAtUtc = visitedAtUtc,
                });
            }

            return results;
        }
        catch
        {
            return new List<BrowserVisitEntry>();
        }
        finally
        {
            CleanupSqliteSnapshot(snapshotPath);
        }
    }

    private async Task<List<BrowserVisitEntry>> CollectFirefoxHistoryEntriesAsync(
        string browserName,
        string profilesRoot,
        DateTime fromUtc,
        int limitPerBrowser)
    {
        if (!Directory.Exists(profilesRoot))
        {
            return new List<BrowserVisitEntry>();
        }

        var entries = new List<BrowserVisitEntry>();
        foreach (var profilePath in Directory.GetDirectories(profilesRoot))
        {
            var dbPath = Path.Combine(profilePath, "places.sqlite");
            if (!File.Exists(dbPath))
            {
                continue;
            }

            entries.AddRange(await ReadFirefoxHistorySnapshotAsync(
                browserName,
                dbPath,
                fromUtc,
                120));

            if (entries.Count >= limitPerBrowser)
            {
                break;
            }
        }

        return entries
            .OrderByDescending(x => x.VisitedAtUtc)
            .Take(limitPerBrowser)
            .ToList();
    }

    private async Task<List<BrowserVisitEntry>> ReadFirefoxHistorySnapshotAsync(
        string browserName,
        string sourceDbPath,
        DateTime fromUtc,
        int limit)
    {
        var snapshotPath = CreateSqliteSnapshot(sourceDbPath);
        if (snapshotPath is null)
        {
            return new List<BrowserVisitEntry>();
        }

        try
        {
            var threshold = ToFirefoxTimestamp(fromUtc);
            var results = new List<BrowserVisitEntry>();

            await using var connection = new SqliteConnection(
                $"Data Source={snapshotPath};Mode=ReadOnly;");
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT url, COALESCE(title, ''), last_visit_date
FROM moz_places
WHERE last_visit_date IS NOT NULL
  AND last_visit_date >= $threshold
ORDER BY last_visit_date DESC
LIMIT $limit;";
            command.Parameters.AddWithValue("$threshold", threshold);
            command.Parameters.AddWithValue("$limit", limit);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var url = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var domain = NormalizeDomainForHosts(ExtractDomainFromUrl(url));
                if (string.IsNullOrWhiteSpace(domain) || ShouldIgnoreWebsiteLogDomain(domain))
                {
                    continue;
                }

                var title = reader.IsDBNull(1) ? null : reader.GetString(1);
                var rawVisited = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
                var visitedAtUtc = FromFirefoxTimestamp(rawVisited);

                if (visitedAtUtc < fromUtc.AddMinutes(-1))
                {
                    continue;
                }

                results.Add(new BrowserVisitEntry
                {
                    Domain = domain,
                    Url = url,
                    Title = title,
                    Browser = browserName,
                    VisitedAtUtc = visitedAtUtc,
                });
            }

            return results;
        }
        catch
        {
            return new List<BrowserVisitEntry>();
        }
        finally
        {
            CleanupSqliteSnapshot(snapshotPath);
        }
    }

    private static string? ExtractDomainFromUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var trimmed = rawUrl.Trim();
        var withProtocol = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                           trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"http://{trimmed}";

        if (!Uri.TryCreate(withProtocol, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Host;
    }

    private static long ToChromiumTimestamp(DateTime utcDateTime)
    {
        var utc = utcDateTime.ToUniversalTime();
        var delta = utc - WebKitEpochUtc;
        return delta.Ticks / 10;
    }

    private static DateTime FromChromiumTimestamp(long rawTimestamp)
    {
        if (rawTimestamp <= 0)
        {
            return DateTime.UtcNow;
        }

        try
        {
            return WebKitEpochUtc.AddTicks(rawTimestamp * 10);
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private static long ToFirefoxTimestamp(DateTime utcDateTime)
    {
        var unixMilliseconds = new DateTimeOffset(utcDateTime.ToUniversalTime())
            .ToUnixTimeMilliseconds();
        return unixMilliseconds * 1000;
    }

    private static DateTime FromFirefoxTimestamp(long rawTimestamp)
    {
        if (rawTimestamp <= 0)
        {
            return DateTime.UtcNow;
        }

        try
        {
            if (rawTimestamp > 1_000_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(rawTimestamp / 1000)
                    .UtcDateTime;
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(rawTimestamp)
                .UtcDateTime;
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private static string? CreateSqliteSnapshot(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return null;
            }

            var snapshotDir = Path.Combine(
                Path.GetTempPath(),
                "ServerManagerBilling",
                "website-log-snapshots");
            Directory.CreateDirectory(snapshotDir);

            var snapshotPath = Path.Combine(
                snapshotDir,
                $"{Path.GetFileName(sourcePath)}.{Guid.NewGuid():N}.db");

            File.Copy(sourcePath, snapshotPath, true);

            var sourceWal = sourcePath + "-wal";
            var sourceShm = sourcePath + "-shm";
            var snapshotWal = snapshotPath + "-wal";
            var snapshotShm = snapshotPath + "-shm";
            if (File.Exists(sourceWal))
            {
                try
                {
                    File.Copy(sourceWal, snapshotWal, true);
                }
                catch
                {
                    // Ignore WAL copy failures.
                }
            }

            if (File.Exists(sourceShm))
            {
                try
                {
                    File.Copy(sourceShm, snapshotShm, true);
                }
                catch
                {
                    // Ignore SHM copy failures.
                }
            }

            return snapshotPath;
        }
        catch
        {
            return null;
        }
    }

    
    private static void CleanupStaleWebsiteLogSnapshots()
    {
        try
        {
            var snapshotDir = Path.Combine(
                Path.GetTempPath(),
                "ServerManagerBilling",
                "website-log-snapshots");

            if (!Directory.Exists(snapshotDir)) return;

            var threshold = DateTime.UtcNow.AddHours(-2);
            var files = Directory.GetFiles(snapshotDir);
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < threshold)
                {
                    try { info.Delete(); } catch { }
                }
            }
        }
        catch { }
    }

    private static void CleanupSqliteSnapshot(string snapshotPath)
    {
        try
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }

        try
        {
            var walPath = snapshotPath + "-wal";
            if (File.Exists(walPath))
            {
                File.Delete(walPath);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }

        try
        {
            var shmPath = snapshotPath + "-shm";
            if (File.Exists(shmPath))
            {
                File.Delete(shmPath);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }

    private static bool ShouldIgnoreWebsiteLogDomain(string domain)
    {
        if (domain.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith(".arpa", StringComparison.OrdinalIgnoreCase) ||
            domain.Equals("wpad", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(domain, out var ipAddress))
        {
            if (IPAddress.IsLoopback(ipAddress))
            {
                return true;
            }

            if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ipAddress.GetAddressBytes();
                if (bytes.Length == 4)
                {
                    if (bytes[0] == 10)
                    {
                        return true;
                    }

                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    {
                        return true;
                    }

                    if (bytes[0] == 192 && bytes[1] == 168)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private async Task TrackAndClearMemberSessionAsync(string reason)
    {
        var activeSession = _activeMemberSession;
        if (activeSession is not null)
        {
            await SyncActiveMemberUsageAsync(reason, true);
            _ = await ReportMemberPresenceAsync(activeSession, false);
        }
        else if (_isPostpaidGuestSession)
        {
            await ReportGuestPresenceAsync(false);
        }
        else if (_isAdminSession)
        {
            await ReportAdminPresenceAsync(false);
        }

        _activeMemberSession = null;
        _isPostpaidGuestSession = false;
        _isAdminSession = false;
        _lastSyncedMemberUsedSeconds = 0;
        ResetMemberRemainingWarnings();
        TrackMachineState(_currentMachineState);
        
        Dispatcher.Invoke(() =>
        {
            _mainWindow?.SetMemberInfo(null, null);
            _mainWindow?.SetServiceCost(0);
            _mainWindow?.SetServiceOrderCount(0);
        });
    }

    private async Task<(bool Success, string Message)> ReportMemberPresenceAsync(
        ActiveMemberSession activeSession,
        bool isActive)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl("/members/presence"),
                new
                {
                    agentId = _settings.AgentId,
                    memberId = activeSession.MemberId,
                    username = activeSession.Username,
                    fullName = activeSession.FullName,
                    isActive,
                });

            if (!response.IsSuccessStatusCode)
            {
                var err = await ReadErrorMessageAsync(response);
                if (_logger is not null)
                {
                    await _logger.ErrorAsync(
                        $"Report member presence failed ({(isActive ? "ACTIVE" : "INACTIVE")}): {(int)response.StatusCode} {err}");
                }

                return (
                    false,
                    string.IsNullOrWhiteSpace(err)
                        ? "Khong the cap nhat trang thai hoi vien."
                        : err);
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Report member presence failed", ex);
            }

            return (false, $"Khong the ket noi server: {ex.Message}");
        }
    }

    private async Task ReportGuestPresenceAsync(bool isActive)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl("/members/guest-presence"),
                new
                {
                    agentId = _settings.AgentId,
                    isActive,
                    displayName = "Khách vãng lai",
                });

            if (!response.IsSuccessStatusCode && _logger is not null)
            {
                var err = await ReadErrorMessageAsync(response);
                await _logger.ErrorAsync(
                    $"Report guest presence failed ({(isActive ? "ACTIVE" : "INACTIVE")}): {(int)response.StatusCode} {err}");
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Report guest presence failed", ex);
            }
        }
    }

    private async Task ReportAdminPresenceAsync(bool isActive, string? username = null)
    {
        try
        {
            var normalizedUsername = (username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                normalizedUsername = "Admin";
            }

            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl("/members/admin-presence"),
                new
                {
                    agentId = _settings.AgentId,
                    isActive,
                    username = normalizedUsername,
                    fullName = "Admin",
                });

            if (!response.IsSuccessStatusCode && _logger is not null)
            {
                var err = await ReadErrorMessageAsync(response);
                await _logger.ErrorAsync(
                    $"Report admin presence failed ({(isActive ? "ACTIVE" : "INACTIVE")}): {(int)response.StatusCode} {err}");
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Report admin presence failed", ex);
            }
        }
    }

    private int GetUsedSecondsOnUiThread()
    {
        if (_mainWindow is null)
        {
            return 0;
        }

        if (Dispatcher.CheckAccess())
        {
            return _mainWindow.GetUsedSeconds();
        }

        return Dispatcher.Invoke(() => _mainWindow?.GetUsedSeconds() ?? 0);
    }

    private int GetRemainingMinutesOnUiThread()
    {
        if (_mainWindow is null)
        {
            return 0;
        }

        if (Dispatcher.CheckAccess())
        {
            return _mainWindow.GetRemainingMinutes();
        }

        return Dispatcher.Invoke(() => _mainWindow?.GetRemainingMinutes() ?? 0);
    }

    private void ResetMemberRemainingWarnings()
    {
        _lastMemberRemainingMinutes = null;
        _memberRemainingWarningSent.Clear();
    }

    private void EvaluateMemberRemainingTimeWarnings()
    {
        if (_activeMemberSession is null || _isAdminSession || _isPostpaidGuestSession)
        {
            ResetMemberRemainingWarnings();
            return;
        }

        var remainingMinutes = Math.Max(0, GetRemainingMinutesOnUiThread());
        var previousRemainingMinutes = _lastMemberRemainingMinutes ?? (remainingMinutes + 1);
        _lastMemberRemainingMinutes = remainingMinutes;

        foreach (var threshold in MemberRemainingWarningThresholds)
        {
            if (remainingMinutes > threshold)
            {
                continue;
            }

            if (_memberRemainingWarningSent.Contains(threshold))
            {
                continue;
            }

            if (previousRemainingMinutes <= threshold)
            {
                continue;
            }

            _memberRemainingWarningSent.Add(threshold);
            ShowMemberRemainingTimeWarning(threshold);
            break;
        }
    }

    private void ShowMemberRemainingTimeWarning(int thresholdMinutes)
    {
        _ = SpeakMemberRemainingWarningAsync(thresholdMinutes);

        Dispatcher.Invoke(() =>
        {
            _mainWindow?.SetLastCommand(
                $"MEMBER TIME WARNING {thresholdMinutes}m @ {DateTime.Now:HH:mm:ss}");
        });

        if (_logger is not null)
        {
            _ = _logger.InfoAsync($"Member remaining time warning: threshold={thresholdMinutes}m");
        }
    }

    private async Task EnforceMemberAutoLockIfNoRemainingTimeAsync(string source)
    {
        if (_isMemberAutoLockInProgress)
        {
            return;
        }

        if (_activeMemberSession is null || _isAdminSession || _isPostpaidGuestSession)
        {
            return;
        }

        var remainingMinutes = Math.Max(0, GetRemainingMinutesOnUiThread());
        if (remainingMinutes > 0)
        {
            return;
        }

        _isMemberAutoLockInProgress = true;
        try
        {
            await TrackAndClearMemberSessionAsync($"MEMBER_EXPIRED:{source}");
            Dispatcher.Invoke(() =>
            {
                LockMachine(force: true);
                _mainWindow?.SetLastCommand(
                    $"MEMBER EXPIRED AUTO LOCK @ {DateTime.Now:HH:mm:ss}");
            });

            if (_logger is not null)
            {
                await _logger.InfoAsync(
                    $"Auto-locked member session because remaining time reached 0 (source={source})");
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Auto-lock for expired member session failed", ex);
            }
        }
        finally
        {
            _isMemberAutoLockInProgress = false;
        }
    }

    private async Task SpeakMemberRemainingWarningAsync(int thresholdMinutes)
    {
        if (TryPlayCustomMemberRemainingWarningAudio(thresholdMinutes, out var customAudioPath))
        {
            if (_logger is not null)
            {
                await _logger.InfoAsync(
                    $"Played custom member warning audio ({thresholdMinutes}m): {customAudioPath}");
            }

            return;
        }

        var speechText = $"Con {thresholdMinutes} phut.";

        try
        {
            await Task.Run(() =>
            {
                using var synthesizer = new SpeechSynthesizer(); // Optimizing TTS initialization is skipped for safety, but we can rely on GC since it is rare.
                synthesizer.SetOutputToDefaultAudioDevice();
                synthesizer.Rate = -1;
                synthesizer.Volume = 100;

                try
                {
                    synthesizer.SelectVoiceByHints(
                        VoiceGender.NotSet,
                        VoiceAge.NotSet,
                        0,
                        new CultureInfo("vi-VN"));
                }
                catch
                {
                    // Fall back to the default installed voice.
                }

                synthesizer.Speak(speechText);
            });
        }
        catch
        {
            try
            {
                System.Media.SystemSounds.Exclamation.Play();
            }
            catch
            {
                // Keep warning flow resilient even when audio output is unavailable.
            }
        }
    }

    private static bool TryPlayCustomMemberRemainingWarningAudio(
        int thresholdMinutes,
        out string selectedPath)
    {
        selectedPath = string.Empty;
        if (thresholdMinutes <= 0)
        {
            return false;
        }

        var candidates = ResolveMemberRemainingWarningAudioCandidates(thresholdMinutes);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
            {
                continue;
            }

            if (!TryPlayAudioFile(candidate))
            {
                continue;
            }

            selectedPath = candidate;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ResolveMemberRemainingWarningAudioCandidates(
        int thresholdMinutes)
    {
        var fileNames = new[]
        {
            $"con-{thresholdMinutes}-phut.mp3",
            $"con-{thresholdMinutes}-phut.wav",
            $"member-warning-{thresholdMinutes}m.mp3",
            $"member-warning-{thresholdMinutes}m.wav",
        };

        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "audio"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ServerManagerBilling",
                "audio"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "Music"),
        };

        var result = new List<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            foreach (var fileName in fileNames)
            {
                var path = Path.Combine(root, fileName);
                if (!result.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(path);
                }
            }
        }

        return result;
    }

    private static bool TryPlayAudioFile(string filePath)
    {
        try
        {
            var absolutePath = Path.GetFullPath(filePath);
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            var dispatcher = Current?.Dispatcher;
            if (dispatcher is null)
            {
                return false;
            }

            dispatcher.Invoke(() =>
            {
                lock (MemberWarningAudioPlaybackSync)
                {
                    _memberWarningAudioPlayer?.Stop();
                    _memberWarningAudioPlayer?.Close();

                    var player = new MediaPlayer
                    {
                        Volume = 1.0,
                    };
                    player.MediaFailed += (_, _) =>
                    {
                        try
                        {
                            player.Stop();
                            player.Close();
                        }
                        catch
                        {
                            // Keep warning flow resilient when media playback fails.
                        }
                    };

                    player.Open(new Uri(absolutePath, UriKind.Absolute));
                    player.Play();
                    _memberWarningAudioPlayer = player;
                }
            });

            return true;
        }
        catch
        {
            return false;
        }
    }
    private async Task SyncActiveMemberUsageAsync(string reason, bool force)
    {
        var activeSession = _activeMemberSession;
        if (activeSession is null)
        {
            return;
        }

        var usedSeconds = GetUsedSecondsOnUiThread();
        var delta = usedSeconds - _lastSyncedMemberUsedSeconds;
        if (delta <= 0)
        {
            return;
        }

        if (!force && delta < 10)
        {
            return;
        }

        await _memberUsageSyncLock.WaitAsync();
        try
        {
            activeSession = _activeMemberSession;
            if (activeSession is null)
            {
                return;
            }

            usedSeconds = GetUsedSecondsOnUiThread();
            delta = usedSeconds - _lastSyncedMemberUsedSeconds;
            if (delta <= 0)
            {
                return;
            }

            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/members/{activeSession.MemberId}/usage"),
                new
                {
                    usedSeconds = delta,
                    note = $"SESSION_USAGE:{reason}",
                    createdBy = _settings.AgentId,
                });

            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                await _logger!.ErrorAsync(
                    $"Failed to sync member usage ({reason}): {(int)response.StatusCode} {error}");
                return;
            }

            _lastSyncedMemberUsedSeconds = usedSeconds;
            try
            {
                var usagePayload = await response.Content.ReadFromJsonAsync<MemberUsageSyncResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });
                if (usagePayload?.Member is not null)
                {
                    SynchronizeMemberBillingFromServer(usagePayload.Member, usedSeconds);
                }
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    await _logger.ErrorAsync(
                        $"Parse member usage sync payload failed ({reason})",
                        ex);
                }
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync($"Sync member usage failed ({reason})", ex);
            }
        }
        finally
        {
            _memberUsageSyncLock.Release();
        }
    }

    private async Task<LoyaltySettingsResponse?> GetLoyaltySettingsAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync(BuildApiUrl("/members/loyalty/settings"));
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<LoyaltySettingsResponse>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
        }
        catch
        {
            return null;
        }
    }

    private async Task<MemberLoyaltyResponse?> GetMemberLoyaltyAsync(string memberId)
    {
        try
        {
            using var response = await _httpClient.GetAsync(BuildApiUrl($"/members/{memberId}/loyalty"));
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<MemberLoyaltyResponse>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
        }
        catch
        {
            return null;
        }
    }

    private void ShowLoyaltyDialog(
        ActiveMemberSession activeSession,
        LoyaltySettingsResponse settings,
        MemberLoyaltyResponse loyaltyResponse)
    {
        var member = loyaltyResponse.Member;
        var loyalty = loyaltyResponse.Loyalty;

        var dialog = new Window
        {
            Title = $"Ði?m tích luy - {activeSession.Username}",
            Width = 430,
            Height = 420,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
        };
        for (var index = 0; index < 8; index++)
        {
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
            });
        }
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });

        var titleTextBlock = new TextBlock
        {
            Text = $"H?i viên: {activeSession.Username}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 17,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleTextBlock, 0);
        root.Children.Add(titleTextBlock);

        var balanceTextBlock = new TextBlock
        {
            Text = $"S? du hi?n t?i: {member.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(balanceTextBlock, 1);
        root.Children.Add(balanceTextBlock);

        var playTimeTextBlock = new TextBlock
        {
            Text = $"Gi? choi còn l?i: {member.PlayHours:0.##} gi?",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(playTimeTextBlock, 2);
        root.Children.Add(playTimeTextBlock);

        var pointsTextBlock = new TextBlock
        {
            Text = $"Ði?m hi?n có: {loyalty.AvailablePoints} di?m",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 90, 168)),
        };
        Grid.SetRow(pointsTextBlock, 3);
        root.Children.Add(pointsTextBlock);

        var progressTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 12),
            Text =
                $"Ðã tích luy: {loyalty.ProgressMinutes:0.##}/{settings.MinutesPerPoint} phút d? lên di?m k? ti?p.",
            Foreground = Brushes.DimGray,
        };
        Grid.SetRow(progressTextBlock, 4);
        root.Children.Add(progressTextBlock);

        var inputPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 8),
        };
        inputPanel.Children.Add(new TextBlock
        {
            Text = "S? di?m mu?n d?i:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        var pointsBox = new TextBox
        {
            Width = 100,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "1",
        };
        inputPanel.Children.Add(pointsBox);
        Grid.SetRow(inputPanel, 5);
        root.Children.Add(inputPanel);

        var helpText = new TextBlock
        {
            Text = "1 di?m = 1 phút choi. Có th? d?i nhi?u di?m m?t l?n.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(helpText, 6);
        root.Children.Add(helpText);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(errorTextBlock, 7);
        root.Children.Add(errorTextBlock);

        var actionPanel = new UniformGrid
        {
            Columns = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var redeemAllButton = new Button
        {
            Content = "Ð?i t?t c?",
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = loyalty.AvailablePoints > 0,
        };
        redeemAllButton.Click += (_, _) =>
        {
            pointsBox.Text = Math.Max(1, loyalty.AvailablePoints).ToString();
            pointsBox.Focus();
            pointsBox.SelectAll();
        };

        var cancelButton = new Button
        {
            Content = "Ðóng",
            Margin = new Thickness(0, 0, 0, 0),
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var spinButton = new Button
        {
            Content = "V\u00f2ng quay",
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(255, 204, 0)),
        };
        spinButton.Content = "V\u00f2ng quay";
        spinButton.Click += (_, _) =>
        {
            ShowLuckySpinDialogV2(activeSession, settings, loyaltyResponse);
        };

        var redeemButton = new Button
        {
            Content = "Ð?i di?m",
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
            IsEnabled = loyalty.AvailablePoints > 0,
        };
        redeemButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;
            if (!int.TryParse(pointsBox.Text.Trim(), out var redeemPoints) || redeemPoints < 1)
            {
                errorTextBlock.Text = "S? di?m d?i ph?i là s? nguyên >= 1.";
                return;
            }

            if (redeemPoints > loyalty.AvailablePoints)
            {
                errorTextBlock.Text = $"Ch? còn {loyalty.AvailablePoints} di?m.";
                return;
            }

            redeemButton.IsEnabled = false;
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/loyalty/redeem"),
                    new
                    {
                        points = redeemPoints,
                        createdBy = "client.loyalty",
                    });

                if (!response.IsSuccessStatusCode)
                {
                    var message = await ReadErrorMessageAsync(response);
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(message)
                        ? $"Ð?i di?m th?t b?i ({(int)response.StatusCode})"
                        : message;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberLoyaltyRedeemResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                if (payload?.Member is not null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var usedSecondsNow = _mainWindow?.GetUsedSeconds() ?? 0;
                        SynchronizeMemberBillingFromServer(payload.Member, usedSecondsNow);
                        _mainWindow?.SetLastCommand(
                            $"Ð?i di?m {redeemPoints} @ {DateTime.Now:HH:mm:ss}");
                        _lastSyncedMemberUsedSeconds = usedSecondsNow;
                    });
                }

                MessageBox.Show(
                    $"Ð?i di?m thành công: +{redeemPoints} phút choi.",
                    "Ði?m tích luy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                dialog.Close();
            }
            finally
            {
                redeemButton.IsEnabled = true;
            }
        };

        actionPanel.Children.Add(spinButton);
        actionPanel.Children.Add(redeemButton);
        actionPanel.Children.Add(redeemAllButton);
        actionPanel.Children.Add(cancelButton);
        Grid.SetRow(actionPanel, 9);
        root.Children.Add(actionPanel);

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private void ShowLuckySpinDialog(
        ActiveMemberSession activeSession,
        LoyaltySettingsResponse settings,
        MemberLoyaltyResponse loyaltyResponse)
    {
        var loyalty = loyaltyResponse.Loyalty;
        var dialog = new Window
        {
            Title = "Vòng quay may m?n",
            Width = 420,
            Height = 580,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid { Margin = new Thickness(20) };
        for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "TH? V?N MAY",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Crimson,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var pointsLabel = new TextBlock
        {
            Text = $"B?n dang có: {loyalty.AvailablePoints} di?m",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        };
        Grid.SetRow(pointsLabel, 1);
        root.Children.Add(pointsLabel);

        // Wheel Structure
        var wheelSize = 260;
        var wheelContainer = new Grid
        {
            Width = wheelSize,
            Height = wheelSize,
            Margin = new Thickness(0, 0, 0, 20),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        
        var wheelRotation = new RotateTransform(0);
        wheelContainer.RenderTransform = wheelRotation;
        wheelContainer.RenderTransformOrigin = new Point(0.5, 0.5);

        var wheelItems = new[]
        {
            new { Label = "Ð?C BI?T\n30p", Minutes = 30, Color = new SolidColorBrush(Color.FromRgb(220, 38, 38)) }, // Red
            new { Label = "0p", Minutes = 0, Color = new SolidColorBrush(Color.FromRgb(107, 114, 128)) },      // Gray
            new { Label = "NH?T\n20p", Minutes = 20, Color = new SolidColorBrush(Color.FromRgb(37, 99, 235)) },   // Blue
            new { Label = "2p", Minutes = 2, Color = new SolidColorBrush(Color.FromRgb(249, 115, 22)) },      // Orange
            new { Label = "NHÌ\n10p", Minutes = 10, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },  // Green
            new { Label = "5p", Minutes = 5, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) },       // Yellow
            new { Label = "0p", Minutes = 0, Color = new SolidColorBrush(Color.FromRgb(107, 114, 128)) },      // Gray
            new { Label = "2p", Minutes = 2, Color = new SolidColorBrush(Color.FromRgb(249, 115, 22)) },      // Orange
            new { Label = "NHÌ\n10p", Minutes = 10, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },  // Green
            new { Label = "5p", Minutes = 5, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) }        // Yellow
        };
        wheelItems = new[]
        {
            new { Label = "0p", Minutes = 0, Color = new SolidColorBrush(Color.FromRgb(100, 116, 139)) },
            new { Label = "1p", Minutes = 1, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) },
            new { Label = "2p", Minutes = 2, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },
            new { Label = "4p", Minutes = 4, Color = new SolidColorBrush(Color.FromRgb(249, 115, 22)) },
            new { Label = "6p", Minutes = 6, Color = new SolidColorBrush(Color.FromRgb(37, 99, 235)) },
            new { Label = "8p", Minutes = 8, Color = new SolidColorBrush(Color.FromRgb(220, 38, 38)) },
            new { Label = "10p", Minutes = 10, Color = new SolidColorBrush(Color.FromRgb(100, 116, 139)) },
            new { Label = "15p", Minutes = 15, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) },
            new { Label = "20p", Minutes = 20, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },
            new { Label = "30p", Minutes = 30, Color = new SolidColorBrush(Color.FromRgb(220, 38, 38)) }
        };

        double radius = wheelSize / 2.0;
        double angleStep = 360.0 / wheelItems.Length;

        // Outer Rim
        var outerRim = new System.Windows.Shapes.Ellipse
        {
            Width = wheelSize + 10, Height = wheelSize + 10,
            Stroke = new LinearGradientBrush(Colors.Gold, Colors.DarkGoldenrod, 45),
            StrokeThickness = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        
        for (int i = 0; i < wheelItems.Length; i++)
        {
            var item = wheelItems[i];
            double startAngle = i * angleStep;
            double endAngle = (i + 1) * angleStep;
            
            // Draw Slice
            double radStart = (startAngle - 90) * Math.PI / 180.0;
            double radEnd = (endAngle - 90) * Math.PI / 180.0;

            Point p1 = new Point(radius, radius);
            Point p2 = new Point(radius + radius * Math.Cos(radStart), radius + radius * Math.Sin(radStart));
            Point p3 = new Point(radius + radius * Math.Cos(radEnd), radius + radius * Math.Sin(radEnd));

            var path = new System.Windows.Shapes.Path
            {
                Fill = item.Color,
                Stroke = Brushes.White,
                StrokeThickness = 1.2,
                Data = new PathGeometry(new[] { 
                    new PathFigure(p1, new PathSegment[] {
                        new LineSegment(p2, true),
                        new ArcSegment(p3, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true),
                        new LineSegment(p1, true)
                    }, true) 
                })
            };
            wheelContainer.Children.Add(path);

            // Add Label
            var label = new TextBlock
            {
                Text = item.Label,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            
            var labelGroup = new TransformGroup();
            labelGroup.Children.Add(new TranslateTransform(0, -radius * 0.72));
            labelGroup.Children.Add(new RotateTransform(startAngle + angleStep / 2.0));
            label.RenderTransform = labelGroup;
            
            wheelContainer.Children.Add(label);
        }

        // Center hub
        var hub = new System.Windows.Shapes.Ellipse
        {
            Width = 46, Height = 46,
            Fill = Brushes.White,
            Stroke = Brushes.Gold,
            StrokeThickness = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        wheelContainer.Children.Add(hub);

        var hubText = new TextBlock
        {
            Text = "QUAY",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        wheelContainer.Children.Add(hubText);

        var wheelAndPointer = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        wheelAndPointer.Children.Add(outerRim);
        wheelAndPointer.Children.Add(wheelContainer);

        // Pointer (Needle) - More distinct arrow
        var pointer = new System.Windows.Shapes.Path
        {
            Fill = Brushes.Crimson,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -15, 0, 0),
            Data = Geometry.Parse("M 0,0 L 12,24 L -12,24 Z"),
            Width = 24, Height = 24,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.5 }
        };
        wheelAndPointer.Children.Add(pointer);

        Grid.SetRow(wheelAndPointer, 2);
        root.Children.Add(wheelAndPointer);

        var costText = new TextBlock
        {
            Text = "Chi phí: 5 di?m / lu?t quay",
            Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 15)
        };
        Grid.SetRow(costText, 3);
        root.Children.Add(costText);

        var resultText = new TextBlock
        {
            Text = "",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DarkGreen,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MinHeight = 50
        };
        Grid.SetRow(resultText, 4);
        root.Children.Add(resultText);

        var spinButton = new Button
        {
            Content = "QUAY NGAY!",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Width = 180,
            Height = 50,
            Background = Brushes.Crimson,
            Foreground = Brushes.White,
            IsEnabled = loyalty.AvailablePoints >= 5
        };

        var closeButton = new Button
        {
            Content = "Ðóng",
            Width = 100,
            Height = 35,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => dialog.Close();

        spinButton.Click += async (_, _) =>
        {
            spinButton.IsEnabled = false;
            closeButton.IsEnabled = false;
            resultText.Text = "Ðang quay...";
            resultText.Foreground = Brushes.DimGray;

            // Start fake fast spin while waiting for API
            var fastSpinAnimation = new DoubleAnimation
            {
                From = wheelRotation.Angle,
                To = wheelRotation.Angle + 3600, // 10 rotations
                Duration = TimeSpan.FromSeconds(10),
                RepeatBehavior = RepeatBehavior.Forever
            };
            wheelRotation.BeginAnimation(RotateTransform.AngleProperty, fastSpinAnimation);

            try
            {
                var startTime = DateTime.Now;
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/loyalty/spin"),
                    new { createdBy = "client.loyalty.spin" });

                // Ensure at least 1.5s spin for effect
                var elapsed = DateTime.Now - startTime;
                if (elapsed < TimeSpan.FromSeconds(1.5)) await Task.Delay(TimeSpan.FromSeconds(1.5) - elapsed);

                if (!response.IsSuccessStatusCode)
                {
                    wheelRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                    var error = await ReadErrorMessageAsync(response);
                    resultText.Text = string.IsNullOrWhiteSpace(error) ? "L?i k?t n?i!" : error;
                    resultText.Foreground = Brushes.Red;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberLoyaltySpinResponse>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (payload is not null)
                {
                    // Find target slice index (there could be multiple for 10p, 5p, 2p, 0p)
                    var possibleIndices = wheelItems
                        .Select((item, index) => new { item, index })
                        .Where(x => x.item.Minutes == payload.WonMinutes)
                        .Select(x => x.index)
                        .ToList();
                        
                    int targetIndex = possibleIndices.Count > 0 
                        ? possibleIndices[new Random().Next(possibleIndices.Count)] 
                        : 1; // Default to 0p if not found

                    // Calculate target angle to point exactly to the middle of the slice
                    double targetAngleOffset = -( (targetIndex + 0.5) * angleStep );
                    double currentAngle = wheelRotation.Angle % 360;
                    double finalAngle = wheelRotation.Angle + (360 * 4) - currentAngle + targetAngleOffset;

                    var stopAnimation = new DoubleAnimation
                    {
                        From = wheelRotation.Angle,
                        To = finalAngle,
                        Duration = TimeSpan.FromSeconds(3.5),
                        EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                    };
                    
                    var tcs = new TaskCompletionSource<bool>();
                    stopAnimation.Completed += (s, e) => tcs.SetResult(true);
                    wheelRotation.BeginAnimation(RotateTransform.AngleProperty, stopAnimation);
                    
                    await tcs.Task;

                    pointsLabel.Text = $"B?n dang có: {payload.Loyalty.AvailablePoints} di?m";
                    resultText.Text = payload.WonMinutes > 0
                        ? $"CHÚC M?NG!\nB?n trúng {payload.WonMinutes} phút choi!"
                        : "Chúc b?n may m?n l?n sau!";
                    resultText.Foreground = payload.WonMinutes > 0 ? Brushes.DarkGreen : Brushes.OrangeRed;

                    Dispatcher.Invoke(() =>
                    {
                        var usedSecondsNow = _mainWindow?.GetUsedSeconds() ?? 0;
                        SynchronizeMemberBillingFromServer(payload.Member, usedSecondsNow);
                        _mainWindow?.SetLastCommand($"QUAY THU?NG: +{payload.WonMinutes}m @ {DateTime.Now:HH:mm:ss}");
                        _lastSyncedMemberUsedSeconds = usedSecondsNow;
                    });

                    spinButton.IsEnabled = payload.Loyalty.AvailablePoints >= 5;
                }
            }
            catch (Exception ex)
            {
                wheelRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                resultText.Text = "L?i: " + ex.Message;
                resultText.Foreground = Brushes.Red;
            }
            finally
            {
                closeButton.IsEnabled = true;
            }
        };

        Grid.SetRow(spinButton, 8);
        root.Children.Add(spinButton);

        var bottomPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        bottomPanel.Children.Add(closeButton);
        Grid.SetRow(bottomPanel, 9);
        root.Children.Add(bottomPanel);

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private void ShowLuckySpinDialogV2(
        ActiveMemberSession activeSession,
        LoyaltySettingsResponse settings,
        MemberLoyaltyResponse loyaltyResponse)
    {
        var loyalty = loyaltyResponse.Loyalty;
        var dialog = new Window
        {
            Title = "Vòng quay may m?n",
            Width = 520,
            Height = 760,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
            Background = new SolidColorBrush(Color.FromRgb(243, 244, 246)),
        };

        var root = new Grid { Margin = new Thickness(22, 18, 22, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titlePanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 14),
        };
        titlePanel.Children.Add(new TextBlock
        {
            Text = "TH? V?N MAY",
            FontSize = 40,
            FontWeight = FontWeights.ExtraBold,
            Foreground = new SolidColorBrush(Color.FromRgb(225, 29, 72)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "M?i lu?t quay t?n 5 di?m",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });
        Grid.SetRow(titlePanel, 0);
        root.Children.Add(titlePanel);

        var statsCard = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 14),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.18,
            },
        };
        var statsGrid = new Grid();
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var pointsLabel = new TextBlock
        {
            Text = $"Ði?m hi?n có: {loyalty.AvailablePoints:N0}",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(pointsLabel, 0);
        statsGrid.Children.Add(pointsLabel);

        var separator = new Border
        {
            Width = 1,
            Height = 26,
            Margin = new Thickness(12, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(separator, 1);
        statsGrid.Children.Add(separator);

        var costText = new TextBlock
        {
            Text = "Chi phí: 5 di?m/lu?t",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(costText, 2);
        statsGrid.Children.Add(costText);

        statsCard.Child = statsGrid;
        Grid.SetRow(statsCard, 1);
        root.Children.Add(statsCard);

        var wheelCard = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 14, 18, 16),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 2,
                Opacity = 0.14,
            },
            Margin = new Thickness(0, 0, 0, 14),
        };

        var wheelHost = new Grid();
        const double wheelSize = 330;
        var wheelContainer = new Grid
        {
            Width = wheelSize,
            Height = wheelSize,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var wheelRotation = new RotateTransform(0);
        wheelContainer.RenderTransform = wheelRotation;
        wheelContainer.RenderTransformOrigin = new Point(0.5, 0.5);

        var wheelItems = new[]
        {
            new { Label = "Ð?C BI?T\n30p", Minutes = 30, Color = new SolidColorBrush(Color.FromRgb(220, 38, 38)) },
            new { Label = "0p", Minutes = 0, Color = new SolidColorBrush(Color.FromRgb(100, 116, 139)) },
            new { Label = "NH?T\n20p", Minutes = 20, Color = new SolidColorBrush(Color.FromRgb(37, 99, 235)) },
            new { Label = "2p", Minutes = 2, Color = new SolidColorBrush(Color.FromRgb(249, 115, 22)) },
            new { Label = "NHÌ\n10p", Minutes = 10, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },
            new { Label = "5p", Minutes = 5, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) },
            new { Label = "0p", Minutes = 0, Color = new SolidColorBrush(Color.FromRgb(100, 116, 139)) },
            new { Label = "2p", Minutes = 2, Color = new SolidColorBrush(Color.FromRgb(249, 115, 22)) },
            new { Label = "NHÌ\n10p", Minutes = 10, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },
            new { Label = "5p", Minutes = 5, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) }
        };
        wheelItems = new[]
        {
            new { Label = "0p", Minutes = 0, Color = new SolidColorBrush(Color.FromRgb(100, 116, 139)) },
            new { Label = "1p", Minutes = 1, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) },
            new { Label = "2p", Minutes = 2, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },
            new { Label = "4p", Minutes = 4, Color = new SolidColorBrush(Color.FromRgb(249, 115, 22)) },
            new { Label = "6p", Minutes = 6, Color = new SolidColorBrush(Color.FromRgb(37, 99, 235)) },
            new { Label = "8p", Minutes = 8, Color = new SolidColorBrush(Color.FromRgb(220, 38, 38)) },
            new { Label = "10p", Minutes = 10, Color = new SolidColorBrush(Color.FromRgb(100, 116, 139)) },
            new { Label = "15p", Minutes = 15, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) },
            new { Label = "20p", Minutes = 20, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },
            new { Label = "30p", Minutes = 30, Color = new SolidColorBrush(Color.FromRgb(220, 38, 38)) }
        };

        var radius = wheelSize / 2.0;
        var angleStep = 360.0 / wheelItems.Length;

        var outerRim = new System.Windows.Shapes.Ellipse
        {
            Width = wheelSize + 10,
            Height = wheelSize + 10,
            Stroke = new LinearGradientBrush(Colors.Gold, Colors.DarkGoldenrod, 45),
            StrokeThickness = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        for (var i = 0; i < wheelItems.Length; i++)
        {
            var item = wheelItems[i];
            var startAngle = i * angleStep;
            var endAngle = (i + 1) * angleStep;

            var radStart = (startAngle - 90) * Math.PI / 180.0;
            var radEnd = (endAngle - 90) * Math.PI / 180.0;

            var center = new Point(radius, radius);
            var p2 = new Point(radius + radius * Math.Cos(radStart), radius + radius * Math.Sin(radStart));
            var p3 = new Point(radius + radius * Math.Cos(radEnd), radius + radius * Math.Sin(radEnd));

            var path = new System.Windows.Shapes.Path
            {
                Fill = item.Color,
                Stroke = Brushes.White,
                StrokeThickness = 1.2,
                Data = new PathGeometry(new[]
                {
                    new PathFigure(center, new PathSegment[]
                    {
                        new LineSegment(p2, true),
                        new ArcSegment(p3, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true),
                        new LineSegment(center, true)
                    }, true)
                })
            };
            wheelContainer.Children.Add(path);

            var midAngle = startAngle + angleStep / 2.0;
            var label = new TextBlock
            {
                Text = item.Label,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Width = 78,
            };

            var labelGroup = new TransformGroup();
            labelGroup.Children.Add(new TranslateTransform(0, -radius * 0.69));
            labelGroup.Children.Add(new RotateTransform(midAngle));
            label.RenderTransform = labelGroup;
            wheelContainer.Children.Add(label);
        }

        var hub = new System.Windows.Shapes.Ellipse
        {
            Width = 46,
            Height = 46,
            Fill = Brushes.White,
            Stroke = Brushes.Gold,
            StrokeThickness = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        wheelContainer.Children.Add(hub);

        var hubText = new TextBlock
        {
            Text = "QUAY",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        wheelContainer.Children.Add(hubText);

        var wheelAndPointer = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        wheelAndPointer.Children.Add(outerRim);
        wheelAndPointer.Children.Add(wheelContainer);

        var pointerLayer = new Grid
        {
            Width = wheelSize + 12,
            Height = wheelSize + 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        var pointer = new System.Windows.Shapes.Polygon
        {
            Fill = new SolidColorBrush(Color.FromRgb(225, 29, 72)),
            Stroke = Brushes.WhiteSmoke,
            StrokeThickness = 2.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -21, 0, 0),
            Points = new PointCollection
            {
                new Point(0, 0),
                new Point(20, 0),
                new Point(10, 28),
            },
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.45
            }
        };

        var pointerCap = new System.Windows.Shapes.Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(225, 29, 72)),
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -7, 0, 0),
        };

        pointerLayer.Children.Add(pointer);
        pointerLayer.Children.Add(pointerCap);
        wheelAndPointer.Children.Add(pointerLayer);

        wheelHost.Children.Add(wheelAndPointer);
        wheelCard.Child = wheelHost;
        Grid.SetRow(wheelCard, 2);
        root.Children.Add(wheelCard);

        var resultText = new TextBlock
        {
            Text = "Nh?n QUAY NGAY d? b?t d?u.",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MinHeight = 54
        };

        var resultCard = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 14),
            Child = resultText,
        };
        Grid.SetRow(resultCard, 3);
        root.Children.Add(resultCard);

        var spinButton = new Button
        {
            Content = "QUAY NGAY!",
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Width = 260,
            Height = 56,
            Background = new SolidColorBrush(Color.FromRgb(225, 29, 72)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(190, 24, 93)),
            BorderThickness = new Thickness(1),
            IsEnabled = loyalty.AvailablePoints >= 5
        };

        var closeButton = new Button
        {
            Content = "Ðóng",
            Width = 120,
            Height = 42,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Right,
            BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
        };
        closeButton.Click += (_, _) => dialog.Close();

        spinButton.Click += async (_, _) =>
        {
            spinButton.IsEnabled = false;
            closeButton.IsEnabled = false;
            resultText.Text = "Ðang quay...";
            resultText.Foreground = Brushes.DimGray;

            var fastSpinAnimation = new DoubleAnimation
            {
                From = wheelRotation.Angle,
                To = wheelRotation.Angle + 4320,
                Duration = TimeSpan.FromSeconds(6.4),
                RepeatBehavior = RepeatBehavior.Forever,
                AccelerationRatio = 0.16,
                DecelerationRatio = 0.06
            };
            wheelRotation.BeginAnimation(RotateTransform.AngleProperty, fastSpinAnimation);

            try
            {
                var startTime = DateTime.Now;
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/loyalty/spin"),
                    new { createdBy = "client.loyalty.spin" });

                var elapsed = DateTime.Now - startTime;
                if (elapsed < TimeSpan.FromSeconds(2.6))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2.6) - elapsed);
                }

                if (!response.IsSuccessStatusCode)
                {
                    wheelRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                    var error = await ReadErrorMessageAsync(response);
                    resultText.Text = string.IsNullOrWhiteSpace(error) ? "L?i k?t n?i!" : error;
                    resultText.Foreground = Brushes.Red;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberLoyaltySpinResponse>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (payload is not null)
                {
                    var possibleIndices = wheelItems
                        .Select((item, index) => new { item, index })
                        .Where(x => x.item.Minutes == payload.WonMinutes)
                        .Select(x => x.index)
                        .ToList();

                    var targetIndex = possibleIndices.Count > 0
                        ? possibleIndices[new Random().Next(possibleIndices.Count)]
                        : 1;

                    var targetAngleOffset = -((targetIndex + 0.5) * angleStep);
                    var currentAngle = wheelRotation.Angle % 360;
                    var finalAngle = wheelRotation.Angle + (360 * 6) - currentAngle + targetAngleOffset;

                    var stopAnimation = new DoubleAnimation
                    {
                        From = wheelRotation.Angle,
                        To = finalAngle,
                        Duration = TimeSpan.FromSeconds(4.8),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    var tcs = new TaskCompletionSource<bool>();
                    stopAnimation.Completed += (_, _) => tcs.SetResult(true);
                    wheelRotation.BeginAnimation(RotateTransform.AngleProperty, stopAnimation);
                    await tcs.Task;

                    pointsLabel.Text = $"Ði?m hi?n có: {payload.Loyalty.AvailablePoints:N0}";
                    resultText.Text = payload.WonMinutes > 0
                        ? $"CHÚC M?NG!\nB?n trúng {payload.WonMinutes} phút choi!"
                        : "Chúc b?n may m?n l?n sau!";
                    resultText.Foreground = payload.WonMinutes > 0 ? Brushes.DarkGreen : Brushes.OrangeRed;

                    Dispatcher.Invoke(() =>
                    {
                        var usedSecondsNow = _mainWindow?.GetUsedSeconds() ?? 0;
                        SynchronizeMemberBillingFromServer(payload.Member, usedSecondsNow);
                        _mainWindow?.SetLastCommand($"QUAY THU?NG: +{payload.WonMinutes}m @ {DateTime.Now:HH:mm:ss}");
                        _lastSyncedMemberUsedSeconds = usedSecondsNow;
                    });

                    spinButton.IsEnabled = payload.Loyalty.AvailablePoints >= 5;
                }
            }
            catch (Exception ex)
            {
                wheelRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                resultText.Text = "L?i: " + ex.Message;
                resultText.Foreground = Brushes.Red;
            }
            finally
            {
                closeButton.IsEnabled = true;
            }
        };

        var bottomPanel = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(spinButton, 1);
        bottomPanel.Children.Add(spinButton);
        Grid.SetColumn(closeButton, 3);
        bottomPanel.Children.Add(closeButton);
        Grid.SetRow(bottomPanel, 6);
        root.Children.Add(bottomPanel);

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private void ShowMiniGamesHubDialog(
        ActiveMemberSession activeSession,
        LoyaltySettingsResponse settings,
        MemberLoyaltyResponse loyaltyResponse)
    {
        var dialog = new Window
        {
            Title = "Mini game",
            Width = 420,
            Height = 290,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = "Khu mini game",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var subtitle = new TextBlock
        {
            Text = "Ðã xác th?c m?t kh?u. Ch?n trò choi b?n mu?n.",
            Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(subtitle, 1);
        root.Children.Add(subtitle);

        var gameButtons = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        gameButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var spinButton = new Button
        {
            Content = "Vòng quay may m?n",
            Height = 72,
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            Background = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
        };
        spinButton.Click += (_, _) =>
        {
            dialog.Close();
            ShowLuckySpinDialogV2(activeSession, settings, loyaltyResponse);
        };
        Grid.SetColumn(spinButton, 0);
        gameButtons.Children.Add(spinButton);

        Grid.SetRow(gameButtons, 2);
        root.Children.Add(gameButtons);

        var closeButton = new Button
        {
            Content = "Ðóng",
            Width = 100,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        closeButton.Click += (_, _) => dialog.Close();
        Grid.SetRow(closeButton, 3);
        root.Children.Add(closeButton);

        dialog.Content = root;
        dialog.ShowDialog();
    }
    private void ShowHorseRaceMiniDialog(ActiveMemberSession activeSession)
    {
        var dialog = new Window
        {
            Title = $"Dua ngua mini - {activeSession.Username}",
            Width = 780,
            Height = 560,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
        };

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "DUA NGUA SPEED RUN",
            FontSize = 30,
            FontWeight = FontWeights.ExtraBold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var note = new TextBlock
        {
            Text = "Chon ngua va bam Bat dau. Icon se chay muot den vach dich.",
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(note, 1);
        root.Children.Add(note);

        var trackBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 8),
        };

        var trackGrid = new Grid();
        for (var i = 0; i < 6; i++)
        {
            trackGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var horseNames = new[] { "Sam Chop", "Bao Dem", "Hoa Tien", "Loc Xanh", "Than Toc", "Mat Troi" };
        var horseRows = new List<Border>();
        var horseCanvases = new List<Canvas>();
        var horseTransforms = new List<TranslateTransform>();
        var horseIcons = new List<TextBlock>();
        var distanceTexts = new List<TextBlock>();

        for (var i = 0; i < horseNames.Length; i++)
        {
            var row = new Border
            {
                Background = i % 2 == 0
                    ? new SolidColorBrush(Color.FromRgb(248, 250, 252))
                    : new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10, 6, 10, 6),
            };

            var lane = new Grid();
            lane.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            lane.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lane.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

            var nameText = new TextBlock
            {
                Text = $"#{i + 1} {horseNames[i]}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(nameText, 0);
            lane.Children.Add(nameText);

            var trackCanvas = new Canvas
            {
                Height = 32,
                Margin = new Thickness(8, 0, 8, 0),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            };

            var finishLine = new Border
            {
                Width = 4,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                CornerRadius = new CornerRadius(2),
            };
            trackCanvas.Children.Add(finishLine);
            finishLine.SetValue(Canvas.TopProperty, 1d);
            trackCanvas.SizeChanged += (_, _) =>
            {
                finishLine.SetValue(Canvas.LeftProperty, Math.Max(0, trackCanvas.ActualWidth - 6));
            };

            var runnerWrap = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };

            var horseIcon = new TextBlock
            {
                Text = "??",
                FontSize = 26,
                VerticalAlignment = VerticalAlignment.Center,
            };
            horseIcons.Add(horseIcon);
            runnerWrap.Children.Add(horseIcon);

            var speedFx = new TextBlock
            {
                Text = "??",
                FontSize = 14,
                Margin = new Thickness(-4, 10, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            runnerWrap.Children.Add(speedFx);

            var runnerTransform = new TranslateTransform();
            runnerWrap.RenderTransform = runnerTransform;
            horseTransforms.Add(runnerTransform);

            trackCanvas.Children.Add(runnerWrap);
            runnerWrap.SetValue(Canvas.LeftProperty, 2d);
            runnerWrap.SetValue(Canvas.TopProperty, -1d);

            Grid.SetColumn(trackCanvas, 1);
            lane.Children.Add(trackCanvas);
            horseCanvases.Add(trackCanvas);

            var distanceText = new TextBlock
            {
                Text = "0m",
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            };
            Grid.SetColumn(distanceText, 2);
            lane.Children.Add(distanceText);
            distanceTexts.Add(distanceText);

            row.Child = lane;
            Grid.SetRow(row, i);
            trackGrid.Children.Add(row);
            horseRows.Add(row);
        }

        trackBorder.Child = trackGrid;
        Grid.SetRow(trackBorder, 2);
        root.Children.Add(trackBorder);

        var resultText = new TextBlock
        {
            Text = "San sang xuat phat.",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            Margin = new Thickness(0, 10, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        Grid.SetRow(resultText, 3);
        root.Children.Add(resultText);

        var actionGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var horsePicker = new ComboBox
        {
            Width = 200,
            Height = 32,
            ItemsSource = horseNames.Select((name, idx) => $"Ngua #{idx + 1} - {name}"),
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 0, 0),
        };
        Grid.SetColumn(horsePicker, 0);
        actionGrid.Children.Add(horsePicker);

        var raceButton = new Button
        {
            Content = "Bat dau",
            Width = 110,
            Height = 32,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
        };
        Grid.SetColumn(raceButton, 2);
        actionGrid.Children.Add(raceButton);

        var closeButton = new Button
        {
            Content = "Ðóng",
            Width = 90,
            Height = 32,
        };
        closeButton.Click += (_, _) => dialog.Close();
        Grid.SetColumn(closeButton, 4);
        actionGrid.Children.Add(closeButton);

        var isRacing = false;

        raceButton.Click += async (_, _) =>
        {
            if (isRacing)
            {
                return;
            }

            isRacing = true;
            raceButton.IsEnabled = false;
            closeButton.IsEnabled = false;
            horsePicker.IsEnabled = false;

            foreach (var row in horseRows)
            {
                row.BorderThickness = new Thickness(0);
                row.BorderBrush = Brushes.Transparent;
            }

            for (var i = 0; i < horseTransforms.Count; i++)
            {
                horseTransforms[i].X = 0;
                distanceTexts[i].Text = "0m";
                horseIcons[i].Text = "??";
            }

            resultText.Text = "Cac ngua dang tang toc...";
            resultText.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));

            var rng = new Random();
            var winner = -1;
            const double finishDistance = 100d;
            var progress = new double[horseNames.Length];
            var speed = new double[horseNames.Length];

            for (var i = 0; i < speed.Length; i++)
            {
                speed[i] = 11 + rng.NextDouble() * 3;
            }

            var watch = Stopwatch.StartNew();
            var previous = watch.Elapsed;

            while (winner < 0 && dialog.IsVisible)
            {
                await Task.Delay(16);
                var now = watch.Elapsed;
                var dt = Math.Max(0.01, (now - previous).TotalSeconds);
                previous = now;

                for (var i = 0; i < horseNames.Length; i++)
                {
                    var acceleration = (rng.NextDouble() * 3.2) - 0.8;
                    if (rng.NextDouble() < 0.08)
                    {
                        acceleration += 4.5 * rng.NextDouble();
                    }

                    speed[i] = Math.Clamp(speed[i] + acceleration * dt * 2.3, 8.0, 27.0);
                    progress[i] = Math.Min(finishDistance, progress[i] + speed[i] * dt);

                    var maxTravel = Math.Max(0, horseCanvases[i].ActualWidth - 52);
                    var ratio = progress[i] / finishDistance;
                    horseTransforms[i].X = maxTravel * ratio;
                    distanceTexts[i].Text = $"{progress[i]:0}m";

                    if (progress[i] >= finishDistance && winner < 0)
                    {
                        winner = i;
                    }
                }
            }

            if (winner < 0 || !dialog.IsVisible)
            {
                isRacing = false;
                return;
            }

            if (winner >= 0)
            {
                horseRows[winner].BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                horseRows[winner].BorderThickness = new Thickness(2);
                horseIcons[winner].Text = "????";
            }

            var picked = horsePicker.SelectedIndex;
            if (picked == winner)
            {
                resultText.Text = $"Chuan! {horseNames[winner]} ve nhat.";
                resultText.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            }
            else
            {
                resultText.Text = $"{horseNames[winner]} ve nhat. Thu lai van moi nhe.";
                resultText.Foreground = new SolidColorBrush(Color.FromRgb(234, 88, 12));
            }

            raceButton.IsEnabled = true;
            closeButton.IsEnabled = true;
            horsePicker.IsEnabled = true;
            isRacing = false;
        };

        dialog.Closed += (_, _) => isRacing = false;

        Grid.SetRow(actionGrid, 4);
        root.Children.Add(actionGrid);

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private void LockMachine(bool force)
    {
        if (!force && _isPostpaidGuestSession)
        {
            return;
        }

        ClearManualLockState();
        _lockScreenWindow?.PrepareForLock();

        _mainWindow?.SetMachineState("LOCKED");
        _mainWindow?.SetLastCommand($"LOCK @ {DateTime.Now:HH:mm:ss}");
        TrackMachineState("LOCKED");
    }

    private void UnlockMachine()
    {
        ClearManualLockState();
        _lockScreenWindow?.Hide();
        _mainWindow?.SetMachineState("IN_USE");
        _mainWindow?.SetLastCommand($"OPEN @ {DateTime.Now:HH:mm:ss}");
        TrackMachineState("IN_USE");
        _ = RefreshServiceCostUiAsync(force: true);

        // Clean up stale website-log snapshot files from previous runs
        _ = Task.Run(CleanupStaleWebsiteLogSnapshots);
    }

    private void ResumeGuestSessionFromServer()
    {
        if (_activeMemberSession is not null || _isAdminSession)
        {
            return;
        }

        _isPostpaidGuestSession = true;
        _mainWindow?.ConfigureBilling(
            _settings.TotalSessionMinutes,
            _currentHourlyRate,
            false);
        _mainWindow?.SetMemberInfo(null, null);
        UnlockMachine();
        _mainWindow?.SetLastCommand($"GUEST RESUME @ {DateTime.Now:HH:mm:ss}");
    }

    private void OnConnectionStatusChanged(string status)
    {
        Dispatcher.Invoke(() => _mainWindow?.SetConnectionStatus(status));
    }

    private void PauseMachine()
    {
        ClearManualLockState();
        _lockScreenWindow?.PrepareForLock();
        _mainWindow?.SetMachineState("PAUSED");
        _mainWindow?.SetLastCommand($"PAUSE @ {DateTime.Now:HH:mm:ss}");
        TrackMachineState("PAUSED");
    }

    private void ClearManualLockState()
    {
        _manualLockPassword = null;
        _lockScreenWindow?.SetManualUnlockMode(false);
    }

    private static void TriggerSystemRestart()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = "/r /t 3 /f",
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        Process.Start(psi);
    }

    private static void TriggerSystemShutdown()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = "/s /t 3 /f",
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        Process.Start(psi);
    }

    private static int CloseUserApplications()
    {
        var currentPid = Process.GetCurrentProcess().Id;
        var closed = 0;
        var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer",
            "dwm",
            "winlogon",
            "csrss",
            "services",
            "lsass",
            "svchost",
            "ShellExperienceHost",
            "StartMenuExperienceHost",
            "Taskmgr",
        };

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == currentPid)
                {
                    continue;
                }

                if (skipNames.Contains(process.ProcessName))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    continue;
                }

                if (process.CloseMainWindow())
                {
                    closed++;
                }
            }
            catch
            {
                // Ignore per-process access issues.
            }
            finally
            {
                process.Dispose();
            }
        }

        return closed;
    }

    private static string GetLogDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directory = Path.Combine(root, "ServerManagerBilling", "logs");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private void OnAdminNotificationReceived(string message, string? requestedBy)
    {
        Dispatcher.Invoke(() =>
        {
            var fromText = string.IsNullOrWhiteSpace(requestedBy) ? "Qu?n tr? viên" : requestedBy;
            MessageBox.Show(
                message,
                $"Thông báo t? {fromText}",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _mainWindow?.SetLastCommand($"NOTIFY @ {DateTime.Now:HH:mm:ss}");
        });
    }

    private void OnMemberAccountChangedFromServer(MemberAccountChangedPayload payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.MemberId))
        {
            return;
        }

        var activeSession = _activeMemberSession;
        if (activeSession is null || _isAdminSession || _isPostpaidGuestSession)
        {
            return;
        }

        if (!payload.MemberId.Equals(activeSession.MemberId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var snapshot = payload.Member;
        if (snapshot is null)
        {
            return;
        }

        var updatedMember = new MemberLoginItem
        {
            Id = string.IsNullOrWhiteSpace(snapshot.Id) ? activeSession.MemberId : snapshot.Id,
            Username = string.IsNullOrWhiteSpace(snapshot.Username) ? activeSession.Username : snapshot.Username,
            FullName = activeSession.FullName,
            Balance = snapshot.Balance,
            PlaySeconds = Math.Max(0, snapshot.PlaySeconds),
            Rank = string.IsNullOrWhiteSpace(snapshot.Rank) ? activeSession.Rank : snapshot.Rank,
        };

        activeSession.Username = updatedMember.Username;
        activeSession.Rank = updatedMember.Rank;

        var usedSecondsNow = GetUsedSecondsOnUiThread();
        SynchronizeMemberBillingFromServer(updatedMember, usedSecondsNow);
        EvaluateMemberRemainingTimeWarnings();
        _ = EnforceMemberAutoLockIfNoRemainingTimeAsync("REALTIME_ACCOUNT_CHANGED");

        var reasonText = string.IsNullOrWhiteSpace(payload.Reason) ? "SYNC" : payload.Reason;
        Dispatcher.Invoke(() =>
        {
            _mainWindow?.SetMemberInfo(activeSession.Username, activeSession.Rank);
            _mainWindow?.SetLastCommand(
                $"MEMBER ACCOUNT {reasonText} @ {DateTime.Now:HH:mm:ss}");
        });

        if (_logger is not null)
        {
            _ = _logger.InfoAsync(
                $"Applied member.account.changed memberId={payload.MemberId} reason={reasonText}");
        }
    }

    private async Task HandleGetRunningAppsRequestedAsync(AdminGetRunningAppsPayload payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payload.PcId) || string.IsNullOrWhiteSpace(payload.RequestId))
            {
                return;
            }

            var currentPid = Process.GetCurrentProcess().Id;
            var apps = new List<object>();
            var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "explorer",
                "dwm",
                "winlogon",
                "csrss",
                "services",
                "lsass",
                "svchost",
                "ShellExperienceHost",
                "StartMenuExperienceHost",
                "Taskmgr",
            };

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == currentPid) continue;
                    if (skipNames.Contains(process.ProcessName)) continue;
                    if (string.IsNullOrWhiteSpace(process.MainWindowTitle)) continue;

                    apps.Add(new
                    {
                        pid = process.Id,
                        name = process.ProcessName,
                        title = process.MainWindowTitle
                    });
                }
                catch
                {
                    // Ignore inaccessible processes
                }
            }

            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/pcs/{payload.PcId}/running-apps-upload"),
                new
                {
                    requestId = payload.RequestId,
                    apps = apps
                });

            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                await _logger!.ErrorAsync($"Upload running apps failed ({(int)response.StatusCode}): {error}");
                return;
            }

            await _logger!.InfoAsync($"Uploaded {apps.Count} running app(s) for requestId={payload.RequestId}");
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Handle get running apps failed", ex);
            }
        }
    }

    private async Task HandleKillProcessRequestedAsync(AdminKillProcessPayload payload)
    {
        try
        {
            var p = Process.GetProcessById(payload.Pid);
            if (p != null && string.Equals(p.ProcessName, payload.Name, StringComparison.OrdinalIgnoreCase))
            {
                p.Kill();
                await _logger!.InfoAsync($"Killed process PID={payload.Pid} Name={payload.Name} by admin request");
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync($"Failed to kill process PID={payload.Pid} Name={payload.Name}", ex);
            }
        }
    }

    private async Task HandleCaptureScreenshotRequestedAsync(
        AdminCaptureScreenshotPayload payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payload.PcId) ||
                string.IsNullOrWhiteSpace(payload.RequestId))
            {
                return;
            }

            var captured = CapturePrimaryScreenJpeg();
            if (captured is null)
            {
                await _logger!.ErrorAsync("Capture screenshot failed: empty image");
                return;
            }

            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/pcs/{payload.PcId}/screenshot-upload"),
                new
                {
                    agentId = _settings.AgentId,
                    requestId = payload.RequestId,
                    imageBase64 = captured.Base64,
                    mimeType = "image/jpeg",
                    width = captured.Width,
                    height = captured.Height,
                    capturedAt = DateTime.UtcNow.ToString("o"),
                });

            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                await _logger!.ErrorAsync(
                    $"Upload screenshot failed ({(int)response.StatusCode}): {error}");
                return;
            }

            await _logger!.InfoAsync(
                $"Screenshot captured and uploaded (requestId={payload.RequestId}, {captured.Width}x{captured.Height})");
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Handle capture screenshot failed", ex);
            }
        }
    }

    private async Task HandleLiveFrameRequestedAsync(AdminLiveFrameRequestPayload payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payload.PcId) ||
                string.IsNullOrWhiteSpace(payload.RequestId))
            {
                return;
            }

            var captured = CapturePrimaryScreenJpeg(
                maxWidth: 1024,
                jpegQuality: 45,
                highQualityResize: false);
            if (captured is null)
            {
                return;
            }

            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/pcs/{payload.PcId}/live-frame-upload"),
                new
                {
                    agentId = _settings.AgentId,
                    requestId = payload.RequestId,
                    imageBase64 = captured.Base64,
                    mimeType = "image/jpeg",
                    width = captured.Width,
                    height = captured.Height,
                    capturedAt = DateTime.UtcNow.ToString("o"),
                });

            if (!response.IsSuccessStatusCode && _logger is not null)
            {
                var error = await ReadErrorMessageAsync(response);
                await _logger.ErrorAsync(
                    $"Upload live frame failed ({(int)response.StatusCode}): {error}");
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Handle live frame request failed", ex);
            }
        }
    }

    private async Task HandleRemoteInputRequestedAsync(AdminRemoteInputPayload payload)
    {
        try
        {
            ApplyRemoteInput(payload);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Handle remote input failed", ex);
            }
        }
    }

    private static void ApplyRemoteInput(AdminRemoteInputPayload payload)
    {
        var type = (payload.Type ?? string.Empty).Trim().ToLowerInvariant();
        switch (type)
        {
            case "mouse_move":
                MoveMouseToNormalized(payload.X, payload.Y);
                break;
            case "mouse_down":
                ApplyMouseButton(payload.Button, isDown: true, payload.X, payload.Y);
                break;
            case "mouse_up":
                ApplyMouseButton(payload.Button, isDown: false, payload.X, payload.Y);
                break;
            case "mouse_wheel":
                ApplyMouseWheel(payload.Delta ?? 0, payload.X, payload.Y);
                break;
            case "key_down":
                ApplyKeyboardKey(payload.Key, keyUp: false);
                break;
            case "key_up":
                ApplyKeyboardKey(payload.Key, keyUp: true);
                break;
            case "text":
                if (!string.IsNullOrEmpty(payload.Text))
                {
                    SendUnicodeText(payload.Text);
                }
                break;
        }
    }

    private static void MoveMouseToNormalized(double? x, double? y)
    {
        if (x is null || y is null)
        {
            return;
        }

        ApplyAbsoluteMousePosition(x.Value, y.Value);
    }

    private static void ApplyMouseButton(
        string? button,
        bool isDown,
        double? x,
        double? y)
    {
        if (x is not null && y is not null)
        {
            ApplyAbsoluteMousePosition(x.Value, y.Value);
        }

        var normalizedButton = (button ?? "left").Trim().ToLowerInvariant();
        uint flags = normalizedButton switch
        {
            "right" => isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            "middle" => isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            _ => isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
        };

        SendMouseInput(flags, 0);
    }

    private static void ApplyMouseWheel(int delta, double? x, double? y)
    {
        if (x is not null && y is not null)
        {
            ApplyAbsoluteMousePosition(x.Value, y.Value);
        }

        if (delta == 0)
        {
            return;
        }

        SendMouseInput(MOUSEEVENTF_WHEEL, delta);
    }

    private static void ApplyKeyboardKey(string? key, bool keyUp)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var virtualKey = ResolveVirtualKey(key);
        if (virtualKey is null)
        {
            return;
        }

        SendKeyInput(virtualKey.Value, keyUp);
    }

    private static void ApplyAbsoluteMousePosition(double normalizedX, double normalizedY)
    {
        var x = Math.Clamp(normalizedX, 0d, 1d);
        var y = Math.Clamp(normalizedY, 0d, 1d);

        var width = Math.Max(1, GetSystemMetrics(SM_CXSCREEN));
        var height = Math.Max(1, GetSystemMetrics(SM_CYSCREEN));

        var pixelX = Math.Clamp((int)Math.Round(x * (width - 1)), 0, width - 1);
        var pixelY = Math.Clamp((int)Math.Round(y * (height - 1)), 0, height - 1);

        _ = SetCursorPos(pixelX, pixelY);
    }

    private static void SendMouseInput(uint flags, int mouseData)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = unchecked((uint)mouseData),
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };

        _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyInput(ushort virtualKey, bool keyUp)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };

        _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendUnicodeText(string text)
    {
        foreach (var character in text)
        {
            var down = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)character,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero,
                    },
                },
            };

            var up = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)character,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero,
                    },
                },
            };

            _ = SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
        }
    }

    private static ushort? ResolveVirtualKey(string key)
    {
        var normalized = key.Trim();
        if (Enum.TryParse<Key>(normalized, ignoreCase: true, out var parsedKey))
        {
            var wpfVirtualKey = KeyInterop.VirtualKeyFromKey(parsedKey);
            if (wpfVirtualKey > 0)
            {
                return (ushort)wpfVirtualKey;
            }
        }

        if (normalized.Length == 1)
        {
            var vk = VkKeyScan(normalized[0]);
            if (vk != -1)
            {
                return (ushort)(vk & 0xff);
            }
        }

        var upper = normalized.ToUpperInvariant();
        if (upper.StartsWith("F", StringComparison.Ordinal) &&
            int.TryParse(upper[1..], out var fnNumber) &&
            fnNumber is >= 1 and <= 24)
        {
            return (ushort)(0x70 + (fnNumber - 1));
        }

        return upper switch
        {
            "ENTER" or "RETURN" => 0x0d,
            "TAB" => 0x09,
            "SPACE" => 0x20,
            "ESC" or "ESCAPE" => 0x1b,
            "BACK" or "BACKSPACE" => 0x08,
            "DELETE" => 0x2e,
            "INSERT" => 0x2d,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PRIOR" => 0x21,
            "PAGEDOWN" or "NEXT" => 0x22,
            "LEFT" or "LEFTARROW" => 0x25,
            "UP" or "UPARROW" => 0x26,
            "RIGHT" or "RIGHTARROW" => 0x27,
            "DOWN" or "DOWNARROW" => 0x28,
            "SHIFT" or "LEFTSHIFT" or "RIGHTSHIFT" => 0x10,
            "CTRL" or "CONTROL" or "LEFTCTRL" or "RIGHTCTRL" => 0x11,
            "ALT" or "MENU" or "LEFTALT" or "RIGHTALT" => 0x12,
            "LWIN" or "LEFTWIN" => 0x5b,
            "RWIN" or "RIGHTWIN" => 0x5c,
            "CAPSLOCK" => 0x14,
            _ => null,
        };
    }

    private static CapturedScreenshot? CapturePrimaryScreenJpeg(
        int maxWidth = 1024,
        long jpegQuality = 50,
        bool highQualityResize = false)
    {
        try
        {
            var width = Math.Max(1, (int)Math.Round(SystemParameters.PrimaryScreenWidth));
            var height = Math.Max(1, (int)Math.Round(SystemParameters.PrimaryScreenHeight));

            using var original = new Drawing.Bitmap(width, height);
            using (var graphics = Drawing.Graphics.FromImage(original))
            {
                graphics.CopyFromScreen(0, 0, 0, 0, new Drawing.Size(width, height));
            }

            var output = original;
            Drawing.Bitmap? resized = null;
            if (maxWidth > 0 && width > maxWidth)
            {
                var ratio = maxWidth / (double)width;
                var resizedHeight = Math.Max(1, (int)Math.Round(height * ratio));
                resized = new Drawing.Bitmap(maxWidth, resizedHeight);
                using var g = Drawing.Graphics.FromImage(resized);
                if (highQualityResize)
                {
                    g.InterpolationMode = DrawingDrawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = DrawingDrawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = DrawingDrawing2D.PixelOffsetMode.HighQuality;
                }
                else
                {
                    g.InterpolationMode = DrawingDrawing2D.InterpolationMode.Bilinear;
                    g.SmoothingMode = DrawingDrawing2D.SmoothingMode.HighSpeed;
                    g.PixelOffsetMode = DrawingDrawing2D.PixelOffsetMode.HighSpeed;
                }
                g.DrawImage(original, 0, 0, maxWidth, resizedHeight);
                output = resized;
                width = maxWidth;
                height = resizedHeight;
            }

            try
            {
                using var memory = new MemoryStream();
                var jpegCodec = GetJpegCodec();
                if (jpegCodec is null)
                {
                    output.Save(memory, DrawingImaging.ImageFormat.Jpeg);
                }
                else
                {
                    var encoder = DrawingImaging.Encoder.Quality;
                    using var encoderParams = new DrawingImaging.EncoderParameters(1);
                    var quality = Math.Clamp(jpegQuality, 30L, 90L);
                    encoderParams.Param[0] = new DrawingImaging.EncoderParameter(encoder, quality);
                    output.Save(memory, jpegCodec, encoderParams);
                }

                return new CapturedScreenshot
                {
                    Base64 = Convert.ToBase64String(memory.ToArray()),
                    Width = width,
                    Height = height,
                };
            }
            finally
            {
                resized?.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    private static DrawingImaging.ImageCodecInfo? GetJpegCodec()
    {
        return DrawingImaging.ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => codec.FormatID == DrawingImaging.ImageFormat.Jpeg.Guid);
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);

    private string BuildApiUrl(string path)
    {
        var serverBase = _settings.ServerUrl.TrimEnd('/');
        var apiBase = serverBase.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)
            ? serverBase
            : $"{serverBase}/api/v1";

        var baseUri = new Uri($"{apiBase.TrimEnd('/')}/");
        return new Uri(baseUri, path.TrimStart('/')).ToString();
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return response.StatusCode == HttpStatusCode.Unauthorized
                ? "Sai tài kho?n ho?c m?t kh?u."
                : string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("message", out var messageElement))
            {
                return raw;
            }

            if (messageElement.ValueKind == JsonValueKind.Array)
            {
                var messages = messageElement
                    .EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                var joined = string.Join(Environment.NewLine, messages);
                return string.IsNullOrWhiteSpace(joined) ? raw : joined;
            }

            return messageElement.GetString() ?? raw;
        }
        catch
        {
            return raw;
        }
    }
}

public sealed class BrowserVisitEntry
{
    public string Domain { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string Browser { get; set; } = "unknown";

    public DateTime VisitedAtUtc { get; set; }
}

public sealed class CapturedScreenshot
{
    public string Base64 { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }
}

public sealed class LoginAttemptResult
{
    public LoginAttemptResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public bool Success { get; }

    public string Message { get; }
}

public sealed class MemberLoginResponse
{
    public MemberLoginItem? Member { get; set; }
}

public sealed class MemberLoginItem
{
    public string Id { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public int PlaySeconds { get; set; }

    public double PlayHours { get; set; }

    public string? Rank { get; set; }
}

public sealed class LoyaltySettingsResponse
{
    public bool Enabled { get; set; }

    public int MinutesPerPoint { get; set; }

    public int PointsToMinutes { get; set; }
}

public sealed class ClientRuntimeSettingsResponse
{
    public int ReadyAutoShutdownMinutes { get; set; }
    public string LockScreenBackgroundMode { get; set; } = "none";
    public string LockScreenBackgroundUrl { get; set; } = string.Empty;
    public decimal PricingStep { get; set; } = 1000m;
    public decimal MinimumCharge { get; set; } = 1000m;
    public bool AllowMemberWithdraw { get; set; } = true;
    public bool AllowMemberTopupRequest { get; set; } = true;

    public string ServerTime { get; set; } = string.Empty;
}

public sealed class WebFilterSettingsResponse
{
    public bool Enabled { get; set; }

    public List<string> BlockedDomains { get; set; } = new();

    public string UpdatedAt { get; set; } = string.Empty;

    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class WebsiteLogSettingsResponse
{
    public bool Enabled { get; set; }

    public string UpdatedAt { get; set; } = string.Empty;

    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class MemberLoyaltyResponse
{
    public MemberLoginItem Member { get; set; } = new();

    public MemberLoyaltyItem Loyalty { get; set; } = new();
}

public sealed class MemberLoyaltyRedeemResponse
{
    public MemberLoginItem Member { get; set; } = new();

    public MemberLoyaltyItem Loyalty { get; set; } = new();

    public int RedeemedPoints { get; set; }

    public int GrantedMinutes { get; set; }
}

public sealed class MemberLoyaltySpinResponse
{
    public MemberLoginItem Member { get; set; } = new();

    public MemberLoyaltyItem Loyalty { get; set; } = new();

    public int WonMinutes { get; set; }

    public string PrizeLabel { get; set; } = string.Empty;

    public int CostPoints { get; set; }

    public string? SpunAt { get; set; }
}

public sealed class MemberUsageSyncResponse
{
    public MemberLoginItem? Member { get; set; }

    public int ConsumedSeconds { get; set; }

    public int RequestedSeconds { get; set; }
}

public sealed class MemberTransferBalanceResponse
{
    public MemberLoginItem? SourceMember { get; set; }

    public MemberLoginItem? TargetMember { get; set; }

    public decimal Amount { get; set; }

    public string? TransferredAt { get; set; }
}

public sealed class MemberWithdrawRequestResponse
{
    public MemberWithdrawRequestItem? Request { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }
}

public sealed class MemberWithdrawRequestItem
{
    public string RequestId { get; set; } = string.Empty;

    public string MemberId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string? Note { get; set; }

    public string Status { get; set; } = string.Empty;

    public string RequestedAt { get; set; } = string.Empty;
}

public sealed class MemberTopupRequestResponse
{
    public MemberTopupRequestItem? Request { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }
}

public sealed class MemberTopupRequestItem
{
    public string RequestId { get; set; } = string.Empty;

    public string MemberId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string? Note { get; set; }

    public string Status { get; set; } = string.Empty;

    public string RequestedAt { get; set; } = string.Empty;
}

public sealed class MemberLoyaltyItem
{
    public int AvailablePoints { get; set; }

    public int EarnedPoints { get; set; }

    public int RedeemedPoints { get; set; }

    public int ProgressSeconds { get; set; }

    public double ProgressMinutes { get; set; }
}

public sealed class ActiveMemberSession
{
    public string MemberId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Rank { get; set; }
}

public sealed class ClientPcListResponse
{
    public List<ClientPcItemDto> Items { get; set; } = new();
}

public sealed class ClientPcItemDto
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ClientPcActiveSessionDto? ActiveSession { get; set; }
}

public sealed class ClientPcActiveSessionDto
{
    public string Id { get; set; } = string.Empty;
}

public sealed class ClientServiceItemsResponse
{
    public List<ClientServiceItemDto> Items { get; set; } = new();
}

public sealed class ClientServiceItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "-";
    public decimal UnitPrice { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ClientPcServiceOrdersResponse
{
    public string PcId { get; set; } = string.Empty;
    public List<ClientPcServiceOrderItem> Items { get; set; } = new();
}

public sealed class ClientPcServiceOrderItem
{
    public string Id { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public bool IsPaid { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public ClientServiceItemDto? ServiceItem { get; set; }
}

public sealed class ClientExistingServiceSummary
{
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}

public sealed class ClientServiceOrderSelectionRow : INotifyPropertyChanged
{
    private const int MaxAddQuantity = 99;
    private int _quantity;

    public string ServiceItemId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string Category { get; init; } = "-";
    public decimal UnitPrice { get; init; }
    public int ExistingQuantity { get; init; }
    public int CancelableQuantity { get; init; }
    public decimal ExistingAmount { get; init; }
    public string UnitPriceText => UnitPrice.ToString("N0", CultureInfo.InvariantCulture);
    public string ExistingText => ExistingQuantity <= 0 ? "-" : $"{ExistingQuantity:N0} ({ExistingAmount:N0})";
    public int ServerOwnedQuantity => Math.Max(0, ExistingQuantity - Math.Max(0, CancelableQuantity));
    public string SourceText
    {
        get
        {
            var clientOwnedQuantity = Math.Max(0, CancelableQuantity);
            if (ExistingQuantity <= 0)
            {
                return "-";
            }

            if (clientOwnedQuantity > 0 && ServerOwnedQuantity > 0)
            {
                return "Hon hop";
            }

            if (ServerOwnedQuantity > 0)
            {
                return "Server";
            }

            return "May tram";
        }
    }
    public bool CanDecrease => Quantity > -Math.Max(0, CancelableQuantity);

    public int Quantity
    {
        get => _quantity;
        set
        {
            var minCancelableQuantity = -Math.Max(0, CancelableQuantity);
            var clamped = Math.Clamp(value, minCancelableQuantity, MaxAddQuantity);
            if (_quantity == clamped)
            {
                return;
            }

            _quantity = clamped;
            NotifyQuantityChanged();
        }
    }

    public decimal LineTotal => UnitPrice * Quantity;
    public string LineTotalText => LineTotal.ToString("N0", CultureInfo.InvariantCulture);

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ClientServiceOrderSelectionRow FromServiceItem(
        ClientServiceItemDto item,
        ClientExistingServiceSummary? existingSummary = null,
        ClientExistingServiceSummary? clientOwnedSummary = null)
    {
        var existingQuantity = existingSummary?.Quantity ?? 0;
        var existingAmount = existingSummary?.Amount ?? 0;
        var cancelableQuantity = clientOwnedSummary?.Quantity ?? 0;
        return new ClientServiceOrderSelectionRow
        {
            ServiceItemId = item.Id,
            ServiceName = item.Name,
            Category = string.IsNullOrWhiteSpace(item.Category) ? "-" : item.Category,
            UnitPrice = item.UnitPrice,
            ExistingQuantity = existingQuantity,
            CancelableQuantity = cancelableQuantity,
            ExistingAmount = existingAmount,
            Quantity = 0,
        };
    }

    public void IncreaseQuantity()
    {
        Quantity = Math.Min(MaxAddQuantity, Quantity + 1);
    }

    public void DecreaseQuantity()
    {
        var minCancelableQuantity = -Math.Max(0, CancelableQuantity);
        Quantity = Math.Max(minCancelableQuantity, Quantity - 1);
    }

    private void NotifyQuantityChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotal)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotalText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanDecrease)));
    }}