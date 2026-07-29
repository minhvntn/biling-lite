using System;
using System.Text.Json.Serialization;

namespace Server.Admin.App.Models;

public class SupportMessageDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("pcId")]
    public string PcId { get; set; } = string.Empty;

    [JsonPropertyName("pcName")]
    public string PcName { get; set; } = string.Empty;

    [JsonPropertyName("sentBy")]
    public string SentBy { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = string.Empty;

    public string DirectionText => Direction == "TO_ADMIN" ? "Đến Admin" : "Đến Client";

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("isRead")]
    public bool IsRead { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
