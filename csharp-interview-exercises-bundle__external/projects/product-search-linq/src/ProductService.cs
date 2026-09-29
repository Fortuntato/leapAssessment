using Microsoft.EntityFrameworkCore;

namespace ProductSearch;

public sealed class ProductService : IProductService
{
    private readonly AppDbContext _db;
    public ProductService(AppDbContext db) => _db = db;

    public Task<Paged<ProductDto>> SearchAsync(ProductQuery query, CancellationToken ct = default)
    {
        // TODO:
        // - Filter by Search across Name/Sku (case-insensitive)
        // - Filter by Category if provided
        // - Sort by created|price|name with asc/desc
        // - Page (cap PageSize at 100)
        // - Project to ProductDto efficiently

        throw new NotImplementedException();
    }
}