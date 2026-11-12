using AiPlatform.Domain.Entities.Erp;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AiPlatform.Application.Tests.Erp;

// warranty seed ভুল হলে AI আত্মবিশ্বাসের সাথে ভুল মেয়াদ বলবে — তাই তিনটে জিনিস পাহারা দিই:
// প্রতি branch-এ row বসেছে, একাধিক type কাজ করে, আর দ্বিতীয়বার চালালে duplicate হয় না।
public class ErpSeederWarrantyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ErpDbContext _db;

    public ErpSeederWarrantyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ErpDbContext(options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Seed_GivesEachBranchItsOwnWarrantyRows()
    {
        await ErpSeeder.SeedAsync(_db);

        // Dell XPS 13: Service + Parts, ২ branch = ৪ row।
        var xps = await WarrantiesOf("Dell XPS 13");
        Assert.Equal(4, xps.Count);
        Assert.Equal(2, xps.Count(w => w.WarrantyType == WarrantyType.Service && w.DurationMonths == 24));
        Assert.Equal(2, xps.Count(w => w.WarrantyType == WarrantyType.Parts && w.DurationMonths == 12));
    }

    [Fact]
    public async Task Seed_LeavesLifetimeDurationNullAndUncoveredProductEmpty()
    {
        await ErpSeeder.SeedAsync(_db);

        var usb = await WarrantiesOf("SanDisk USB 32GB");
        Assert.All(usb, w => Assert.Equal(WarrantyType.Lifetime, w.WarrantyType));
        Assert.All(usb, w => Assert.Null(w.DurationMonths));   // আজীবন = মেয়াদ নেই

        Assert.Empty(await WarrantiesOf("HDMI Cable 2m"));     // ইচ্ছাকৃতভাবে warranty-হীন
    }

    [Fact]
    public async Task Seed_IsIdempotent()
    {
        await ErpSeeder.SeedAsync(_db);
        var first = await _db.ProductWarranties.CountAsync();

        await ErpSeeder.SeedAsync(_db);

        Assert.Equal(first, await _db.ProductWarranties.CountAsync());
    }

    private async Task<List<ProductWarranty>> WarrantiesOf(string productName)
        => await _db.ProductWarranties
            .Where(w => w.Product.Name == productName)
            .ToListAsync();

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
