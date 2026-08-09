using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

public class Product
{
    [Key]
    public int Id { get; set; }
    [ForeignKey(nameof(Category))]
    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    [ForeignKey(nameof(Channel))]
    public int ChannelId { get; set; }
    public Channel? Channel { get; set; }

    public string? Brand { get; set; }
    public string? PurchaseMethod { get; set; }
    public decimal? Price { get; set; }
    public string? Description { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MessageUrl { get; set; } = string.Empty;
}
