using System.Text.Json;
using AiPlatform.Domain.Entities.Erp;
using AiPlatform.Infrastructure.Agent;
using AiPlatform.Infrastructure.Erp;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AiPlatform.Application.Tests.Erp;

// tool ভুল সংখ্যা ফেরালে AI সেটাই আত্মবিশ্বাসের সাথে বলবে — ধরার একমাত্র জায়গা এখানে।
// data ErpStockService দিয়ে ঢোকানো, তাই hand-written row নয়, আসল approval path।
public class GetSalesToolTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ErpDbContext _db;
    private readonly GetSalesTool _tool;

    private const long UserId = 1;
    private long _companyId;
    private long _hoId;
    private long _cbId;

    public GetSalesToolTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlite(_connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
        _db.Database.EnsureCreated();
        _tool = new GetSalesTool(_db);

        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GrandTotal_SumsOnlyApprovedSalesInRange()
    {
        var result = await Run(new { fromDate = "2026-05-01", toDate = "2026-05-31" });

        // মে: করিম ২টা Dell @150000 + রহিম ৫টা মাউস @1000 = 300000 + 5000
        Assert.Equal("305,000 BDT", result.GetProperty("totalAmountAllRows").GetString());
    }

    [Fact]
    public async Task OutsideRange_IsExcluded()
    {
        var may = await Run(new { fromDate = "2026-05-01", toDate = "2026-05-31" });
        var june = await Run(new { fromDate = "2026-06-01", toDate = "2026-06-30" });

        Assert.NotEqual(
            may.GetProperty("totalAmountAllRows").GetString(),
            june.GetProperty("totalAmountAllRows").GetString());
    }

    // "May masher sell dekhao customer wise"
    [Fact]
    public async Task GroupByCustomer_ReturnsPerCustomerRowsSortedByAmount()
    {
        var result = await Run(new { fromDate = "2026-05-01", toDate = "2026-05-31", groupBy = "customer" });

        var rows = result.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Karim Traders", rows[0].GetProperty("group").GetString());   // বেশি টাকা আগে
        Assert.Equal("300,000 BDT", rows[0].GetProperty("totalAmount").GetString());
        Assert.Equal("Rahim Enterprise", rows[1].GetProperty("group").GetString());
    }

    // "June masher top 3 ... dekhao"
    [Fact]
    public async Task TopN_LimitsToHighestSellingGroups()
    {
        var result = await Run(new { fromDate = "2026-06-01", toDate = "2026-06-30", groupBy = "product", topN = 1 });

        var rows = result.GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("Dell XPS 13", rows[0].GetProperty("group").GetString());
        Assert.True(result.GetProperty("truncated").GetBoolean());

        // দুটো যোগফল আলাদা হতেই হবে — এক নাম থাকায় LLM শীর্ষ-N-এর মোট বলে ভুল করেছিল (2026-07-22)।
        var returned = result.GetProperty("totalAmountReturnedRows").GetString();
        var all = result.GetProperty("totalAmountAllRows").GetString();
        Assert.Equal(rows[0].GetProperty("totalAmount").GetString(), returned);
        Assert.NotEqual(all, returned);   // all-rows total covers more than the truncated subset
    }

    // "x buyer + y product" — দুই filter একসাথে
    [Fact]
    public async Task CustomerAndProductFilters_CombineToNarrowResult()
    {
        var result = await Run(new
        {
            fromDate = "2026-01-01",
            toDate = "2026-12-31",
            customerName = "Karim",
            productName = "Dell"
        });

        // করিম Dell কিনেছে মে-তে ২টা + জুনে ৩টা = ৫টা
        Assert.Equal(5, result.GetProperty("rows")[0].GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task ProductFilter_SumsOnlyThatProductsLines_NotWholeOrder()
    {
        // জুনের order-এ Dell আর মাউস দুটোই আছে; মাউস filter দিলে শুধু মাউসের টাকা আসবে।
        var result = await Run(new { fromDate = "2026-06-01", toDate = "2026-06-30", productName = "Mouse" });

        Assert.Equal("2,000 BDT", result.GetProperty("totalAmountAllRows").GetString());
    }

    [Fact]
    public async Task LocationFilter_MatchesCodeOrName()
    {
        var byCode = await Run(new { fromDate = "2026-01-01", toDate = "2026-12-31", location = "CB" });
        var byName = await Run(new { fromDate = "2026-01-01", toDate = "2026-12-31", location = "Chittagong" });

        Assert.Equal(
            byCode.GetProperty("totalAmountAllRows").GetString(),
            byName.GetProperty("totalAmountAllRows").GetString());
        Assert.Equal("50,000 BDT", byCode.GetProperty("totalAmountAllRows").GetString());
    }

    [Fact]
    public async Task UnapprovedOrder_IsNeverCounted()
    {
        var product = await _db.Products.FirstAsync(p => p.LocationId == _hoId && p.Name == "Dell XPS 13");
        var draft = await AddSale(_hoId, "Karim Traders", new DateOnly(2026, 5, 20), (product, 10, 150000m));
        // ইচ্ছে করে approve করলাম না

        var result = await Run(new { fromDate = "2026-05-01", toDate = "2026-05-31" });

        Assert.Equal("305,000 BDT", result.GetProperty("totalAmountAllRows").GetString());   // অপরিবর্তিত
        Assert.False(await _db.SalesOrders.AnyAsync(s => s.SalesOrderId == draft && s.Approved));
    }

    [Fact]
    public async Task NoMatch_ReturnsZeroRowsWithNote()
    {
        var result = await Run(new { fromDate = "2026-05-01", toDate = "2026-05-31", customerName = "Nobody" });

        Assert.Equal(0, result.GetProperty("rowCount").GetInt32());
        Assert.True(result.TryGetProperty("note", out _));
    }

    // LLM ভুল argument দিলে throw নয় — error ফেরত, যাতে ও শুধরে আবার ডাকতে পারে।
    [Theory]
    [InlineData("{}")]                                                        // date নেই
    [InlineData("""{"fromDate":"গতকাল","toDate":"2026-05-31"}""")]            // পার্স হয় না
    [InlineData("""{"fromDate":"2026-05-31","toDate":"2026-05-01"}""")]       // উল্টো ক্রম
    [InlineData("""{"fromDate":"2026-05-01","toDate":"2026-05-31","groupBy":"colour"}""")]
    [InlineData("""{"fromDate":"2026-05-01","toDate":"2026-05-31","topN":0}""")]
    public async Task InvalidArguments_ReturnErrorInsteadOfThrowing(string argumentsJson)
    {
        var result = JsonDocument.Parse(
            await _tool.ExecuteAsync(JsonDocument.Parse(argumentsJson).RootElement, CancellationToken.None)).RootElement;

        Assert.True(result.TryGetProperty("error", out _));
    }

    // ছোট model মাঝে মাঝে সংখ্যা string হিসেবে পাঠায়।
    [Fact]
    public async Task TopN_AcceptsNumberSentAsString()
    {
        var result = JsonDocument.Parse(await _tool.ExecuteAsync(
            JsonDocument.Parse("""{"fromDate":"2026-06-01","toDate":"2026-06-30","groupBy":"product","topN":"1"}""").RootElement,
            CancellationToken.None)).RootElement;

        Assert.Single(result.GetProperty("rows").EnumerateArray());
    }

    // --- helpers ---

    private async Task<JsonElement> Run(object arguments)
    {
        var json = await _tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(arguments), CancellationToken.None);
        return JsonDocument.Parse(json).RootElement;
    }

    private async Task SeedAsync()
    {
        var company = new Company { Code = "APC", Name = "Ai Platform Company", CreatedBy = UserId };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
        _companyId = company.CompanyId;

        _hoId = await AddLocation("HO", "Head Office");
        _cbId = await AddLocation("CB", "Chittagong Branch");

        await AddCustomer(_hoId, "Karim Traders");
        await AddCustomer(_hoId, "Rahim Enterprise");
        await AddCustomer(_cbId, "Bengal IT Solutions");

        var dellHo = await AddProduct(_hoId, "Dell XPS 13", serialized: false);
        var mouseHo = await AddProduct(_hoId, "Logitech M170 Mouse", serialized: false);
        var dellCb = await AddProduct(_cbId, "Dell XPS 13", serialized: false);

        // stock ঢোকাই (নইলে approve "insufficient stock" দিয়ে ব্যর্থ হবে)
        await Purchase(_hoId, (dellHo, 50, 120000m), (mouseHo, 200, 800m));
        await Purchase(_cbId, (dellCb, 20, 120000m));

        var stock = new ErpStockService(_db);

        // মে — করিম ২টা Dell (300000), রহিম ৫টা মাউস (5000)
        await stock.ApproveSalesAsync(
            await AddSale(_hoId, "Karim Traders", new DateOnly(2026, 5, 10), (dellHo, 2, 150000m)), UserId);
        await stock.ApproveSalesAsync(
            await AddSale(_hoId, "Rahim Enterprise", new DateOnly(2026, 5, 22), (mouseHo, 5, 1000m)), UserId);

        // জুন — করিম এক order-এ ৩টা Dell + ২টা মাউস
        await stock.ApproveSalesAsync(
            await AddSale(_hoId, "Karim Traders", new DateOnly(2026, 6, 5), (dellHo, 3, 150000m), (mouseHo, 2, 1000m)), UserId);

        // চট্টগ্রাম — আলাদা branch (50000)
        await stock.ApproveSalesAsync(
            await AddSale(_cbId, "Bengal IT Solutions", new DateOnly(2026, 6, 18), (dellCb, 1, 50000m)), UserId);
    }

    private async Task<long> AddLocation(string code, string name)
    {
        var location = new Location { Code = code, Name = name, CompanyId = _companyId, CreatedBy = UserId };
        _db.Locations.Add(location);
        await _db.SaveChangesAsync();
        return location.LocationId;
    }

    private async Task AddCustomer(long locationId, string name)
    {
        _db.Customers.Add(new Customer
        {
            Code = $"CUS-{name.GetHashCode():X}",
            Name = name,
            LocationId = locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        });
        await _db.SaveChangesAsync();
    }

    private async Task<Product> AddProduct(long locationId, string name, bool serialized)
    {
        var product = new Product
        {
            Name = name,
            Category = "Test",
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

    private async Task Purchase(long locationId, params (Product Product, int Quantity, decimal UnitPrice)[] lines)
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

        foreach (var (product, quantity, unitPrice) in lines)
        {
            order.Details.Add(new PurchaseOrderDetail
            {
                ProductId = product.ProductId,
                Quantity = quantity,
                UnitPrice = unitPrice,
                TotalAmount = quantity * unitPrice,
                CreatedBy = UserId
            });
        }

        order.TotalAmount = order.Details.Sum(d => d.TotalAmount);
        _db.PurchaseOrders.Add(order);
        await _db.SaveChangesAsync();

        await new ErpStockService(_db).ApprovePurchaseAsync(order.PurchaseOrderId, UserId);
    }

    private async Task<long> AddSale(
        long locationId, string customerName, DateOnly date,
        params (Product Product, int Quantity, decimal UnitPrice)[] lines)
    {
        var customer = await _db.Customers.FirstAsync(c => c.Name == customerName);

        var order = new SalesOrder
        {
            SalesOrderNo = $"SO-APC-{locationId}-2026-{_db.SalesOrders.Count() + 1:D6}",
            CustomerId = customer.CustomerId,
            OrderDate = date,
            LocationId = locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };

        foreach (var (product, quantity, unitPrice) in lines)
        {
            order.Details.Add(new SalesOrderDetail
            {
                ProductId = product.ProductId,
                Quantity = quantity,
                UnitPrice = unitPrice,
                TotalAmount = quantity * unitPrice,
                CreatedBy = UserId
            });
        }

        order.TotalAmount = order.Details.Sum(d => d.TotalAmount);
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

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
