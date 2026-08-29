using System.Text.Json.Serialization;

namespace Domain.DTOs;

public sealed class MessageFileChannelDTO
{
    [JsonPropertyName("channel_id")]
    public string ChannelId { get; set; } = null!;

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("description")]
    public string Description { get; set; } = null!;

    [JsonPropertyName("messages")]
    public List<MessageFileMessageDTO> Messages { get; set; } = [];
}

public sealed class MessageFileMessageDTO
{
    [JsonPropertyName("message_url")]
    public string MessageUrl { get; set; } = null!;

    [JsonPropertyName("datetime_utc")]
    public string DatetimeUtc { get; set; } = null!;

    [JsonPropertyName("text")]
    public string Text { get; set; } = null!;

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }
    public int? Index;
}