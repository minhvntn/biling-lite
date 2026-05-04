using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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
    private int _lastSyncedMemberUsedSeconds;
    private readonly SemaphoreSlim _memberUsageSyncLock = new(1, 1);
    private readonly DispatcherTimer _readyAutoShutdownTimer = new();
    private DateTime? _readyIdleSinceUtc;
    private bool _readyAutoShutdownTriggered;
    private bool _isReadyAutoShutdownTickRunning;
    private int _readyAutoShutdownMinutes = 3;
    private DateTime _lastRuntimeSettingsFetchUtc = DateTime.MinValue;
    private string _currentMachineState = "LOCKED";
    private readonly DispatcherTimer _webFilterSyncTimer = new();
    private bool _isWebFilterSyncRunning;
    private DateTime _lastWebFilterFetchUtc = DateTime.MinValue;
    private string _lastWebFilterSignature = string.Empty;
    private readonly DispatcherTimer _websiteLogSyncTimer = new();
    private bool _isWebsiteLogSyncRunning;
    private bool _websiteLogEnabled;
    private DateTime _lastWebsiteLogSettingsFetchUtc = DateTime.MinValue;
    private DateTime _lastWebsiteHistoryScanUtc = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _websiteDomainLastSentAt =
        new(StringComparer.OrdinalIgnoreCase);
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
        _mainWindow.SetConnectionStatus("Connecting...");
        _mainWindow.SetMachineState("LOCKED");
        _mainWindow.SetLastCommand("Boot sequence");
        MainWindow = _mainWindow;
        _mainWindow.Show();
        TrackMachineState("LOCKED");

        _readyAutoShutdownTimer.Interval = TimeSpan.FromSeconds(10);
        _readyAutoShutdownTimer.Tick += ReadyAutoShutdownTimer_Tick;
        _readyAutoShutdownTimer.Start();
        _ = RefreshClientRuntimeSettingsAsync();

        _webFilterSyncTimer.Interval = TimeSpan.FromSeconds(90);
        _webFilterSyncTimer.Tick += WebFilterSyncTimer_Tick;
        _webFilterSyncTimer.Start();
        _ = RefreshAndApplyWebFilterAsync(true);

        _websiteLogSyncTimer.Interval = TimeSpan.FromSeconds(60);
        _websiteLogSyncTimer.Tick += WebsiteLogSyncTimer_Tick;
        _websiteLogSyncTimer.Start();
        _ = SyncWebsiteLogsAsync(true);

        _lockScreenWindow = new LockScreenWindow();
        _lockScreenWindow.PrepareForLock();

        _socketService = new AgentSocketService(
            _settings,
            _logger,
            HandleCommandAsync,
            OnConnectionStatusChanged,
            OnAdminNotificationReceived,
            HandleCaptureScreenshotRequestedAsync,
            rate =>
            {
                _currentHourlyRate = rate;
                Dispatcher.Invoke(() => _mainWindow?.UpdateHourlyRate(_currentHourlyRate));
            },
            isEnabled =>
            {
                Dispatcher.Invoke(() => _lockScreenWindow?.SetGuestLoginEnabled(isEnabled));
            });

        _ = Task.Run(async () =>
        {
            try
            {
                await _socketService.StartAsync();
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("Socket initialization failed", ex);
                Dispatcher.Invoke(() => _mainWindow.SetConnectionStatus("Disconnected"));
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Task.Run(() => TrackAndClearMemberSessionAsync("APP_EXIT")).GetAwaiter().GetResult();
        }
        catch
        {
            // Keep shutdown path resilient.
        }

        _mainWindow?.AllowShutdown();
        _lockScreenWindow?.AllowShutdown();
        _readyAutoShutdownTimer.Stop();
        _webFilterSyncTimer.Stop();
        _websiteLogSyncTimer.Stop();

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
                    if (payload.HourlyRate is > 0)
                    {
                        _currentHourlyRate = payload.HourlyRate.Value;
                        _mainWindow?.ConfigureBilling(
                            _settings.TotalSessionMinutes,
                            _currentHourlyRate);
                    }
                    Dispatcher.Invoke(UnlockMachine);
                    return (true, "opened");

                case "LOCK":
                    await TrackAndClearMemberSessionAsync("SERVER_LOCK");
                    Dispatcher.Invoke(LockMachine);
                    return (true, "locked");

                case "RESTART":
                    await TrackAndClearMemberSessionAsync("SERVER_RESTART");
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
                    _mainWindow?.SetLastCommand($"CLOSE_APPS @ {DateTime.Now:HH:mm:ss}");
                    return (true, $"closed {closedCount} app(s)");

                case "PAUSE":
                    await SyncActiveMemberUsageAsync("SERVER_PAUSE", true);
                    Dispatcher.Invoke(PauseMachine);
                    return (true, "paused");

                case "RESUME":
                    if (payload.HourlyRate is > 0)
                    {
                        _currentHourlyRate = payload.HourlyRate.Value;
                        _mainWindow?.ConfigureBilling(
                            _settings.TotalSessionMinutes,
                            _currentHourlyRate);
                    }
                    Dispatcher.Invoke(UnlockMachine);
                    _mainWindow?.SetLastCommand($"RESUME @ {DateTime.Now:HH:mm:ss}");
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
        await TrackAndClearMemberSessionAsync(reason);
        Dispatcher.Invoke(() =>
        {
            LockMachine();
            _mainWindow?.SetLastCommand($"{reason} @ {DateTime.Now:HH:mm:ss}");
        });
    }

    public async Task<LoginAttemptResult> TryUnlockFromLockScreenAsync(
        string username,
        string password)
    {
        var normalizedUsername = username.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrEmpty(password))
        {
            return new LoginAttemptResult(false, "Vui lòng nhập tên đăng nhập và mật khẩu.");
        }

        // Check if this is an agent-admin login (verified by server)
        bool isAgentAdmin = false;
        try
        {
            using var adminCheckResp = await _httpClient.PostAsJsonAsync(
                BuildApiUrl("/settings/agent-admin/login"),
                new { username = normalizedUsername, password });
            isAgentAdmin = adminCheckResp.IsSuccessStatusCode;
        }
        catch
        {
            // If server unreachable, fall back to local default
            isAgentAdmin = normalizedUsername.Equals("admin", StringComparison.OrdinalIgnoreCase)
                && password == "admin";
        }

        if (isAgentAdmin)
        {
            _activeMemberSession = null;
            _lastSyncedMemberUsedSeconds = 0;
            Dispatcher.Invoke(() =>
            {
                _mainWindow?.ConfigureBilling(
                    _settings.TotalSessionMinutes,
                    _currentHourlyRate,
                    true);
                UnlockMachine();
                _mainWindow?.SetLastCommand($"ADMIN LOGIN @ {DateTime.Now:HH:mm:ss}");
            });

            return new LoginAttemptResult(true, "Đăng nhập quản trị thành công.");
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
                return new LoginAttemptResult(
                    false,
                    string.IsNullOrWhiteSpace(errorText)
                        ? "Đăng nhập không thành công."
                        : errorText);
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            
            if (payload.TryGetProperty("hourlyRate", out var rateElement))
            {
                _currentHourlyRate = rateElement.GetDecimal();
            }

            if (!payload.TryGetProperty("member", out var memberElement))
            {
                return new LoginAttemptResult(false, "Không đọc được thông tin hội viên.");
            }

            var member = JsonSerializer.Deserialize<MemberLoginItem>(
                memberElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (member is null)
            {
                return new LoginAttemptResult(false, "Không giải mã được thông tin hội viên.");
            }

            if (string.IsNullOrWhiteSpace(member.Id))
            {
                return new LoginAttemptResult(false, "Thiếu mã hội viên từ máy chủ.");
            }

            _activeMemberSession = new ActiveMemberSession
            {
                MemberId = member.Id,
                Username = member.Username,
                FullName = member.FullName,
                Rank = member.Rank,
            };
            _lastSyncedMemberUsedSeconds = 0;

            await ReportMemberPresenceAsync(_activeMemberSession, true);

            Dispatcher.Invoke(() =>
            {
                var totalMinutes = Math.Max(1, member.PlaySeconds / 60);
                _mainWindow?.ConfigureBilling(totalMinutes, _currentHourlyRate, true);
                _mainWindow?.SetMemberInfo(member.Username, member.Rank);
                UnlockMachine();
                _mainWindow?.SetLastCommand(
                    $"MEMBER LOGIN {member.Username} @ {DateTime.Now:HH:mm:ss}");
            });

            return new LoginAttemptResult(
                true,
                $"Đăng nhập thành công: {member.Username}");
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Lock screen member login failed", ex);
            }

            return new LoginAttemptResult(false, $"Không thể kết nối server: {ex.Message}");
        }
    }

    public async Task<LoginAttemptResult> TryUnlockAsGuestAsync()
    {
        try
        {
            using var response = await _httpClient.PostAsync(
                BuildApiUrl($"/pcs/{_settings.AgentId}/guest-login"),
                null);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await ReadErrorMessageAsync(response);
                return new LoginAttemptResult(
                    false,
                    string.IsNullOrWhiteSpace(errorText)
                        ? "Không thể đăng nhập khách vãng lai."
                        : errorText);
            }

            _activeMemberSession = null;
            _lastSyncedMemberUsedSeconds = 0;

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (payload.TryGetProperty("pc", out var pcElement) &&
                pcElement.TryGetProperty("group", out var groupElement) &&
                groupElement.TryGetProperty("hourlyRate", out var rateElement))
            {
                _currentHourlyRate = rateElement.GetDecimal();
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

            return new LoginAttemptResult(true, "Đăng nhập khách vãng lai thành công.");
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Lock screen guest login failed", ex);
            }

            return new LoginAttemptResult(false, $"Không thể kết nối server: {ex.Message}");
        }
    }

    public async void OpenLoyaltyPanelFromClientUi()
    {
        var activeSession = _activeMemberSession;
        if (activeSession is null)
        {
            MessageBox.Show(
                "Vui lòng đăng nhập bằng tài khoản hội viên để dùng điểm tích lũy.",
                "Điểm tích lũy",
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
                    "Không tải được cài đặt điểm tích lũy từ máy chủ.",
                    "Điểm tích lũy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!settings.Enabled)
            {
                MessageBox.Show(
                    "Tính năng điểm tích lũy đang tắt ở máy chủ.",
                    "Điểm tích lũy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var loyalty = await GetMemberLoyaltyAsync(activeSession.MemberId);
            if (loyalty is null)
            {
                MessageBox.Show(
                    "Không đọc được điểm tích lũy của hội viên.",
                    "Điểm tích lũy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ShowLoyaltyDialog(activeSession, settings, loyalty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Lỗi khi mở điểm tích lũy: {ex.Message}",
                "Điểm tích lũy",
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
                "Vui lòng đăng nhập bằng tài khoản hội viên để chuyển tiền.",
                "Chuyển tiền hội viên",
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
                $"Không thể mở màn hình chuyển tiền: {ex.Message}",
                "Chuyển tiền hội viên",
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
                "Vui lòng đăng nhập để đổi mật khẩu.",
                "Đổi mật khẩu",
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
            Title = "Đổi mật khẩu hội viên",
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
            Text = "ĐỔI MẬT KHẨU",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 90, 168)),
            Margin = new Thickness(0, 0, 0, 20),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        // Current Password
        var curLabel = new TextBlock { Text = "Mật khẩu hiện tại:", Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(curLabel, 1);
        root.Children.Add(curLabel);

        var currentPwdBox = new PasswordBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(currentPwdBox, 2);
        root.Children.Add(currentPwdBox);

        // New Password
        var newLabel = new TextBlock { Text = "Mật khẩu mới:", Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(newLabel, 3);
        root.Children.Add(newLabel);

        var newPwdBox = new PasswordBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(newPwdBox, 4);
        root.Children.Add(newPwdBox);

        // Confirm New Password
        var confirmLabel = new TextBlock { Text = "Xác nhận mật khẩu mới:", Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(confirmLabel, 5);
        root.Children.Add(confirmLabel);

        var confirmPwdBox = new PasswordBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(confirmPwdBox, 6);
        root.Children.Add(confirmPwdBox);

        var errorText = new TextBlock { Text = "", Foreground = Brushes.Red, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(errorText, 7);
        root.Children.Add(errorText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "Hủy", Width = 80, Margin = new Thickness(0, 0, 10, 0) };
        var saveBtn = new Button { Content = "Cập nhật", Width = 100, IsDefault = true, Background = new SolidColorBrush(Color.FromRgb(30, 90, 168)), Foreground = Brushes.White };
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

            if (string.IsNullOrEmpty(currentPwd)) { errorText.Text = "Vui lòng nhập mật khẩu hiện tại."; return; }
            if (string.IsNullOrEmpty(newPwd)) { errorText.Text = "Vui lòng nhập mật khẩu mới."; return; }
            if (newPwd.Length < 4) { errorText.Text = "Mật khẩu mới phải từ 4 ký tự trở lên."; return; }
            if (newPwd != confirmPwd) { errorText.Text = "Mật khẩu xác nhận không khớp."; return; }

            saveBtn.IsEnabled = false;
            errorText.Text = "Đang kiểm tra mật khẩu hiện tại...";
            errorText.Foreground = Brushes.DimGray;

            try
            {
                // 1. Verify current password via login
                using var loginResp = await _httpClient.PostAsJsonAsync(BuildApiUrl("/members/login"), new { username = activeSession.Username, password = currentPwd });
                if (!loginResp.IsSuccessStatusCode)
                {
                    errorText.Text = "Mật khẩu hiện tại không chính xác.";
                    errorText.Foreground = Brushes.Red;
                    saveBtn.IsEnabled = true;
                    return;
                }

                // 2. Update to new password
                errorText.Text = "Đang cập nhật mật khẩu mới...";
                using var updateResp = await _httpClient.PatchAsJsonAsync(BuildApiUrl($"/members/{activeSession.MemberId}"), new { password = newPwd, updatedBy = "client.password.change" });
                
                if (updateResp.IsSuccessStatusCode)
                {
                    MessageBox.Show("Đổi mật khẩu thành công!", "Mật khẩu", MessageBoxButton.OK, MessageBoxImage.Information);
                    _mainWindow?.SetLastCommand($"CHANGE_PWD @ {DateTime.Now:HH:mm:ss}");
                    dialog.Close();
                }
                else
                {
                    var msg = await ReadErrorMessageAsync(updateResp);
                    errorText.Text = string.IsNullOrWhiteSpace(msg) ? "Lỗi khi cập nhật mật khẩu." : msg;
                    errorText.Foreground = Brushes.Red;
                    saveBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                errorText.Text = "Lỗi kết nối: " + ex.Message;
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
            Title = "Xác nhận mật khẩu",
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
            Text = $"Nhập mật khẩu tài khoản '{username}' để tiếp tục:",
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
        var cancelBtn = new Button { Content = "Hủy", Width = 70, Margin = new Thickness(0, 0, 10, 0) };
        var okBtn = new Button { Content = "Xác nhận", Width = 80, IsDefault = true, Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)), Foreground = Brushes.White };
        
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
                errorLabel.Text = "Vui lòng nhập mật khẩu.";
                return;
            }

            okBtn.IsEnabled = false;
            errorLabel.Text = "Đang xác thực...";
            errorLabel.Foreground = Brushes.Gray;

            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl("/members/login"),
                    new { username, password = pwd });

                if (response.IsSuccessStatusCode)
                {
                    tcs.SetResult(true);
                    dialog.Close();
                }
                else
                {
                    errorLabel.Text = "Mật khẩu không chính xác.";
                    errorLabel.Foreground = Brushes.Red;
                    okBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                errorLabel.Text = "Lỗi kết nối: " + ex.Message;
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

    private void ShowTransferBalanceDialog(
        ActiveMemberSession activeSession,
        MemberLoginItem sourceMember)
    {
        var dialog = new Window
        {
            Title = $"Chuyển tiền - {activeSession.Username}",
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
            Text = "Chuyển tiền cho hội viên khác",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"Tài khoản gửi: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"Số dư hiện tại: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var targetLabel = new TextBlock
        {
            Text = "Tài khoản nhận:",
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
            Text = "Số tiền chuyển (VND):",
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
            Text = "Ghi chú (không bắt buộc):",
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
            Text = "Tối thiểu 1.000 VND cho mỗi lần chuyển.",
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
            Content = "Hủy",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var transferButton = new Button
        {
            Content = "Chuyển tiền",
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
                errorTextBlock.Text = "Vui lòng nhập tài khoản nhận.";
                return;
            }

            if (string.Equals(targetUsername, sourceMember.Username, StringComparison.OrdinalIgnoreCase))
            {
                errorTextBlock.Text = "Không thể chuyển tiền cho chính mình.";
                return;
            }

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "Số tiền chuyển không hợp lệ.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "Số tiền chuyển tối thiểu là 1.000 VND.";
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
                        ? $"Chuyển tiền thất bại ({(int)response.StatusCode})"
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
                    $"CHUYỂN TIỀN {amount:N0} -> {targetUsername} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Chuyển tiền thành công.\n\nĐã chuyển: {amount:N0} VND\nĐến: {targetUsername}\nSố dư còn lại: {nextBalance:N0} VND",
                    "Chuyển tiền hội viên",
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

    private async void ReadyAutoShutdownTimer_Tick(object? sender, EventArgs e)
    {
        if (_isReadyAutoShutdownTickRunning)
        {
            return;
        }

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
            $"TỰ TẮT sau {_readyAutoShutdownMinutes} phút không đăng nhập");

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
        return code is "ONLINE" or "LOCKED";
    }

    private void TrackMachineState(string state)
    {
        _currentMachineState = string.IsNullOrWhiteSpace(state)
            ? _currentMachineState
            : state;

        if (_activeMemberSession is not null)
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
        if (!force && (now - _lastWebsiteLogSettingsFetchUtc).TotalSeconds < 60)
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
            await ReportMemberPresenceAsync(activeSession, false);
        }

        _activeMemberSession = null;
        _lastSyncedMemberUsedSeconds = 0;
        TrackMachineState(_currentMachineState);
        
        Dispatcher.Invoke(() =>
        {
            _mainWindow?.SetMemberInfo(null, null);
        });
    }

    private async Task ReportMemberPresenceAsync(ActiveMemberSession activeSession, bool isActive)
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

            if (!response.IsSuccessStatusCode && _logger is not null)
            {
                var err = await ReadErrorMessageAsync(response);
                await _logger.ErrorAsync(
                    $"Report member presence failed ({(isActive ? "ACTIVE" : "INACTIVE")}): {(int)response.StatusCode} {err}");
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync("Report member presence failed", ex);
            }
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

    private async Task SyncActiveMemberUsageAsync(string reason, bool force)
    {
        var activeSession = _activeMemberSession;
        if (activeSession is null)
        {
            return;
        }

        var usedSeconds = _mainWindow?.GetUsedSeconds() ?? 0;
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

            usedSeconds = _mainWindow?.GetUsedSeconds() ?? usedSeconds;
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
                });

            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                await _logger!.ErrorAsync(
                    $"Failed to sync member usage ({reason}): {(int)response.StatusCode} {error}");
                return;
            }

            _lastSyncedMemberUsedSeconds = usedSeconds;
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
            Title = $"Điểm tích lũy - {activeSession.Username}",
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
            Text = $"Hội viên: {activeSession.Username}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 17,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleTextBlock, 0);
        root.Children.Add(titleTextBlock);

        var balanceTextBlock = new TextBlock
        {
            Text = $"Số dư hiện tại: {member.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(balanceTextBlock, 1);
        root.Children.Add(balanceTextBlock);

        var playTimeTextBlock = new TextBlock
        {
            Text = $"Giờ chơi còn lại: {member.PlayHours:0.##} giờ",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(playTimeTextBlock, 2);
        root.Children.Add(playTimeTextBlock);

        var pointsTextBlock = new TextBlock
        {
            Text = $"Điểm hiện có: {loyalty.AvailablePoints} điểm",
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
                $"Đã tích lũy: {loyalty.ProgressMinutes:0.##}/{settings.MinutesPerPoint} phút để lên điểm kế tiếp.",
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
            Text = "Số điểm muốn đổi:",
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
            Text = "1 điểm = 1 phút chơi. Có thể đổi nhiều điểm một lần.",
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

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var redeemAllButton = new Button
        {
            Content = "Đổi tất cả",
            Width = 92,
            Margin = new Thickness(0, 0, 8, 0),
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
            Content = "Đóng",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var spinButton = new Button
        {
            Content = "Vòng quay",
            Width = 92,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(255, 204, 0)),
        };
        spinButton.Click += (_, _) =>
        {
            dialog.Close();
            ShowLuckySpinDialog(activeSession, settings, loyaltyResponse);
        };

        var redeemButton = new Button
        {
            Content = "Đổi điểm",
            Width = 90,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
            IsEnabled = loyalty.AvailablePoints > 0,
        };
        redeemButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;
            if (!int.TryParse(pointsBox.Text.Trim(), out var redeemPoints) || redeemPoints < 1)
            {
                errorTextBlock.Text = "Số điểm đổi phải là số nguyên >= 1.";
                return;
            }

            if (redeemPoints > loyalty.AvailablePoints)
            {
                errorTextBlock.Text = $"Chỉ còn {loyalty.AvailablePoints} điểm.";
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
                        ? $"Đổi điểm thất bại ({(int)response.StatusCode})"
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
                        var totalSeconds = payload.Member.PlaySeconds + usedSecondsNow;
                        var totalMinutes = Math.Max(1, (int)Math.Ceiling(totalSeconds / 60.0));
                        _mainWindow?.ConfigureBilling(totalMinutes, _currentHourlyRate, false);
                        _mainWindow?.SetLastCommand(
                            $"Đổi điểm {redeemPoints} @ {DateTime.Now:HH:mm:ss}");
                        _lastSyncedMemberUsedSeconds = usedSecondsNow;
                    });
                }

                MessageBox.Show(
                    $"Đổi điểm thành công: +{redeemPoints} phút chơi.",
                    "Điểm tích lũy",
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
            Title = "Vòng quay may mắn",
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
            Text = "THỬ VẬN MAY",
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
            Text = $"Bạn đang có: {loyalty.AvailablePoints} điểm",
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
            new { Label = "ĐẶC BIỆT\n30p", Minutes = 30, Color = new SolidColorBrush(Color.FromRgb(220, 38, 38)) }, // Red
            new { Label = "0p", Minutes = 0, Color = new SolidColorBrush(Color.FromRgb(107, 114, 128)) },      // Gray
            new { Label = "NHẤT\n20p", Minutes = 20, Color = new SolidColorBrush(Color.FromRgb(37, 99, 235)) },   // Blue
            new { Label = "2p", Minutes = 2, Color = new SolidColorBrush(Color.FromRgb(249, 115, 22)) },      // Orange
            new { Label = "NHÌ\n10p", Minutes = 10, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },  // Green
            new { Label = "5p", Minutes = 5, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) },       // Yellow
            new { Label = "0p", Minutes = 0, Color = new SolidColorBrush(Color.FromRgb(107, 114, 128)) },      // Gray
            new { Label = "2p", Minutes = 2, Color = new SolidColorBrush(Color.FromRgb(249, 115, 22)) },      // Orange
            new { Label = "NHÌ\n10p", Minutes = 10, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },  // Green
            new { Label = "5p", Minutes = 5, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) }        // Yellow
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
            Text = "Chi phí: 5 điểm / lượt quay",
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
            Content = "Đóng",
            Width = 100,
            Height = 35,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => dialog.Close();

        spinButton.Click += async (_, _) =>
        {
            spinButton.IsEnabled = false;
            closeButton.IsEnabled = false;
            resultText.Text = "Đang quay...";
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
                    resultText.Text = string.IsNullOrWhiteSpace(error) ? "Lỗi kết nối!" : error;
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

                    pointsLabel.Text = $"Bạn đang có: {payload.Loyalty.AvailablePoints} điểm";
                    resultText.Text = payload.WonMinutes > 0
                        ? $"CHÚC MỪNG!\nBạn trúng {payload.WonMinutes} phút chơi!"
                        : "Chúc bạn may mắn lần sau!";
                    resultText.Foreground = payload.WonMinutes > 0 ? Brushes.DarkGreen : Brushes.OrangeRed;

                    Dispatcher.Invoke(() =>
                    {
                        var usedSecondsNow = _mainWindow?.GetUsedSeconds() ?? 0;
                        var totalSeconds = payload.Member.PlaySeconds + usedSecondsNow;
                        var totalMinutes = Math.Max(1, (int)Math.Ceiling(totalSeconds / 60.0));
                        _mainWindow?.ConfigureBilling(totalMinutes, _currentHourlyRate, false);
                        _mainWindow?.SetLastCommand($"QUAY THƯỞNG: +{payload.WonMinutes}m @ {DateTime.Now:HH:mm:ss}");
                        _lastSyncedMemberUsedSeconds = usedSecondsNow;
                    });

                    spinButton.IsEnabled = payload.Loyalty.AvailablePoints >= 5;
                }
            }
            catch (Exception ex)
            {
                wheelRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                resultText.Text = "Lỗi: " + ex.Message;
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

    private void LockMachine()
    {
        _lockScreenWindow?.PrepareForLock();

        _mainWindow?.SetMachineState("LOCKED");
        _mainWindow?.SetLastCommand($"LOCK @ {DateTime.Now:HH:mm:ss}");
        TrackMachineState("LOCKED");
    }

    private void UnlockMachine()
    {
        _lockScreenWindow?.Hide();
        _mainWindow?.SetMachineState("IN_USE");
        _mainWindow?.SetLastCommand($"OPEN @ {DateTime.Now:HH:mm:ss}");
        TrackMachineState("IN_USE");
    }

    private void OnConnectionStatusChanged(string status)
    {
        Dispatcher.Invoke(() => _mainWindow?.SetConnectionStatus(status));
    }

    private void PauseMachine()
    {
        _lockScreenWindow?.PrepareForLock();
        _mainWindow?.SetMachineState("PAUSED");
        _mainWindow?.SetLastCommand($"PAUSE @ {DateTime.Now:HH:mm:ss}");
        TrackMachineState("PAUSED");
    }

    private static void TriggerSystemRestart()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = "/r /t 0 /f",
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
            Arguments = "/s /t 0 /f",
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
            var fromText = string.IsNullOrWhiteSpace(requestedBy) ? "Quản trị viên" : requestedBy;
            MessageBox.Show(
                message,
                $"Thông báo từ {fromText}",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _mainWindow?.SetLastCommand($"NOTIFY @ {DateTime.Now:HH:mm:ss}");
        });
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

    private static CapturedScreenshot? CapturePrimaryScreenJpeg()
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

            var maxWidth = 1366;
            var output = original;
            Drawing.Bitmap? resized = null;
            if (width > maxWidth)
            {
                var ratio = maxWidth / (double)width;
                var resizedHeight = Math.Max(1, (int)Math.Round(height * ratio));
                resized = new Drawing.Bitmap(maxWidth, resizedHeight);
                using var g = Drawing.Graphics.FromImage(resized);
                g.InterpolationMode = DrawingDrawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = DrawingDrawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = DrawingDrawing2D.PixelOffsetMode.HighQuality;
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
                    encoderParams.Param[0] = new DrawingImaging.EncoderParameter(encoder, 65L);
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
                ? "Sai tài khoản hoặc mật khẩu."
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

public sealed class MemberTransferBalanceResponse
{
    public MemberLoginItem? SourceMember { get; set; }

    public MemberLoginItem? TargetMember { get; set; }

    public decimal Amount { get; set; }

    public string? TransferredAt { get; set; }
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
