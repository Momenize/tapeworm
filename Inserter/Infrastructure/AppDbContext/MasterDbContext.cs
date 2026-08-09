using Microsoft.EntityFrameworkCore;
using Infrastructure.Entities;

namespace Infrastructure.AppDbContext;

public class MasterDbContext(DbContextOptions<MasterDbContext> options) : DbContext(options)
{
    public DbSet<Channel> Channels { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
}
