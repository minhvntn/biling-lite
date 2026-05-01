namespace Client.Agent.Wpf.Models;

public sealed class CommandExecutePayload
{
    public string CommandId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? IssuedAt { get; set; }
    public decimal? HourlyRate { get; set; }
}
