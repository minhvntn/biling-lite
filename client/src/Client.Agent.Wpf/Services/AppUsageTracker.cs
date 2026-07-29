using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Client.Agent.Wpf.Services;

public sealed class AppUsageTracker
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "svchost", "csrss", "dwm", "lsass", "services", "smss",
        "wininit", "winlogon", "fontdrvhost", "sihost", "taskhostw",
        "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost",
        "RuntimeBroker", "backgroundTaskHost", "TextInputHost",
        "SecurityHealthSystray", "ctfmon", "conhost", "dllhost",
        "ApplicationFrameHost", "SystemSettings", "LockApp",
        "CompPkgSrv", "NisSrv", "MsMpEng", "SearchIndexer",
        "spoolsv", "WmiPrvSE", "audiodg", "Registry",
        "System", "Idle", "Client.Agent.Wpf", "Server.Admin.App"
    };

    public static List<AppInfo> GetRunningApps()
    {
        var apps = new List<AppInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var foregroundHwnd = GetForegroundWindow();
            uint foregroundPid = 0;
            if (foregroundHwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(foregroundHwnd, out foregroundPid);
            }

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                        continue;

                    var name = proc.ProcessName;
                    if (SystemProcesses.Contains(name))
                        continue;

                    if (!seen.Add(name))
                        continue;

                    apps.Add(new AppInfo
                    {
                        ProcessId = proc.Id,
                        Name = name,
                        Title = proc.MainWindowTitle.Length > 200
                            ? proc.MainWindowTitle[..200]
                            : proc.MainWindowTitle,
                        IsForeground = proc.Id == (int)foregroundPid
                    });
                }
                catch
                {
                    // Skip processes that throw access denied
                }
            }
        }
        catch
        {
            // Swallow
        }

        return apps;
    }

    public class AppInfo
    {
        public int ProcessId { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsForeground { get; set; }
    }
}
