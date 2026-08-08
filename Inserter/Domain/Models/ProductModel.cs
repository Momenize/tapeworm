using Domain.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Models;

public class ProductModel
{
    [Key]
    public int Id { get; set; }
    public string? ProductName { get; set; }
    [ForeignKey(nameof(Channel))]
    public int ChannelId { get; set; }
    public ChannelModel? Channel { get; set; }
    public CategoryModel? Category { get; set; }
    [ForeignKey(nameof(Category))]
    public int CategoryId { get; set; }
    public decimal? Price { get; set; }
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public string? SellMethod { get; set; }
}
