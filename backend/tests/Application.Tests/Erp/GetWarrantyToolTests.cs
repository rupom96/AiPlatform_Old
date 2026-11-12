using System.Text.Json;
using AiPlatform.Domain.Entities.Erp;
using AiPlatform.Infrastructure.Agent;
using AiPlatform.Infrastructure.Erp;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AiPlatform.Application.Tests.Erp;

// মেয়াদের হিসাব এখানে হয় বলেই LLM-কে date arithmetic করতে হয় না — তাই এই হিসাবটাই
// পাহারা দেওয়া সবচেয়ে জরুরি: expiry ঠিক তারিখে, expired flag ঠিক দিকে, আজীবন কখনো expired নয়।
public class GetWarrantyToolTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ErpDbContext _db;
    private readonly GetWarrantyTool _tool;
    private readonly ErpStockService _stock;

    private const long UserId = 1;
    private readonly long _companyId;
    private readonly long _locationId;

    public GetWarrantyToolTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ErpDbContext(options);
        _db.Database.EnsureCreated();
        _tool = new GetWarrantyTool(_db);
        _stock = new ErpStockService(_db);

        var company = new Company { Code = "APC", Name = "Test Co", CreatedBy = UserId };
        _db.Companies.Add(company);
        _db.SaveChanges();
        _companyId = company.CompanyId;

        var location = new Location { Code = "HO", Name = "Head Office", CompanyId = _companyId, CreatedBy = UserId };
        _db.Locations.Add(location);
        _db.SaveChanges();
        _locationId = location.LocationId;

        var customer = new Customer
        {
            Code = "CUS-1",
            Name = "Karim",
            CompanyId = _companyId,
            LocationId = _locationId,
            CreatedBy = UserId
        };
        _db.Customers.Add(customer);
        _db.SaveChanges();
        _customerId = customer.CustomerId;
    }

    private readonly long _customerId;

    [Fact]
    public async Task Serial_ReportsExpiryFromSaleDate()
    {
        var laptop = AddProduct("Dell XPS 13", serialized: true);
        AddWarranty(laptop, WarrantyType.Service, months: 24);
        AddWarranty(laptop, WarrantyType.Parts, months: 12);

        // ১৪ মাস আগে বিক্রি → Service (২৪ মাস) চালু, Parts (১২ মাস) শেষ।
        var soldOn = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-14);
        await SellAsync(laptop, soldOn, "SN-001");

        var result = await Execute(new { serialNo = "SN-001" });

        Assert.True(result.GetProperty("found").GetBoolean());
        var warranties = result.GetProperty("items")[0].GetProperty("warranties");

        var service = Single(warranties, "Service");
        Assert.False(service.GetProperty("isExpired").GetBoolean());
        Assert.Equal(soldOn.AddMonths(24).ToString("yyyy-MM-dd"), service.GetProperty("expiresOn").GetString());

        var parts = Single(warranties, "Parts");
        Assert.True(parts.GetProperty("isExpired").GetBoolean());
        Assert.Equal(0, parts.GetProperty("daysRemaining").GetInt32());
    }

    [Fact]
    public async Task Lifetime_NeverExpires()
    {
        var usb = AddProduct("SanDisk USB 32GB", serialized: true);
        AddWarranty(usb, WarrantyType.Lifetime, months: null);

        await SellAsync(usb, DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-60), "SN-USB");

        var result = await Execute(new { serialNo = "SN-USB" });
        var lifetime = Single(result.GetProperty("items")[0].GetProperty("warranties"), "Lifetime");

        Assert.True(lifetime.GetProperty("lifetime").GetBoolean());
        Assert.False(lifetime.GetProperty("isExpired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, lifetime.GetProperty("expiresOn").ValueKind);
    }

    [Fact]
    public async Task Invoice_CoversNonSerializedLinesToo()
    {
        var mouse = AddProduct("Logitech M170 Mouse", serialized: false);
        AddWarranty(mouse, WarrantyType.Replacement, months: 6);

        var invoiceNo = await SellAsync(mouse, DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1), serialNo: null);

        var result = await Execute(new { invoiceNo });

        Assert.True(result.GetProperty("found").GetBoolean());
        var item = result.GetProperty("items")[0];
        Assert.Equal("Logitech M170 Mouse", item.GetProperty("product").GetString());
        Assert.True(item.GetProperty("hasWarranty").GetBoolean());
        Assert.False(Single(item.GetProperty("warranties"), "Replacement").GetProperty("isExpired").GetBoolean());
    }

    [Fact]
    public async Task SerialStillInStock_SaysWarrantyNotStarted()
    {
        var laptop = AddProduct("HP ProBook 450", serialized: true);
        AddWarranty(laptop, WarrantyType.Service, months: 12);
        await PurchaseAsync(laptop, "SN-STOCK");   // কেনা হয়েছে, বিক্রি হয়নি

        var result = await Execute(new { serialNo = "SN-STOCK" });

        Assert.False(result.GetProperty("found").GetBoolean());
        Assert.Contains("still in stock", result.GetProperty("note").GetString());
    }

    [Fact]
    public async Task ProductWithoutWarranty_SaysNoneRatherThanNotFound()
    {
        AddProduct("HDMI Cable 2m", serialized: false);

        var result = await Execute(new { productName = "HDMI" });

        Assert.False(result.GetProperty("found").GetBoolean());
        Assert.Contains("no warranty", result.GetProperty("note").GetString());
    }

    [Fact]
    public async Task NoArguments_ReturnsError()
    {
        var result = await Execute(new { });

        Assert.Contains("serialNo", result.GetProperty("error").GetString());
    }

    private async Task<JsonElement> Execute(object arguments)
    {
        var json = JsonSerializer.SerializeToElement(arguments);
        return JsonDocument.Parse(await _tool.ExecuteAsync(json, CancellationToken.None)).RootElement;
    }

    private static JsonElement Single(JsonElement warranties, string type)
        => warranties.EnumerateArray().Single(w => w.GetProperty("type").GetString() == type);

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

    private void AddWarranty(Product product, WarrantyType type, int? months)
    {
        _db.ProductWarranties.Add(new ProductWarranty
        {
            ProductId = product.ProductId,
            WarrantyType = type,
            DurationMonths = months,
            CreatedBy = UserId
        });
        _db.SaveChanges();
    }

    // stock না থাকলে ApproveSales throw করত — তাই আগে কিনে নিই।
    private async Task PurchaseAsync(Product product, string? serialNo)
    {
        var order = new PurchaseOrder
        {
            PurchaseOrderNo = $"PO-{Guid.NewGuid():N}"[..20],
            SupplierId = EnsureSupplier(),
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-6),
            LocationId = _locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };

        var detail = new PurchaseOrderDetail
        {
            ProductId = product.ProductId,
            Quantity = 1,
            UnitPrice = 100m,
            TotalAmount = 100m,
            CreatedBy = UserId
        };

        if (serialNo is not null)
            detail.Serials.Add(new PurchaseOrderSerial { ProductId = product.ProductId, SerialNo = serialNo, CreatedBy = UserId });

        order.Details.Add(detail);
        order.TotalAmount = 100m;
        _db.PurchaseOrders.Add(order);
        await _db.SaveChangesAsync();

        await _stock.ApprovePurchaseAsync(order.PurchaseOrderId, UserId);
    }

    private async Task<string> SellAsync(Product product, DateOnly soldOn, string? serialNo)
    {
        await PurchaseAsync(product, serialNo);

        var order = new SalesOrder
        {
            SalesOrderNo = $"SO-{Guid.NewGuid():N}"[..20],
            CustomerId = _customerId,
            OrderDate = soldOn,
            LocationId = _locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };

        var detail = new SalesOrderDetail
        {
            ProductId = product.ProductId,
            Quantity = 1,
            UnitPrice = 150m,
            TotalAmount = 150m,
            CreatedBy = UserId
        };

        if (serialNo is not null)
            detail.Serials.Add(new SalesOrderSerial { ProductId = product.ProductId, SerialNo = serialNo, CreatedBy = UserId });

        order.Details.Add(detail);
        order.TotalAmount = 150m;
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        await _stock.ApproveSalesAsync(order.SalesOrderId, UserId);

        return await _db.SalesOrders
            .Where(o => o.SalesOrderId == order.SalesOrderId)
            .Select(o => o.InvoiceNo!)
            .SingleAsync();
    }

    private long EnsureSupplier()
    {
        var existing = _db.Suppliers.FirstOrDefault();
        if (existing is not null)
            return existing.SupplierId;

        var supplier = new Supplier { Code = "SUP-1", Name = "Star Tech", CompanyId = _companyId, LocationId = _locationId, CreatedBy = UserId };
        _db.Suppliers.Add(supplier);
        _db.SaveChanges();
        return supplier.SupplierId;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
