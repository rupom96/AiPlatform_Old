using System.Text.Json;
using AiPlatform.Domain.Entities.Erp;
using AiPlatform.Infrastructure.Agent;
using AiPlatform.Infrastructure.Erp;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AiPlatform.Application.Tests.Erp;

// stock ভুল বললে ব্যবসায়িক সিদ্ধান্ত ভুল হয় ("আছে" বলে বিক্রি করে ফেলা)।
// data ErpStockService দিয়ে ঢোকানো — hand-written row নয়, আসল approval path।
public class GetStockToolTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ErpDbContext _db;
    private readonly GetStockTool _tool;

    private const long UserId = 1;
    private long _companyId;
    private long _hoId;
    private long _cbId;

    public GetStockToolTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlite(_connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
        _db.Database.EnsureCreated();
        _tool = new GetStockTool(_db);

        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task NoFilter_ListsEverythingHighestFirst()
    {
        var result = await Run(new { });

        var rows = Rows(result);
        Assert.Equal(4, rows.Count);   // Dell×2 location, Mouse HO, ProBook CB(শূন্য)
        Assert.Equal("Logitech M170 Mouse", rows[0].GetProperty("product").GetString());
        Assert.Equal(50, rows[0].GetProperty("quantityOnHand").GetInt32());
    }

    [Fact]
    public async Task ProductFilter_MatchesPartialName()
    {
        var result = await Run(new { productName = "Dell" });

        var rows = Rows(result);
        Assert.Equal(2, rows.Count);   // দুই branch-এ আলাদা row
        Assert.All(rows, r => Assert.Contains("Dell", r.GetProperty("product").GetString()));
    }

    [Fact]
    public async Task LocationFilter_MatchesCodeOrName()
    {
        var byCode = await Run(new { location = "CB" });
        var byName = await Run(new { location = "Chittagong" });

        Assert.Equal(Rows(byCode).Count, Rows(byName).Count);
        Assert.All(Rows(byCode), r => Assert.Equal("CB", r.GetProperty("location").GetString()));
    }

    // "কী কমে যাচ্ছে" — কম-আগে সাজানো (উল্টোটা হলে জরুরি জিনিস নিচে চাপা পড়ত)
    [Fact]
    public async Task BelowQuantity_ReturnsOnlyLowStockLowestFirst()
    {
        var result = await Run(new { belowQuantity = 6 });

        var rows = Rows(result);
        Assert.All(rows, r => Assert.True(r.GetProperty("quantityOnHand").GetInt32() < 6));

        var quantities = rows.Select(r => r.GetProperty("quantityOnHand").GetInt32()).ToList();
        Assert.Equal(quantities.OrderBy(q => q), quantities);
    }

    // সব বিক্রি হয়ে গেলে row থেকেই যায় — "০ আছে" নিজেই একটা উত্তর, চুপ করে বাদ দেওয়া নয়।
    [Fact]
    public async Task ZeroStockRow_IsStillReported()
    {
        var result = await Run(new { productName = "ProBook" });

        var rows = Rows(result);
        Assert.Single(rows);
        Assert.Equal(0, rows[0].GetProperty("quantityOnHand").GetInt32());
    }

    [Fact]
    public async Task IncludeSerials_ListsSerialsForSerializedProductOnly()
    {
        var result = await Run(new { location = "HO", includeSerials = true });

        var dell = Rows(result).Single(r => r.GetProperty("product").GetString()!.Contains("Dell"));
        Assert.Equal(2, dell.GetProperty("serials").GetProperty("count").GetInt32());

        // accessory-তে serial নেই — null আসা উচিত, খালি তালিকা নয়
        var mouse = Rows(result).Single(r => r.GetProperty("product").GetString()!.Contains("Mouse"));
        Assert.Equal(JsonValueKind.Null, mouse.GetProperty("serials").ValueKind);
    }

    // দুই branch-এ একই পণ্য থাকলে serial মিশে যাওয়া সবচেয়ে সহজ ভুল।
    [Fact]
    public async Task IncludeSerials_KeepsEachLocationsSerialsSeparate()
    {
        var result = await Run(new { productName = "Dell", includeSerials = true });

        var ho = Rows(result).Single(r => r.GetProperty("location").GetString() == "HO");
        var cb = Rows(result).Single(r => r.GetProperty("location").GetString() == "CB");

        var hoSerials = Serials(ho);
        var cbSerials = Serials(cb);

        Assert.Equal(["HO-1", "HO-2"], hoSerials.Order());
        Assert.Equal(["CB-1"], cbSerials);
        Assert.Empty(hoSerials.Intersect(cbSerials));
    }

    [Fact]
    public async Task WithoutIncludeSerials_SerialsAreOmitted()
    {
        var result = await Run(new { productName = "Dell" });

        Assert.Equal(JsonValueKind.Null, Rows(result)[0].GetProperty("serials").ValueKind);
    }

    [Fact]
    public async Task SoldSerial_NoLongerInStock()
    {
        var dell = await _db.Products.FirstAsync(p => p.LocationId == _hoId && p.Name == "Dell XPS 13");
        var sale = await AddSale(_hoId, new DateOnly(2026, 7, 1), (dell, 1, 160000m), ["HO-1"]);
        await new ErpStockService(_db).ApproveSalesAsync(sale, UserId);

        var result = await Run(new { productName = "Dell", location = "HO", includeSerials = true });

        var row = Rows(result)[0];
        Assert.Equal(1, row.GetProperty("quantityOnHand").GetInt32());
        Assert.Equal(["HO-2"], Serials(row));
    }

    [Fact]
    public async Task NoMatch_ReturnsZeroRowsWithNote()
    {
        var result = await Run(new { productName = "Nothing" });

        Assert.Equal(0, result.GetProperty("rowCount").GetInt32());
        Assert.True(result.TryGetProperty("note", out _));
    }

    [Fact]
    public async Task NothingRunningLow_SaysSoInsteadOfLookingBroken()
    {
        var result = await Run(new { belowQuantity = 0 });

        Assert.Equal(0, result.GetProperty("rowCount").GetInt32());
        Assert.Contains("running low", result.GetProperty("note").GetString());
    }

    [Fact]
    public async Task NegativeBelowQuantity_ReturnsError()
    {
        var result = await Run(new { belowQuantity = -5 });

        Assert.True(result.TryGetProperty("error", out _));
    }

    // ছোট model boolean/সংখ্যা string হিসেবে পাঠাতে পারে।
    [Fact]
    public async Task AcceptsBooleanAndNumberSentAsString()
    {
        var result = JsonDocument.Parse(await _tool.ExecuteAsync(
            JsonDocument.Parse("""{"productName":"Dell","includeSerials":"true","belowQuantity":"5"}""").RootElement,
            CancellationToken.None)).RootElement;

        var rows = Rows(result);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.True(r.GetProperty("quantityOnHand").GetInt32() < 5));
        Assert.NotEqual(JsonValueKind.Null, rows[0].GetProperty("serials").ValueKind);
    }

    // --- helpers ---

    private async Task<JsonElement> Run(object arguments)
        => JsonDocument.Parse(await _tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(arguments), CancellationToken.None)).RootElement;

    private static List<JsonElement> Rows(JsonElement result)
        => result.TryGetProperty("rows", out var rows) ? [.. rows.EnumerateArray()] : [];

    private static List<string> Serials(JsonElement row)
        => [.. row.GetProperty("serials").GetProperty("sample").EnumerateArray().Select(s => s.GetString()!)];

    private async Task SeedAsync()
    {
        var company = new Company { Code = "APC", Name = "Ai Platform Company", CreatedBy = UserId };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
        _companyId = company.CompanyId;

        _hoId = await AddLocation("HO", "Head Office");
        _cbId = await AddLocation("CB", "Chittagong Branch");

        var dellHo = await AddProduct(_hoId, "Dell XPS 13", serialized: true);
        var dellCb = await AddProduct(_cbId, "Dell XPS 13", serialized: true);
        var mouseHo = await AddProduct(_hoId, "Logitech M170 Mouse", serialized: false);
        var probookCb = await AddProduct(_cbId, "HP ProBook 450", serialized: true);

        await Purchase(_hoId, (dellHo, 2, 120000m, ["HO-1", "HO-2"]), (mouseHo, 50, 800m, null));
        await Purchase(_cbId, (dellCb, 1, 120000m, ["CB-1"]), (probookCb, 1, 90000m, ["PB-1"]));

        // ProBook পুরোটা বিক্রি → শূন্য-stock সারি (row থাকে, quantity 0)
        var stock = new ErpStockService(_db);
        await stock.ApproveSalesAsync(
            await AddSale(_cbId, new DateOnly(2026, 6, 1), (probookCb, 1, 110000m), ["PB-1"]), UserId);
    }

    private async Task<long> AddLocation(string code, string name)
    {
        var location = new Location { Code = code, Name = name, CompanyId = _companyId, CreatedBy = UserId };
        _db.Locations.Add(location);
        await _db.SaveChangesAsync();
        return location.LocationId;
    }

    private async Task<Product> AddProduct(long locationId, string name, bool serialized)
    {
        var product = new Product
        {
            Name = name,
            Category = serialized ? "Laptop" : "Accessory",
            Brand = "Test",
            IsSerialized = serialized,
            LocationId = locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return product;
    }

    private async Task Purchase(
        long locationId, params (Product Product, int Quantity, decimal UnitPrice, string[]? Serials)[] lines)
    {
        var order = new PurchaseOrder
        {
            PurchaseOrderNo = $"PO-APC-{locationId}-2026-{_db.PurchaseOrders.Count() + 1:D6}",
            SupplierId = await EnsureSupplier(locationId),
            OrderDate = new DateOnly(2026, 1, 5),
            LocationId = locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };

        foreach (var (product, quantity, unitPrice, serials) in lines)
        {
            var detail = new PurchaseOrderDetail
            {
                ProductId = product.ProductId,
                Quantity = quantity,
                UnitPrice = unitPrice,
                TotalAmount = quantity * unitPrice,
                CreatedBy = UserId
            };
            foreach (var serialNo in serials ?? [])
                detail.Serials.Add(new PurchaseOrderSerial { ProductId = product.ProductId, SerialNo = serialNo, CreatedBy = UserId });

            order.Details.Add(detail);
        }

        order.TotalAmount = order.Details.Sum(d => d.TotalAmount);
        _db.PurchaseOrders.Add(order);
        await _db.SaveChangesAsync();

        await new ErpStockService(_db).ApprovePurchaseAsync(order.PurchaseOrderId, UserId);
    }

    private async Task<long> AddSale(
        long locationId, DateOnly date,
        (Product Product, int Quantity, decimal UnitPrice) line, string[]? serials = null)
    {
        var detail = new SalesOrderDetail
        {
            ProductId = line.Product.ProductId,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            TotalAmount = line.Quantity * line.UnitPrice,
            CreatedBy = UserId
        };
        foreach (var serialNo in serials ?? [])
            detail.Serials.Add(new SalesOrderSerial { ProductId = line.Product.ProductId, SerialNo = serialNo, CreatedBy = UserId });

        var order = new SalesOrder
        {
            SalesOrderNo = $"SO-APC-{locationId}-2026-{_db.SalesOrders.Count() + 1:D6}",
            CustomerId = await EnsureCustomer(locationId),
            OrderDate = date,
            LocationId = locationId,
            CompanyId = _companyId,
            TotalAmount = detail.TotalAmount,
            CreatedBy = UserId
        };
        order.Details.Add(detail);

        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return order.SalesOrderId;
    }

    private async Task<long> EnsureSupplier(long locationId)
    {
        var existing = await _db.Suppliers.FirstOrDefaultAsync(s => s.LocationId == locationId);
        if (existing is not null)
            return existing.SupplierId;

        var supplier = new Supplier
        {
            Code = $"SUP-{locationId}",
            Name = "Star Tech",
            LocationId = locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();
        return supplier.SupplierId;
    }

    private async Task<long> EnsureCustomer(long locationId)
    {
        var existing = await _db.Customers.FirstOrDefaultAsync(c => c.LocationId == locationId);
        if (existing is not null)
            return existing.CustomerId;

        var customer = new Customer
        {
            Code = $"CUS-{locationId}",
            Name = "Karim Traders",
            LocationId = locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer.CustomerId;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
