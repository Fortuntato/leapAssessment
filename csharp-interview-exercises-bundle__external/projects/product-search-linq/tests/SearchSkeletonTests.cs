using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProductSearch;
using Xunit;

public class SearchSkeletonTests
{
    private static AppDbContext CreateDb()
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
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task NotImplemented_Until_Candidate_Writes_Query()
    {
        await using var db = CreateDb();
        var svc = new ProductService(db);
        await Assert.ThrowsAsync<NotImplementedException>(() => svc.SearchAsync(new ProductQuery()));
        // Replace the above assertion with real checks once implemented.
    }
}