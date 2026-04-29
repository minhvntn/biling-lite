using System.Diagnostics;
using Client.Watchdog.Service.Models;
using Client.Watchdog.Service.Services;
using Microsoft.Extensions.Configuration;

namespace Client.Watchdog.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly WatchdogSettings _settings;
    private readonly FileLogger _fileLogger;
    private DateTimeOffset _lastRestartAt = DateTimeOffset.MinValue;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _settings = new WatchdogSettings();
        configuration.GetSection("Watchdog").Bind(_settings);

        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var logDir = Path.Combine(root, "ServerManagerBilling", "logs");
        _fileLogger = new FileLogger(Path.Combine(logDir, "watchdog-service.log"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _fileLogger.WriteInfoAsync("Watchdog service started");
        _logger.LogInformation("Watchdog service started");

        var delaySeconds = Math.Max(2, _settings.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorAndRecoverAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchdog loop failed");
                await _fileLogger.WriteErrorAsync("Watchdog loop failed", ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }

        await _fileLogger.WriteInfoAsync("Watchdog service stopped");
    }

    private async Task MonitorAndRecoverAsync(CancellationToken cancellationToken)
    {
        var running = Process.GetProcessesByName(_settings.AgentProcessName).Length > 0;
        if (running)
        {
            return;
        }

        var cooldownSeconds = Math.Max(5, _settings.RestartCooldownSeconds);
        if (DateTimeOffset.UtcNow - _lastRestartAt < TimeSpan.FromSeconds(cooldownSeconds))
        {
            return;
        }

        _lastRestartAt = DateTimeOffset.UtcNow;
        await _fileLogger.WriteInfoAsync(
            $"Agent process '{_settings.AgentProcessName}' not found. Restart mode={_settings.RestartMode}");

        var restarted = await TryRestartAgentAsync(cancellationToken);
        await _fileLogger.WriteInfoAsync(restarted
            ? "Restart trigger sent"
            : "Restart trigger failed");
    }

    private async Task<bool> TryRestartAgentAsync(CancellationToken cancellationToken)
    {
        var mode = _settings.RestartMode?.Trim().ToLowerInvariant();
        if (mode == "scheduled-task")
        {
            return await RunScheduledTaskAsync(_settings.ScheduledTaskName, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(_settings.AgentExecutablePath) ||
            !File.Exists(_settings.AgentExecutablePath))
        {
            await _fileLogger.WriteErrorAsync("Agent executable path invalid");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _settings.AgentExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(_settings.AgentExecutablePath),
                UseShellExecute = true,
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            await _fileLogger.WriteErrorAsync("Failed to start agent executable", ex);
            return false;
        }
    }

    private async Task<bool> RunScheduledTaskAsync(
        string taskName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskName))
        {
            await _fileLogger.WriteErrorAsync("ScheduledTaskName is empty");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Run /TN \"{taskName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                await _fileLogger.WriteErrorAsync("Failed to start schtasks process");
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode == 0)
            {
                return true;
            }

            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await _fileLogger.WriteErrorAsync($"schtasks failed: {error}");
            return false;
        }
        catch (Exception ex)
        {
            await _fileLogger.WriteErrorAsync("Failed to run scheduled task", ex);
            return false;
        }
    }
}
