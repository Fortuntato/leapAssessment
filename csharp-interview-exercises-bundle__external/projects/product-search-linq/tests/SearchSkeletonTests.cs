using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProductSearch;
using Xunit;

public class SearchSkeletonTests
{
    private static AppDbContext CreateDb(int extraProducts = 0)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(opts);
        db.Products.AddRange(
            new Product { Sku = "A1", Name = "Apple", Category = "Fruit", Price = 1.0m, CreatedUtc = DateTime.UtcNow.AddDays(-3) },
            new Product { Sku = "B2", Name = "Banana", Category = "Fruit", Price = 0.5m, CreatedUtc = DateTime.UtcNow.AddDays(-2) },
            new Product { Sku = "C3", Name = "Carrot", Category = "Veg", Price = 0.8m, CreatedUtc = DateTime.UtcNow.AddDays(-1) }
        );
        for (var i = 0; i < extraProducts; i++)
        {
            db.Products.Add(new Product
            {
                Sku = $"X{i:000}",
                Name = $"Extra {i:000}",
                Category = "Bulk",
                Price = 10 + i,
                CreatedUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        db.SaveChanges();
        return db;
    }

    private static string[] Names(Paged<ProductDto> result) => result.Items.Select(i => i.Name).ToArray();

    [Fact]
    public async Task DefaultQuery_ReturnsAll_NewestFirst()
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery());

        Assert.Equal(3, result.Total);
        Assert.Equal(new[] { "Carrot", "Banana", "Apple" }, Names(result));
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Theory]
    [InlineData("apple")]
    [InlineData("APPLE")]
    [InlineData("a1")]
    [InlineData("  pple  ")]
    public async Task Search_MatchesNameOrSku_CaseInsensitive(string term)
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery { Search = term });

        Assert.Equal(new[] { "Apple" }, Names(result));
    }

    [Fact]
    public async Task Category_FiltersCaseInsensitive()
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery { Category = "veg" });

        Assert.Equal(new[] { "Carrot" }, Names(result));
    }

    [Fact]
    public async Task Search_And_Category_AreCombined()
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery { Search = "a", Category = "Fruit", SortBy = "name", Desc = false });

        Assert.Equal(new[] { "Apple", "Banana" }, Names(result));
        Assert.Equal(2, result.Total);
    }

    [Theory]
    [InlineData("price", false, new[] { "Banana", "Carrot", "Apple" })]
    [InlineData("price", true, new[] { "Apple", "Carrot", "Banana" })]
    [InlineData("name", false, new[] { "Apple", "Banana", "Carrot" })]
    [InlineData("name", true, new[] { "Carrot", "Banana", "Apple" })]
    [InlineData("created", false, new[] { "Apple", "Banana", "Carrot" })]
    [InlineData("created", true, new[] { "Carrot", "Banana", "Apple" })]
    [InlineData("PRICE", false, new[] { "Banana", "Carrot", "Apple" })]
    public async Task Sort_OrdersByRequestedKeyAndDirection(string sortBy, bool desc, string[] expected)
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery { SortBy = sortBy, Desc = desc });

        Assert.Equal(expected, Names(result));
    }

    [Theory]
    [InlineData("leap")]
    [InlineData("")]
    [InlineData(null)]
    public async Task InvalidSort_FallsBackToCreated(string? sortBy)
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery { SortBy = sortBy!, Desc = false });

        Assert.Equal(new[] { "Apple", "Banana", "Carrot" }, Names(result));
    }

    [Fact]
    public async Task Paging_ReturnsRequestedSlice_AndTotal()
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(
            new ProductQuery { SortBy = "name", Desc = false, Page = 2, PageSize = 2 });

        Assert.Equal(new[] { "Carrot" }, Names(result));
        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
    }

    [Fact]
    public async Task PageSize_IsCappedAt100()
    {
        await using var db = CreateDb(extraProducts: 150);
        var result = await new ProductService(db).SearchAsync(new ProductQuery { PageSize = 1000 });

        Assert.Equal(100, result.PageSize);
        Assert.Equal(100, result.Items.Count);
        Assert.Equal(153, result.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task NonPositivePageSize_UsesDefault(int pageSize)
    {
        await using var db = CreateDb(extraProducts: 30);
        var result = await new ProductService(db).SearchAsync(new ProductQuery { PageSize = pageSize });

        Assert.Equal(20, result.PageSize);
        Assert.Equal(20, result.Items.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task NonPositivePage_ClampsToFirstPage(int page)
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery { Page = page });

        Assert.Equal(1, result.Page);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task PageBeyondLast_ReturnsEmptyItems_WithTotal()
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery { Page = 99 });

        Assert.Empty(result.Items);
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task NoMatches_ReturnsEmptyResult()
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery { Search = "zzz" });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Projection_MapsDtoFields()
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery { Search = "banana" });

        var dto = Assert.Single(result.Items);
        Assert.Equal("B2", dto.Sku);
        Assert.Equal("Banana", dto.Name);
        Assert.Equal(0.5m, dto.Price);
    }

    [Theory]
    [InlineData("   ", null)]
    [InlineData(null, "   ")]
    [InlineData("", "")]
    public async Task WhitespaceOrEmptyFilters_AreIgnored(string? search, string? category)
    {
        await using var db = CreateDb();
        var result = await new ProductService(db).SearchAsync(new ProductQuery { Search = search, Category = category });

        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        await using var db = CreateDb();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProductService(db).SearchAsync(new ProductQuery(), cts.Token));
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ProductService(db).SearchAsync(null!));
    }
}
