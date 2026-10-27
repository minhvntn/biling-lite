using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Client.Agent.Wpf.Models;

namespace Client.Agent.Wpf.Services;

public sealed class WarpVpnService
{
    private readonly FileLogger _logger;
    private readonly HttpClient _httpClient;
    private VpnStatus _currentStatus = VpnStatus.Disconnected;

    public event Action<VpnStatus>? StatusChanged;

    public VpnStatus CurrentStatus
    {
        get => _currentStatus;
        private set
        {
            if (_currentStatus != value)
            {
                _currentStatus = value;
                StatusChanged?.Invoke(_currentStatus);
            }
        }
    }

    public WarpVpnService(FileLogger logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public string GetWarpCliPath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var warpPath = Path.Combine(programFiles, "Cloudflare", "Cloudflare WARP", "warp-cli.exe");
        if (File.Exists(warpPath))
        {
            return warpPath;
        }

        var warpPathX86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Cloudflare", "Cloudflare WARP", "warp-cli.exe");
        if (File.Exists(warpPathX86))
        {
            return warpPathX86;
        }

        return warpPath;
    }

    public bool IsWarpInstalled()
    {
        return File.Exists(GetWarpCliPath());
    }

    public async Task<VpnStatus> RefreshStatusAsync()
    {
        if (!IsWarpInstalled())
        {
            CurrentStatus = VpnStatus.NotInstalled;
            return CurrentStatus;
        }

        try
        {
            var output = await RunWarpCliAsync("status");
            var lower = output.ToLowerInvariant();

            if (lower.Contains("status update: connected") || lower.Contains("connected") && !lower.Contains("disconnected") && !lower.Contains("connecting"))
            {
                CurrentStatus = VpnStatus.Connected;
            }
            else if (lower.Contains("connecting"))
            {
                CurrentStatus = VpnStatus.Connecting;
            }
            else if (lower.Contains("disconnecting"))
            {
                CurrentStatus = VpnStatus.Disconnecting;
            }
            else
            {
                CurrentStatus = VpnStatus.Disconnected;
            }
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("Failed to refresh WARP status", ex);
            CurrentStatus = VpnStatus.Error;
        }

        return CurrentStatus;
    }

    public async Task<bool> ConnectAsync()
    {
        if (!IsWarpInstalled())
        {
            CurrentStatus = VpnStatus.NotInstalled;
            return false;
        }

        CurrentStatus = VpnStatus.Connecting;
        try
        {
            // Kiểm tra đăng ký (registration)
            var regStatus = await RunWarpCliAsync("registration show");
            if (regStatus.Contains("missing", StringComparison.OrdinalIgnoreCase) || 
                regStatus.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                regStatus.Contains("not registered", StringComparison.OrdinalIgnoreCase))
            {
                await RunWarpCliAsync("registration new");
            }

            // Kết nối WARP
            await RunWarpCliAsync("connect");

            // Chờ một chút để trạng thái cập nhật
            await Task.Delay(1500);
            await RefreshStatusAsync();
            return CurrentStatus == VpnStatus.Connected;
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("WARP Connect failed", ex);
            CurrentStatus = VpnStatus.Error;
            return false;
        }
    }

    public async Task<bool> DisconnectAsync()
    {
        if (!IsWarpInstalled())
        {
            CurrentStatus = VpnStatus.NotInstalled;
            return true;
        }

        CurrentStatus = VpnStatus.Disconnecting;
        try
        {
            await RunWarpCliAsync("disconnect");
            await Task.Delay(1000);
            await RefreshStatusAsync();
            return CurrentStatus == VpnStatus.Disconnected;
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("WARP Disconnect failed", ex);
            CurrentStatus = VpnStatus.Error;
            return false;
        }
    }

    public async Task<bool> TriggerInstallAsync(VpnSettings settings)
    {
        CurrentStatus = VpnStatus.Installing;
        try
        {
            string targetMsiPath = string.Empty;

            // Ưu tiên 1: Kiểm tra file cục bộ
            if (!string.IsNullOrWhiteSpace(settings.LocalInstallerPath) && File.Exists(settings.LocalInstallerPath))
            {
                await _logger.InfoAsync($"Using local installer path: {settings.LocalInstallerPath}");
                targetMsiPath = settings.LocalInstallerPath;
            }
            else
            {
                // Ưu tiên 2: Tải từ URL
                if (string.IsNullOrWhiteSpace(settings.DownloadUrl))
                {
                    await _logger.ErrorAsync("No local installer found and DownloadUrl is empty.");
                    CurrentStatus = VpnStatus.Error;
                    return false;
                }

                var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var installerDir = Path.Combine(root, "ServerManagerBilling", "installer");
                Directory.CreateDirectory(installerDir);
                targetMsiPath = Path.Combine(installerDir, "Cloudflare_WARP_Release-x64.msi");

                await _logger.InfoAsync($"Downloading WARP from {settings.DownloadUrl} to {targetMsiPath}...");
                using var response = await _httpClient.GetAsync(settings.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var streamToReadFrom = await response.Content.ReadAsStreamAsync();
                await using var streamToWriteTo = File.Open(targetMsiPath, FileMode.Create);
                await streamToReadFrom.CopyToAsync(streamToWriteTo);
                await _logger.InfoAsync("WARP download completed.");
            }

            // Ghi file lệnh cho Watchdog Service xử lý ngầm dưới quyền SYSTEM
            var commandsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ServerManagerBilling", "commands");
            Directory.CreateDirectory(commandsDir);
            var cmdFile = Path.Combine(commandsDir, $"install-warp-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.json");

            var escapedPath = targetMsiPath.Replace("\\", "\\\\");
            var json = $"{{\"installerPath\": \"{escapedPath}\"}}";
            await File.WriteAllTextAsync(cmdFile, json);
            await _logger.InfoAsync($"Install command dispatched to Watchdog: {cmdFile}");

            // Theo dõi trong tối đa 120s chờ cài đặt hoàn tất
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(2000);
                if (IsWarpInstalled())
                {
                    await _logger.InfoAsync("WARP detected after silent install.");
                    await RefreshStatusAsync();
                    return true;
                }
            }

            await _logger.ErrorAsync("Timeout waiting for WARP silent install.");
            CurrentStatus = VpnStatus.Error;
            return false;
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("TriggerInstallAsync failed", ex);
            CurrentStatus = VpnStatus.Error;
            return false;
        }
    }

    private async Task<string> RunWarpCliAsync(string arguments)
    {
        var warpCli = GetWarpCliPath();
        if (!File.Exists(warpCli))
        {
            return string.Empty;
        }

        var psi = new ProcessStartInfo
        {
            FileName = warpCli,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return string.Empty;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }
}
