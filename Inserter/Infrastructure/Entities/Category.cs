using System.ComponentModel.DataAnnotations;

namespace Infrastructure.Entities;

public class Category
{
    [Key]
    public int Id { get; set; }
    public string CategoryName { get; set; } = string.Empty;
}
