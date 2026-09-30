using Microsoft.EntityFrameworkCore;

namespace ProductSearch;

public sealed class ProductService : IProductService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly AppDbContext _db;
    public ProductService(AppDbContext db) => _db = db;

    /// <summary>
    /// Search product using the specified query filters, sorting, and pagination, and returns a page of product results.
    /// </summary>
    /// <remarks>Page numbers less than 1 are treated as 1. The page size is normalized. If no products match
    /// or the requested page is beyond the last page, the result contains an empty item collection with the total count.</remarks>
    /// <param name="query">Filtering, sorting, and pagination criteria for the search.</param>
    /// <param name="ct">Cancellation token used to cancel the asynchronous operation.</param>
    /// <returns>A paged result containing product items for the requested page and the total number of matching products.</returns>
    public async Task<Paged<ProductDto>> SearchAsync(ProductQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var pageSize = NormalizePageSize(query.PageSize);
        var filtered = ApplyFilters(_db.Products.AsNoTracking(), query);
        var total = await filtered.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;

        // No data to return
        if (skip >= total)
            return new Paged<ProductDto>(Array.Empty<ProductDto>(), total, page, pageSize);

        var items = await ApplySort(filtered, query.SortBy, query.Desc)
            .Skip((int)skip)
            .Take(pageSize)
            .Select(p => new ProductDto(p.Id, p.Sku, p.Name, p.Price))
            .ToListAsync(ct);

        return new Paged<ProductDto>(items, total, page, pageSize);
    }

    /// <summary>
    /// Applies search and category filters from the query to the product sequence.
    /// </summary>
    /// <remarks>Search matches product name or SKU using trimmed, case-insensitive containment. Category
    /// matches an exact trimmed, case-insensitive value.</remarks>
    /// <param name="products">The product queryable to filter.</param>
    /// <param name="query">The filter criteria containing optional search text and category.</param>
    /// <returns>An <see cref="IQueryable{T}"/> of <see cref="Product"/> filtered by the provided criteria.</returns>
    private static IQueryable<Product> ApplyFilters(IQueryable<Product> products, ProductQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            products = products.Where(p => p.Name.ToLower().Contains(term) || p.Sku.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim().ToLower();
            products = products.Where(p => p.Category.ToLower() == category);
        }

        return products;
    }

    /// <summary>
    /// Applies sorting to a product query by price, name, or creation time, with a stable ID tie-breaker.
    /// </summary>
    /// <remarks>Sort key matching is case-insensitive and ignores leading and trailing whitespace.</remarks>
    /// <param name="products">The product query to sort.</param>
    /// <param name="sortBy">The sort key. Supported values are "price" and "name"; null, empty, or unrecognized values use creation time.</param>
    /// <param name="desc">true to sort in descending order; otherwise ascending order.</param>
    /// <returns>An <see cref="IQueryable{T}"/> of <see cref="Product"/> ordered by the selected sort key and then by ID.</returns>
    private static IQueryable<Product> ApplySort(IQueryable<Product> products, string? sortBy, bool desc)
    {
        var key = sortBy?.Trim().ToLowerInvariant();

        return (key, desc) switch
        {
            ("price", true) => products.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
            ("price", false) => products.OrderBy(p => p.Price).ThenBy(p => p.Id),
            ("name", true) => products.OrderByDescending(p => p.Name).ThenBy(p => p.Id),
            ("name", false) => products.OrderBy(p => p.Name).ThenBy(p => p.Id),
            (_, true) => products.OrderByDescending(p => p.CreatedUtc).ThenBy(p => p.Id),
            (_, false) => products.OrderBy(p => p.CreatedUtc).ThenBy(p => p.Id),
        };
    }

    private static int NormalizePageSize(int requested) =>
        requested <= 0 ? DefaultPageSize : Math.Min(requested, MaxPageSize);
}
