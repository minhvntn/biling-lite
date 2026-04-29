namespace Client.Watchdog.Service.Models;

public sealed class WatchdogSettings
{
    public string AgentProcessName { get; set; } = "Client.Agent.Wpf";
    public string AgentExecutablePath { get; set; } =
        @"C:\Program Files\ServerManagerBilling\Client.Agent.Wpf.exe";
    public string RestartMode { get; set; } = "scheduled-task";
    public string ScheduledTaskName { get; set; } = "ServerManagerBillingAgent";
    public int CheckIntervalSeconds { get; set; } = 5;
    public int RestartCooldownSeconds { get; set; } = 20;
}
