using Microsoft.Win32;

namespace Client.Agent.Wpf.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupName = "ServerManagerBillingAgent";

    public void EnsureCurrentUserStartup(string executablePath)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                           ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        runKey?.SetValue(StartupName, $"\"{executablePath}\"");
    }
}
