using AiPlatform.Domain.Entities.Erp;
using AiPlatform.Infrastructure.Erp;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AiPlatform.Application.Tests.Erp;

// stock movement-ই ERP-র মূল যুক্তি — ভুল হলে AI আত্মবিশ্বাসের সাথে ভুল সংখ্যা বলবে।
// in-memory SQLite: আসল relational semantics (FK, unique index) কিন্তু Docker লাগে না।
public class ErpStockServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ErpDbContext _db;
    private readonly ErpStockService _stock;

    private const long UserId = 1;
    private long _companyId;
    private long _locationId;

    public ErpStockServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlite(_connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)   // production DI-র মতোই
            .Options;

        _db = new ErpDbContext(options);
        _db.Database.EnsureCreated();
        _stock = new ErpStockService(_db);

        SeedTenant();
    }

    [Fact]
    public async Task ApprovePurchase_AddsLedgerRowAndBalance()
    {
        var product = AddProduct("Logitech M170 Mouse", serialized: false);
        var order = AddPurchase(product, quantity: 10, unitPrice: 850m);

        await _stock.ApprovePurchaseAsync(order.PurchaseOrderId, UserId);

        Assert.Equal(10, await BalanceOf(product));
        var ledger = await _db.CurrentStocks.SingleAsync();
        Assert.Equal(10, ledger.Quantity);
        Assert.Equal(850m, ledger.Cost);
        Assert.Equal(order.PurchaseOrderNo, ledger.ReferenceNo);
    }

    [Fact]
    public async Task ApprovePurchase_IsIdempotent()
    {
        var product = AddProduct("HDMI Cable 2m", serialized: false);
        var order = AddPurchase(product, quantity: 5, unitPrice: 450m);

        await _stock.ApprovePurchaseAsync(order.PurchaseOrderId, UserId);
        await _stock.ApprovePurchaseAsync(order.PurchaseOrderId, UserId);   // দ্বিতীয়বার কিছুই করবে না

        Assert.Equal(5, await BalanceOf(product));
        Assert.Equal(1, await _db.CurrentStocks.CountAsync());
    }

    [Fact]
    public async Task ApprovePurchase_SerializedProduct_PutsEachSerialInStock()
    {
        var product = AddProduct("Dell XPS 13", serialized: true);
        var order = AddPurchase(product, quantity: 3, unitPrice: 135000m, serials: ["SN-1", "SN-2", "SN-3"]);

        await _stock.ApprovePurchaseAsync(order.PurchaseOrderId, UserId);

        var inStock = await _db.CurrentStockDetails.Select(d => d.SerialNo).ToListAsync();
        Assert.Equal(["SN-1", "SN-2", "SN-3"], inStock.Order());
    }

    [Fact]
    public async Task ApprovePurchase_SerializedProductWithMissingSerials_Throws()
    {
        var product = AddProduct("Dell XPS 13", serialized: true);
        var order = AddPurchase(product, quantity: 3, unitPrice: 135000m, serials: ["SN-1"]);   // ৩টার বদলে ১টা

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _stock.ApprovePurchaseAsync(order.PurchaseOrderId, UserId));
    }

    [Fact]
    public async Task ApproveSales_ReducesBalanceAndAssignsInvoiceNo()
    {
        var product = AddProduct("Logitech M170 Mouse", serialized: false);
        await _stock.ApprovePurchaseAsync(AddPurchase(product, 10, 850m).PurchaseOrderId, UserId);

        var sale = AddSale(product, quantity: 4, unitPrice: 1000m);
        await _stock.ApproveSalesAsync(sale.SalesOrderId, UserId);

        Assert.Equal(6, await BalanceOf(product));

        var approved = await _db.SalesOrders.SingleAsync(s => s.SalesOrderId == sale.SalesOrderId);
        Assert.Equal("INV-APC-HO-2026-000001", approved.InvoiceNo);
        Assert.True(approved.Approved);
    }

    // Cost = কেনার খরচ, বিক্রয়মূল্য নয় — নইলে গুদাম খালি হয়েও stock value negative দেখাত।
    [Fact]
    public async Task ApproveSales_RecordsPurchaseCostNotSalesPrice()
    {
        var product = AddProduct("Logitech M170 Mouse", serialized: false);
        await _stock.ApprovePurchaseAsync(AddPurchase(product, 10, 850m).PurchaseOrderId, UserId);

        var sale = AddSale(product, quantity: 10, unitPrice: 1000m);   // বিক্রি ১০০০, কেনা ৮৫০
        await _stock.ApproveSalesAsync(sale.SalesOrderId, UserId);

        var salesLedger = await _db.CurrentStocks.SingleAsync(s => s.Quantity < 0);
        Assert.Equal(850m, salesLedger.Cost);

        // সব বিক্রি হয়ে গেছে → গুদামের মূল্য ঠিক শূন্য
        var stockValue = await _db.CurrentStocks.SumAsync(s => s.Quantity * s.Cost);
        Assert.Equal(0m, stockValue);
    }

    [Fact]
    public async Task ApproveSales_SerializedProduct_RemovesSoldSerialsOnly()
    {
        var product = AddProduct("Dell XPS 13", serialized: true);
        await _stock.ApprovePurchaseAsync(
            AddPurchase(product, 3, 135000m, ["SN-1", "SN-2", "SN-3"]).PurchaseOrderId, UserId);

        var sale = AddSale(product, quantity: 2, unitPrice: 160000m, serials: ["SN-1", "SN-3"]);
        await _stock.ApproveSalesAsync(sale.SalesOrderId, UserId);

        var remaining = await _db.CurrentStockDetails.Select(d => d.SerialNo).ToListAsync();
        Assert.Equal(["SN-2"], remaining);
        Assert.Equal(1, await BalanceOf(product));
    }

    [Fact]
    public async Task ApproveSales_WithoutEnoughStock_ThrowsAndKeepsOrderUnapproved()
    {
        var product = AddProduct("HDMI Cable 2m", serialized: false);
        await _stock.ApprovePurchaseAsync(AddPurchase(product, 2, 450m).PurchaseOrderId, UserId);

        var sale = AddSale(product, quantity: 5, unitPrice: 600m);   // আছে ২, চাওয়া হচ্ছে ৫

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _stock.ApproveSalesAsync(sale.SalesOrderId, UserId));

        Assert.Equal(2, await BalanceOf(product));
        Assert.False(await _db.SalesOrders.AnyAsync(s => s.SalesOrderId == sale.SalesOrderId && s.Approved));
    }

    [Fact]
    public async Task ApproveSales_UnknownSerial_Throws()
    {
        var product = AddProduct("Dell XPS 13", serialized: true);
        await _stock.ApprovePurchaseAsync(AddPurchase(product, 2, 135000m, ["SN-1", "SN-2"]).PurchaseOrderId, UserId);

        var sale = AddSale(product, quantity: 1, unitPrice: 160000m, serials: ["SN-999"]);   // গুদামে নেই

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _stock.ApproveSalesAsync(sale.SalesOrderId, UserId));
    }

    // এক order-এ একই product দুই line-এ থাকলে balance দুটোই গুনতে হবে (local-cache bug ধরার জন্য)।
    [Fact]
    public async Task ApprovePurchase_SameProductTwice_SumsBothLines()
    {
        var product = AddProduct("SanDisk USB 32GB", serialized: false);

        var order = NewPurchase();
        order.Details.Add(NewPurchaseDetail(product, 10, 750m));
        order.Details.Add(NewPurchaseDetail(product, 15, 750m));
        _db.PurchaseOrders.Add(order);
        _db.SaveChanges();

        await _stock.ApprovePurchaseAsync(order.PurchaseOrderId, UserId);

        Assert.Equal(25, await BalanceOf(product));
    }

    // ledger আর summary সবসময় একই কথা বলবে — এটাই ভাঙলে সব রিপোর্ট মিথ্যা।
    [Fact]
    public async Task LedgerAndBalance_StayInSync()
    {
        var product = AddProduct("Logitech K120 Keyboard", serialized: false);
        await _stock.ApprovePurchaseAsync(AddPurchase(product, 20, 1200m).PurchaseOrderId, UserId);
        await _stock.ApproveSalesAsync(AddSale(product, 7, 1500m).SalesOrderId, UserId);
        await _stock.ApprovePurchaseAsync(AddPurchase(product, 5, 1250m).PurchaseOrderId, UserId);

        var ledgerTotal = await _db.CurrentStocks
            .Where(s => s.ProductId == product.ProductId)
            .SumAsync(s => s.Quantity);

        Assert.Equal(ledgerTotal, await BalanceOf(product));
        Assert.Equal(18, ledgerTotal);
    }

    // --- helpers ---

    private void SeedTenant()
    {
        var company = new Company { Code = "APC", Name = "Ai Platform Company", CreatedBy = UserId };
        _db.Companies.Add(company);
        _db.SaveChanges();
        _companyId = company.CompanyId;

        var location = new Location { Code = "HO", Name = "Head Office", CompanyId = _companyId, CreatedBy = UserId };
        _db.Locations.Add(location);
        _db.SaveChanges();
        _locationId = location.LocationId;
    }

    private Product AddProduct(string name, bool serialized)
    {
        var product = new Product
        {
            Name = name,
            Category = serialized ? "Laptop" : "Accessory",
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

    private PurchaseOrder NewPurchase() => new()
    {
        PurchaseOrderNo = $"PO-APC-HO-2026-{_db.PurchaseOrders.Count() + 1:D6}",
        SupplierId = EnsureSupplier(),
        OrderDate = new DateOnly(2026, 7, 1),
        LocationId = _locationId,
        CompanyId = _companyId,
        CreatedBy = UserId
    };

    private static PurchaseOrderDetail NewPurchaseDetail(Product product, int quantity, decimal unitPrice) => new()
    {
        ProductId = product.ProductId,
        Quantity = quantity,
        UnitPrice = unitPrice,
        TotalAmount = quantity * unitPrice,
        CreatedBy = UserId
    };

    private PurchaseOrder AddPurchase(Product product, int quantity, decimal unitPrice, string[]? serials = null)
    {
        var order = NewPurchase();
        var detail = NewPurchaseDetail(product, quantity, unitPrice);

        foreach (var serialNo in serials ?? [])
            detail.Serials.Add(new PurchaseOrderSerial { ProductId = product.ProductId, SerialNo = serialNo, CreatedBy = UserId });

        order.Details.Add(detail);
        order.TotalAmount = detail.TotalAmount;
        _db.PurchaseOrders.Add(order);
        _db.SaveChanges();
        return order;
    }

    private SalesOrder AddSale(Product product, int quantity, decimal unitPrice, string[]? serials = null)
    {
        var detail = new SalesOrderDetail
        {
            ProductId = product.ProductId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TotalAmount = quantity * unitPrice,
            CreatedBy = UserId
        };

        foreach (var serialNo in serials ?? [])
            detail.Serials.Add(new SalesOrderSerial { ProductId = product.ProductId, SerialNo = serialNo, CreatedBy = UserId });

        var order = new SalesOrder
        {
            SalesOrderNo = $"SO-APC-HO-2026-{_db.SalesOrders.Count() + 1:D6}",
            CustomerId = EnsureCustomer(),
            OrderDate = new DateOnly(2026, 7, 15),
            LocationId = _locationId,
            CompanyId = _companyId,
            TotalAmount = detail.TotalAmount,
            CreatedBy = UserId
        };
        order.Details.Add(detail);

        _db.SalesOrders.Add(order);
        _db.SaveChanges();
        return order;
    }

    private long EnsureSupplier()
    {
        var existing = _db.Suppliers.FirstOrDefault();
        if (existing is not null)
            return existing.SupplierId;

        var supplier = new Supplier
        {
            Code = "SUP-APC-HO-2026-000001",
            Name = "Star Tech",
            LocationId = _locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };
        _db.Suppliers.Add(supplier);
        _db.SaveChanges();
        return supplier.SupplierId;
    }

    private long EnsureCustomer()
    {
        var existing = _db.Customers.FirstOrDefault();
        if (existing is not null)
            return existing.CustomerId;

        var customer = new Customer
        {
            Code = "CUS-APC-HO-2026-000001",
            Name = "Karim Traders",
            LocationId = _locationId,
            CompanyId = _companyId,
            CreatedBy = UserId
        };
        _db.Customers.Add(customer);
        _db.SaveChanges();
        return customer.CustomerId;
    }

    private async Task<int> BalanceOf(Product product)
    {
        var balance = await _db.StockBalances
            .FirstOrDefaultAsync(b => b.ProductId == product.ProductId && b.LocationId == _locationId);
        return balance?.QuantityOnHand ?? 0;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
