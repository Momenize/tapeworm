using Infrastructure.Models;
using System.Linq.Expressions;

namespace Domain.DTOs;

public class ProductDTO
{
    public required int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public string? Brand { get; set; }
    public string? SellMethod { get; set; }
    public string? Category { get; set; }
    public static Expression<Func<ProductModel, ProductDTO>> Expr()
    {
        return x => new ProductDTO
        {
            Id = x.Id,
            Name = x.ProductName,
            Brand = x.Brand,
            SellMethod = x.SellMethod,
            Price = x.Price,
            Description = x.Description,
            Category = x.Category != null ? x.Category.CategoryName : null
        };
    }
}
