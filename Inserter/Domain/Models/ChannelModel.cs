namespace Domain.Models;

public class ChannelModel
{
    public int Id { get; set; }
    public string? Description { get; set; }
    public string? City { get; set; }
    public string? PhoneNumers { get; set; }
    public required string ChannelUserName { get; set; }
}
