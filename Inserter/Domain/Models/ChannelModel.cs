using System.ComponentModel.DataAnnotations;

namespace Infrastructure.Models;

public class ChannelModel
{
    [Key]
    public int Id { get; set; }
    public string? UserName { get; set; }
    public string? City { get; set; }
    public string? Description { get; set; }
    public string? PhoneNumbers { get; set; } //Phone numbers can be multiple, separated by commas
}
