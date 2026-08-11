using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

public class Channel
{
    [Key]
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? City { get; set; }
    public string? PhoneNumbers { get; set; }
    [InverseProperty(nameof(Product.Channel))]
    public List<Product> Products { get; set; } = [];
}
