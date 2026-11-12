using System.Text.Json;
using AiPlatform.Domain.Entities.Erp;
using AiPlatform.Infrastructure.Agent;
using AiPlatform.Infrastructure.Erp;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AiPlatform.Application.Tests.Erp;

// একমাত্র write tool — ভুল হলে DB-তে আবর্জনা বসে। তাই দুটো জিনিস পাহারা দেওয়া সবচেয়ে জরুরি:
// (১) খসড়াই থাকে, stock কখনো নড়ে না; (২) অস্পষ্ট বা অসম্ভব ইনপুটে row না বসিয়ে error ফেরে।
public class CreateSalesOrderToolTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ErpDbContext _db;
    private readonly CreateSalesOrderTool _tool;
    private readonly ErpStockService _stock;

    private const long UserId = 1;
    private readonly long _companyId;
    private readonly long _locationId;

    public CreateSalesOrderToolTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ErpDbContext(options);
        _db.Database.EnsureCreated();
        _tool = new CreateSalesOrderTool(_db);
        _stock = new ErpStockService(_db);

        var company = new Company { Code = "APC", Name = "Test Co", CreatedBy = UserId };
        _db.Companies.Add(company);
        _db.SaveChanges();
        _companyId = company.CompanyId;

        var location = new Location { Code = "HO", Name = "Head Office", CompanyId = _companyId, CreatedBy = UserId };
        _db.Locations.Add(location);
        _db.SaveChanges();
        _locationId = location.LocationId;

        _db.Customers.Add(new Customer
        {
            Code = "CUS-1", Name = "Karim Traders", CompanyId = _companyId, LocationId = _locationId, CreatedBy = UserId
        });
        _db.Suppliers.Add(new Supplier
        {
            Code = "SUP-1", Name = "Star Tech", CompanyId = _companyId, LocationId = _locationId, CreatedBy = UserId
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task CreatesDraft_WithoutTouchingStock()
    {
        var mouse = AddProduct("Logitech M170 Mouse", serialized: false);
        await StockUpAsync(mouse, quantity: 20);
        var before = await BalanceOf(mouse);

        var result = await Execute(Order(("Logitech", 5, 1000m)));

        Assert.True(result.GetProperty("created").GetBoolean());
        Assert.False(result.GetProperty("approved").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("invoiceNo").ValueKind);
        // অঙ্ক সাজানো string — LLM যেন নিজে গোল করে ভুল না লেখে (ToolFormat)।
        Assert.Equal("5,000 BDT", result.GetProperty("totalAmount").GetString());

        // মূল guardrail: খসড়া stock ছোঁয় না।
        Assert.Equal(before, await BalanceOf(mouse));
        var order = await _db.SalesOrders.SingleAsync();
        Assert.False(order.Approved);
        Assert.Null(order.InvoiceNo);
    }

    [Fact]
    public async Task SerializedProduct_GetsRealSerialsFromStock_SoApprovalSucceeds()
    {
        var laptop = AddProduct("Dell XPS 13", serialized: true);
        await StockUpAsync(laptop, quantity: 3, serialPrefix: "DELL-X");

        var result = await Execute(Order(("Dell", 2, 150000m)));

        var serials = result.GetProperty("lines")[0].GetProperty("serials").EnumerateArray()
            .Select(s => s.GetString()).ToList();
        Assert.Equal(2, serials.Count);

        // খসড়া সত্যিই approve-যোগ্য কি না — কাল্পনিক serial হলে এখানে throw করত।
        var order = await _db.SalesOrders.SingleAsync();
        await _stock.ApproveSalesAsync(order.SalesOrderId, UserId);
        Assert.Equal(1, await BalanceOf(laptop));
    }

    [Fact]
    public async Task InsufficientStock_RefusesInsteadOfCreatingUnapprovableDraft()
    {
        var mouse = AddProduct("Logitech M170 Mouse", serialized: false);
        await StockUpAsync(mouse, quantity: 3);

        var result = await Execute(Order(("Logitech", 10, 1000m)));

        Assert.False(result.GetProperty("created").GetBoolean());
        Assert.Contains("Insufficient stock", result.GetProperty("error").GetString());
        Assert.Empty(await _db.SalesOrders.ToListAsync());
    }

    [Fact]
    public async Task AmbiguousProduct_AsksInsteadOfPicking()
    {
        var a = AddProduct("Logitech M170 Mouse", serialized: false);
        var b = AddProduct("Logitech K120 Keyboard", serialized: false);
        await StockUpAsync(a, quantity: 10);
        await StockUpAsync(b, quantity: 10);

        var result = await Execute(Order(("Logitech", 1, 1000m)));

        Assert.False(result.GetProperty("created").GetBoolean());
        Assert.Contains("Ask the user", result.GetProperty("error").GetString());
        Assert.Empty(await _db.SalesOrders.ToListAsync());
    }

    [Fact]
    public async Task MissingUnitPrice_RefusesRatherThanGuessing()
    {
        var mouse = AddProduct("Logitech M170 Mouse", serialized: false);
        await StockUpAsync(mouse, quantity: 10);

        var result = await Execute(JsonSerializer.SerializeToElement(new
        {
            customerName = "Karim",
            location = "HO",
            items = new[] { new { productName = "Logitech", quantity = 2 } }
        }));

        Assert.False(result.GetProperty("created").GetBoolean());
        Assert.Contains("unitPrice", result.GetProperty("error").GetString());
        Assert.Empty(await _db.SalesOrders.ToListAsync());
    }

    [Fact]
    public async Task UnknownCustomer_Refuses()
    {
        var mouse = AddProduct("Logitech M170 Mouse", serialized: false);
        await StockUpAsync(mouse, quantity: 10);

        var result = await Execute(Order(("Logitech", 1, 1000m), customerName: "Nobody Ltd"));

        Assert.False(result.GetProperty("created").GetBoolean());
        Assert.Contains("No customer matched", result.GetProperty("error").GetString());
        Assert.Empty(await _db.SalesOrders.ToListAsync());
    }

    private static JsonElement Order(
        (string Product, int Quantity, decimal Price) line, string customerName = "Karim", string location = "HO")
        => JsonSerializer.SerializeToElement(new
        {
            customerName,
            location,
            items = new[] { new { productName = line.Product, quantity = line.Quantity, unitPrice = line.Price } }
        });

    private async Task<JsonElement> Execute(JsonElement arguments)
        => JsonDocument.Parse(await _tool.ExecuteAsync(arguments, CancellationToken.None)).RootElement;

    private Product AddProduct(string name, bool serialized)
    {
        var product = new Product
        {
            Name = name,
            Category = "Test",
            Brand = "Test",
            IsSerialized = serialized,
            LocationId = _locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };
        _db.Products.Add(product);
        _db.SaveChanges();
        return product;
    }

    // stock হাতে না লিখে আসল approval পথেই ঢোকাই — ledger/summary/serial সামঞ্জস্যপূর্ণ থাকে।
    private async Task StockUpAsync(Product product, int quantity, string? serialPrefix = null)
    {
        var supplierId = await _db.Suppliers.Select(s => s.SupplierId).FirstAsync();
        var order = new PurchaseOrder
        {
            PurchaseOrderNo = $"PO-{Guid.NewGuid():N}"[..20],
            SupplierId = supplierId,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1),
            LocationId = _locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };

        var detail = new PurchaseOrderDetail
        {
            ProductId = product.ProductId,
            Quantity = quantity,
            UnitPrice = 100m,
            TotalAmount = quantity * 100m,
            CreatedBy = UserId
        };

        if (product.IsSerialized)
        {
            for (var unit = 1; unit <= quantity; unit++)
            {
                detail.Serials.Add(new PurchaseOrderSerial
                {
                    ProductId = product.ProductId,
                    SerialNo = $"{serialPrefix}-{unit:D2}",
                    CreatedBy = UserId
                });
            }
        }

        order.Details.Add(detail);
        order.TotalAmount = detail.TotalAmount;
        _db.PurchaseOrders.Add(order);
        await _db.SaveChangesAsync();

        await _stock.ApprovePurchaseAsync(order.PurchaseOrderId, UserId);
    }

    private async Task<int> BalanceOf(Product product)
        => await _db.StockBalances
            .Where(b => b.ProductId == product.ProductId)
            .Select(b => b.QuantityOnHand)
            .SingleAsync();

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
