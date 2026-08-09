namespace Domain.DTOs;

public sealed class ExtractedChannelDTO
{
    public string? Description { get; set; }
    public string? City { get; set; }
    public string? PhoneNumbers { get; set; }

    public List<ExtractedProductDTO> Products { get; set; } = [];
}

public sealed class ExtractedProductDTO
{
    public string Name { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string? Brand { get; set; }
    public string? PurchaseMethod { get; set; }
    public decimal? Price { get; set; }
    public string? Description { get; set; }
    public string MessageUrl { get; set; } = string.Empty;
}
