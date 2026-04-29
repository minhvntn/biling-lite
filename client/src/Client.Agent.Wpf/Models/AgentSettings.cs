namespace Client.Agent.Wpf.Models;

public sealed class AgentSettings
{
    public string ServerUrl { get; set; } = "http://localhost:9000";
    public string AgentId { get; set; } = Environment.MachineName;
    public int HeartbeatIntervalSeconds { get; set; } = 10;
    public bool EnableAutoStartup { get; set; } = true;
}
