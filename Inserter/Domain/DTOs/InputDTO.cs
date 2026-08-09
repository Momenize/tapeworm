namespace Domain.DTOs;

public sealed class ChannelInputDTO
{
    public string ChannelId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Description { get; set; }
    public List<MessageInputDTO> Messages { get; set; } = [];
}

public sealed class MessageInputDTO
{
    public string MessageUrl { get; set; } = string.Empty;
    public string DatetimeUtc { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
