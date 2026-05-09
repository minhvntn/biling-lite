namespace Client.Agent.Wpf.Models;
using System.Text.Json.Serialization;

public sealed class CommandExecutePayload
{
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("issuedAt")]
    public string? IssuedAt { get; set; }

    [JsonPropertyName("hourlyRate")]
    public decimal? HourlyRate { get; set; }

    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    [JsonPropertyName("pcId")]
    public string? PcId { get; set; }
}
