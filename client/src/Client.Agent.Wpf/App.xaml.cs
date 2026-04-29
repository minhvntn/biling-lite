using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using Client.Agent.Wpf.Models;
using Client.Agent.Wpf.Services;
using Microsoft.Extensions.Configuration;

namespace Client.Agent.Wpf;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private FileLogger? _logger;
    private AgentSettings _settings = new();
    private AgentSocketService? _socketService;
    private LockScreenWindow? _lockScreenWindow;
    private MainWindow? _mainWindow;

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
        _mainWindow.SetConnectionStatus("Connecting...");
        _mainWindow.SetMachineState("LOCKED");
        _mainWindow.SetLastCommand("Boot sequence");
        MainWindow = _mainWindow;
        _mainWindow.Show();

        _lockScreenWindow = new LockScreenWindow();
        _lockScreenWindow.Show();

        _socketService = new AgentSocketService(
            _settings,
            _logger,
            HandleCommandAsync,
            OnConnectionStatusChanged,
            OnAdminNotificationReceived);

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
        _mainWindow?.AllowShutdown();

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
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        var settings = new AgentSettings();
        configuration.GetSection("Agent").Bind(settings);

        if (string.IsNullOrWhiteSpace(settings.AgentId))
        {
            settings.AgentId = Environment.MachineName;
        }

        return settings;
    }

    private Task<(bool Success, string? Message)> HandleCommandAsync(CommandExecutePayload payload)
    {
        try
        {
            var commandType = payload.Type?.Trim().ToUpperInvariant();
            switch (commandType)
            {
                case "OPEN":
                    Dispatcher.Invoke(UnlockMachine);
                    return Task.FromResult<(bool, string?)>((true, "opened"));

                case "LOCK":
                    Dispatcher.Invoke(LockMachine);
                    return Task.FromResult<(bool, string?)>((true, "locked"));

                case "RESTART":
                    _ = _logger?.InfoAsync("Executing RESTART command");
                    TriggerSystemRestart();
                    return Task.FromResult<(bool, string?)>((true, "restart triggered"));

                case "SHUTDOWN":
                    _ = _logger?.InfoAsync("Executing SHUTDOWN command");
                    TriggerSystemShutdown();
                    return Task.FromResult<(bool, string?)>((true, "shutdown triggered"));

                case "CLOSE_APPS":
                    var closedCount = CloseUserApplications();
                    _mainWindow?.SetLastCommand($"CLOSE_APPS @ {DateTime.Now:HH:mm:ss}");
                    return Task.FromResult<(bool, string?)>((true, $"closed {closedCount} app(s)"));

                case "PAUSE":
                    Dispatcher.Invoke(PauseMachine);
                    return Task.FromResult<(bool, string?)>((true, "paused"));

                case "RESUME":
                    Dispatcher.Invoke(UnlockMachine);
                    _mainWindow?.SetLastCommand($"RESUME @ {DateTime.Now:HH:mm:ss}");
                    return Task.FromResult<(bool, string?)>((true, "resumed"));

                default:
                    return Task.FromResult<(bool, string?)>((false, "Unsupported command type"));
            }
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("Failed to execute command", ex);
            return Task.FromResult<(bool, string?)>((false, ex.Message));
        }
    }

    private void LockMachine()
    {
        _lockScreenWindow?.Show();
        _lockScreenWindow?.Activate();

        _mainWindow?.SetMachineState("LOCKED");
        _mainWindow?.SetLastCommand($"LOCK @ {DateTime.Now:HH:mm:ss}");
    }

    private void UnlockMachine()
    {
        _lockScreenWindow?.Hide();
        _mainWindow?.SetMachineState("IN_USE");
        _mainWindow?.SetLastCommand($"OPEN @ {DateTime.Now:HH:mm:ss}");
    }

    private void OnConnectionStatusChanged(string status)
    {
        Dispatcher.Invoke(() => _mainWindow?.SetConnectionStatus(status));
    }

    private void PauseMachine()
    {
        _lockScreenWindow?.Show();
        _lockScreenWindow?.Activate();
        _mainWindow?.SetMachineState("PAUSED");
        _mainWindow?.SetLastCommand($"PAUSE @ {DateTime.Now:HH:mm:ss}");
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
}
