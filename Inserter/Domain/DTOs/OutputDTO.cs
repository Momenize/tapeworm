namespace Domain.DTOs;

public class ChannelOutputDTO
{
    public string? City { get; set; }
    public string? PhoneNumbers { get; set; }
    public List<ProductOutputDTO> Products { get; set; } = [];
}

public class ProductOutputDTO
{
    public string Name { get; set; } = string.Empty;
    public string? CategoryName { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? PurchaseMethod { get; set; }
    public decimal? Price { get; set; }
    public string? Description { get; set; }
    public int Index { get; set; }
}