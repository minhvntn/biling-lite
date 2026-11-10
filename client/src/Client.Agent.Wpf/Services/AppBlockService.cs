using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Client.Agent.Wpf.Services;

/// <summary>
/// Service responsible for enforcing application and system tool blocking on client workstations.
/// Receives configuration from Server (APP_BLOCK_ENABLED, APP_BLOCK_LIST),
/// sets Windows Group Policies (DisableTaskMgr, DisableRegistryTools, NoRun, etc.),
/// and runs an active process monitor loop to terminate blacklisted applications instantly.
/// </summary>
public sealed class AppBlockService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private const uint WM_CLOSE = 0x0010;
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    public bool IsEnabled { get; private set; }
    public HashSet<string> BlockedKeys { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Callback invoked when a blocked app is terminated, with the friendly display name.
    /// Throttled to avoid flooding.
    /// </summary>
    public Action<string>? OnAppBlocked { get; set; }

    private readonly FileLogger? _logger;
    private readonly DispatcherTimer _scanTimer;
    private readonly ConcurrentDictionary<string, DateTime> _lastAlertPerKey = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    // Mapping key -> User-friendly display name
    private static readonly Dictionary<string, string> KeyDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["taskmgr"] = "Task Manager",
        ["cmd"] = "Command Prompt (cmd)",
        ["powershell"] = "PowerShell",
        ["win_run"] = "Run Dialog (Win+R)",
        ["win_sleep"] = "Windows Sleep / Hibernate",
        ["regedit"] = "Registry Editor",
        ["control"] = "Control Panel",
        ["ms_settings"] = "Windows Settings",
        ["devmgmt"] = "Device Manager",
        ["diskmgmt"] = "Disk Management",
        ["compmgmt"] = "Computer Management",
        ["gpedit"] = "Group Policy Editor",
        ["msconfig"] = "System Configuration",
        ["eventvwr"] = "Event Viewer",
        ["explorer"] = "Windows Explorer",
        ["msedge"] = "Microsoft Edge",
        ["notepad"] = "Notepad",
        ["mstsc"] = "Remote Desktop (mstsc)",
        ["snippingtool"] = "Snipping Tool",
        ["wt"] = "Windows Terminal",
        ["resmon"] = "Resource Monitor",
        ["perfmon"] = "Performance Monitor",
        ["ncpa"] = "Network Connections",
        ["wf"] = "Firewall Settings",
        ["cleanmgr"] = "Disk Cleanup",
        ["rstrui"] = "System Restore",
        ["win_update"] = "Windows Update",
        ["usb_block"] = "USB / Removable Drives",
        ["printscreen"] = "Screenshot (Print Screen)",
    };

    // Mapping process name -> key
    private static readonly Dictionary<string, string> ProcessNameToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["taskmgr"] = "taskmgr",
        ["cmd"] = "cmd",
        ["powershell"] = "powershell",
        ["powershell_ise"] = "powershell",
        ["pwsh"] = "powershell",
        ["regedit"] = "regedit",
        ["control"] = "control",
        ["systemsettings"] = "ms_settings",
        ["msconfig"] = "msconfig",
        ["msedge"] = "msedge",
        ["notepad"] = "notepad",
        ["mstsc"] = "mstsc",
        ["snippingtool"] = "snippingtool",
        ["screenclippinghost"] = "snippingtool",
        ["windowsterminal"] = "wt",
        ["resmon"] = "resmon",
        ["perfmon"] = "perfmon",
        ["cleanmgr"] = "cleanmgr",
        ["rstrui"] = "rstrui",
        ["wuauclt"] = "win_update",
    };

    public AppBlockService(FileLogger? logger = null)
    {
        _logger = logger;
        _scanTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _scanTimer.Tick += ScanTimer_Tick;
        _scanTimer.Start();
    }

    /// <summary>
    /// Update settings from Server. Applies Windows Group Policies and scans active processes.
    /// </summary>
    public void UpdateSettings(bool enabled, IEnumerable<string>? blockedKeys)
    {
        IsEnabled = enabled;
        BlockedKeys = new HashSet<string>(blockedKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        _ = _logger?.InfoAsync($"[AppBlockService] Updated settings: Enabled={IsEnabled}, BlockedCount={BlockedKeys.Count}");

        // Apply or revert native Windows Registry policies
        ApplyRegistryPolicies();

        // Immediate scan to terminate already running apps
        if (IsEnabled && BlockedKeys.Count > 0)
        {
            ScanAndEnforce();
        }
    }

    private void ScanTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsEnabled || BlockedKeys.Count == 0 || _disposed)
        {
            return;
        }

        ScanAndEnforce();
    }

    /// <summary>
    /// Scans running processes and terminates any matching blocked rules.
    /// </summary>
    public void ScanAndEnforce()
    {
        try
        {
            var myPid = Environment.ProcessId;

            // 1. Close Windows Run dialog if blocked
            if (BlockedKeys.Contains("win_run"))
            {
                CloseRunDialog();
            }

            // 2. Scan active processes
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    if (proc.Id == myPid) continue;

                    var procName = proc.ProcessName;

                    // Direct executable match
                    if (ProcessNameToKey.TryGetValue(procName, out var matchedKey))
                    {
                        if (BlockedKeys.Contains(matchedKey))
                        {
                            TerminateBlockedProcess(proc, matchedKey);
                            continue;
                        }
                    }

                    // Special: MMC-based system utilities
                    if (procName.Equals("mmc", StringComparison.OrdinalIgnoreCase))
                    {
                        var mmcKey = DetectMmcToolKey(proc);
                        if (mmcKey != null && BlockedKeys.Contains(mmcKey))
                        {
                            TerminateBlockedProcess(proc, mmcKey);
                            continue;
                        }
                    }

                    // Special: Windows Explorer folder windows
                    if (procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && BlockedKeys.Contains("explorer"))
                    {
                        // Don't kill explorer.exe shell, but close open folder windows
                        if (!string.IsNullOrWhiteSpace(proc.MainWindowTitle) && proc.MainWindowHandle != IntPtr.Zero)
                        {
                            PostMessage(proc.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                            NotifyBlocked("explorer");
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible processes (system protected)
                }
            }
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("[AppBlockService] Error during process scan", ex);
        }
    }

    private void TerminateBlockedProcess(Process proc, string key)
    {
        try
        {
            var name = proc.ProcessName;
            var pid = proc.Id;
            proc.Kill();
            _ = _logger?.WarnAsync($"[AppBlockService] Blocked & killed process '{name}' (PID={pid}) under rule '{key}'");
            NotifyBlocked(key);
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync($"[AppBlockService] Failed to kill blocked process {proc.ProcessName}", ex);
        }
    }

    private void NotifyBlocked(string key)
    {
        var now = DateTime.UtcNow;
        if (!_lastAlertPerKey.TryGetValue(key, out var last) || (now - last).TotalSeconds >= 5)
        {
            _lastAlertPerKey[key] = now;
            var displayName = KeyDisplayNames.TryGetValue(key, out var dn) ? dn : key;
            OnAppBlocked?.Invoke(displayName);
        }
    }

    private static string? DetectMmcToolKey(Process proc)
    {
        try
        {
            var title = proc.MainWindowTitle;
            if (string.IsNullOrWhiteSpace(title)) return null;

            title = title.ToLowerInvariant();
            if (title.Contains("device manager") || title.Contains("quản lý thiết bị") || title.Contains("devmgmt"))
                return "devmgmt";
            if (title.Contains("disk management") || title.Contains("quản lý đĩa") || title.Contains("diskmgmt"))
                return "diskmgmt";
            if (title.Contains("computer management") || title.Contains("quản lý máy tính") || title.Contains("compmgmt"))
                return "compmgmt";
            if (title.Contains("group policy") || title.Contains("chính sách nhóm") || title.Contains("gpedit"))
                return "gpedit";
            if (title.Contains("event viewer") || title.Contains("trình xem sự kiện") || title.Contains("eventvwr"))
                return "eventvwr";
            if (title.Contains("firewall") || title.Contains("tường lửa") || title.Contains("wf.msc"))
                return "wf";
            if (title.Contains("network connections") || title.Contains("kết nối mạng") || title.Contains("ncpa"))
                return "ncpa";
        }
        catch { }

        return null;
    }

    private static void CloseRunDialog()
    {
        try
        {
            var hWnd = FindWindow("#32770", "Run");
            if (hWnd == IntPtr.Zero)
            {
                hWnd = FindWindow("#32770", "Chạy");
            }
            if (hWnd != IntPtr.Zero)
            {
                PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch { }
    }

    private void ApplyRegistryPolicies()
    {
        try
        {
            // Task Manager policy
            var blockTaskMgr = IsEnabled && BlockedKeys.Contains("taskmgr");
            SetCurrentUserPolicy("System", "DisableTaskMgr", blockTaskMgr);

            // Registry tools policy
            var blockRegedit = IsEnabled && BlockedKeys.Contains("regedit");
            SetCurrentUserPolicy("System", "DisableRegistryTools", blockRegedit);

            // Run dialog policy
            var blockRun = IsEnabled && BlockedKeys.Contains("win_run");
            SetCurrentUserPolicy("Explorer", "NoRun", blockRun);

            // Control panel policy
            var blockControl = IsEnabled && BlockedKeys.Contains("control");
            SetCurrentUserPolicy("Explorer", "NoControlPanel", blockControl);

            // Sleep / Hibernate prevention
            if (IsEnabled && BlockedKeys.Contains("win_sleep"))
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            }
            else
            {
                SetThreadExecutionState(ES_CONTINUOUS);
            }

            // USB block (best effort if running with appropriate rights)
            if (IsEnabled && BlockedKeys.Contains("usb_block"))
            {
                SetLocalMachineUsbStorage(block: true);
            }
            else
            {
                SetLocalMachineUsbStorage(block: false);
            }
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("[AppBlockService] Failed to apply registry policies", ex);
        }
    }

    private static void SetCurrentUserPolicy(string subKeyName, string valueName, bool enable)
    {
        try
        {
            var path = $@"Software\Microsoft\Windows\CurrentVersion\Policies\{subKeyName}";
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
            if (key != null)
            {
                if (enable)
                {
                    key.SetValue(valueName, 1, RegistryValueKind.DWord);
                }
                else
                {
                    key.DeleteValue(valueName, throwOnMissingValue: false);
                }
            }
        }
        catch { }
    }

    private static void SetLocalMachineUsbStorage(bool block)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\USBSTOR", writable: true);
            if (key != null)
            {
                // Start = 4 disables USB mass storage driver; Start = 3 enables it
                key.SetValue("Start", block ? 4 : 3, RegistryValueKind.DWord);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _scanTimer.Stop();

            // Revert all policies to clean state on agent exit
            SetCurrentUserPolicy("System", "DisableTaskMgr", false);
            SetCurrentUserPolicy("System", "DisableRegistryTools", false);
            SetCurrentUserPolicy("Explorer", "NoRun", false);
            SetCurrentUserPolicy("Explorer", "NoControlPanel", false);
            SetThreadExecutionState(ES_CONTINUOUS);
        }
        catch { }
    }
}

