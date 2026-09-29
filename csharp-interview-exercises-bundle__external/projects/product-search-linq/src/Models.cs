using Microsoft.EntityFrameworkCore;

namespace ProductSearch;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Product> Products => Set<Product>();
}

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed record ProductDto(int Id, string Sku, string Name, decimal Price);

public sealed class ProductQuery
{
    public string? Search { get; set; }
    public string? Category { get; set; }
    public string SortBy { get; set; } = "created"; // created | price | name
    public bool Desc { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public interface IProductService
{
    Task<Paged<ProductDto>> SearchAsync(ProductQuery query, CancellationToken ct = default);
}