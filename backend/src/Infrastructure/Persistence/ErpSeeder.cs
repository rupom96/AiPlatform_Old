using AiPlatform.Domain.Entities.Erp;
using AiPlatform.Infrastructure.Erp;
using Microsoft.EntityFrameworkCore;

namespace AiPlatform.Infrastructure.Persistence;

// Sample ERP data — IT পণ্যের ব্যবসা, ২টা branch, ২০২৬-এর জানুয়ারি→জুলাই।
//
// দুটো নিয়ম মেনে চলে:
//  ১. Deterministic — fixed seed। বারবার চালালে একই সংখ্যা আসে, তাই AI-র উত্তর যাচাই করা যায়
//     ("জুলাইয়ের বিক্রি কত?" প্রতিবার এক উত্তর দেবে)।
//  ২. Stock নিজে হাতে লেখে না — ErpStockService.Approve* ডাকে। তাই ledger/summary/serial
//     সবসময় সামঞ্জস্যপূর্ণ, আসল approval-এ যা হতো ঠিক তা-ই।
public static class ErpSeeder
{
    private const int RandomSeed = 20260722;   // deterministic
    private const long SystemUserId = 1;       // seed data-র CreatedBy (Admin)

    private static readonly DateOnly SeedStart = new(2026, 1, 1);
    private const int SeedMonths = 7;          // জানুয়ারি → জুলাই ২০২৬

    private static readonly (string Code, string Name)[] Branches =
    [
        ("HO", "Head Office"),
        ("CB", "Chittagong Branch")
    ];

    private static readonly (string Name, string Category, string Brand, bool Serialized, decimal Cost)[] Catalog =
    [
        ("Dell XPS 13",            "Laptop",    "Dell",      true,  135000m),
        ("HP ProBook 450",         "Laptop",    "HP",        true,   92000m),
        ("Lenovo ThinkPad E14",    "Laptop",    "Lenovo",    true,   88000m),
        ("Desktop PC Core i5",     "Computer",  "Assembled", true,   65000m),
        ("Logitech M170 Mouse",    "Accessory", "Logitech",  false,    850m),
        ("Logitech K120 Keyboard", "Accessory", "Logitech",  false,   1200m),
        ("HDMI Cable 2m",          "Accessory", "Generic",   false,    450m),
        ("SanDisk USB 32GB",       "Accessory", "SanDisk",   false,    750m)
    ];

    // কোন পণ্যে কী warranty আসে — Catalog-এর নাম দিয়ে মেলে।
    // ইচ্ছাকৃত বৈচিত্র্য, যাতে AI-কে সব কেসে পরীক্ষা করা যায়:
    //  · এক পণ্যে একাধিক warranty (XPS: Service ২৪ + Parts ১২)
    //  · আজীবন (SanDisk — DurationMonths null)
    //  · warranty-ই নেই (HDMI Cable ইচ্ছে করে বাদ — "নেই" উত্তরটাও ঠিক আসতে হবে)
    private static readonly (string ProductName, WarrantyType Type, int? Months, string? Terms)[] WarrantyCatalog =
    [
        ("Dell XPS 13",            WarrantyType.Service,     24,   "physical ও liquid damage বাদ"),
        ("Dell XPS 13",            WarrantyType.Parts,       12,   null),
        ("HP ProBook 450",         WarrantyType.Service,     12,   "physical ও liquid damage বাদ"),
        ("HP ProBook 450",         WarrantyType.Parts,       12,   null),
        ("Lenovo ThinkPad E14",    WarrantyType.Service,     36,   "battery-তে মাত্র ৬ মাস"),
        ("Desktop PC Core i5",     WarrantyType.Parts,       12,   "assembled — motherboard ও RAM কভার হয়"),
        ("Desktop PC Core i5",     WarrantyType.Labour,       6,   null),
        ("Logitech M170 Mouse",    WarrantyType.Replacement,  6,   "seal অক্ষত থাকতে হবে"),
        ("Logitech K120 Keyboard", WarrantyType.Replacement, 12,   "seal অক্ষত থাকতে হবে"),
        ("SanDisk USB 32GB",       WarrantyType.Lifetime,    null, "manufacturer-এর আজীবন warranty")
    ];

    private static readonly string[] SupplierNames = ["Star Tech", "Ryans Computers"];
    private static readonly string[] CustomerNames = ["Karim Traders", "Rahim Enterprise", "Bengal IT Solutions"];

    // বিক্রয়মূল্য = কেনা দামের উপর এই হারে margin।
    private const decimal SalesMargin = 1.18m;

    public static async Task SeedAsync(ErpDbContext db, CancellationToken ct = default)
    {
        // idempotent — data থাকলে কিছুই করে না (প্রতি startup-এ আবার ঢুকবে না)।
        // ব্যতিক্রম warranty: table-টা পরে যোগ হয়েছে, তাই আগে seed হওয়া DB-তেও যেন বসে।
        if (await db.Companies.AnyAsync(ct))
        {
            await SeedProductWarrantiesAsync(db, ct);
            return;
        }

        var random = new Random(RandomSeed);
        var stock = new ErpStockService(db);

        var company = await SeedCompanyAsync(db, ct);
        var locations = await SeedLocationsAsync(db, company, ct);
        await SeedUsersAsync(db, company, locations, ct);

        foreach (var location in locations)
        {
            var products = await SeedProductsAsync(db, company, location, ct);
            var suppliers = await SeedSuppliersAsync(db, company, location, ct);
            var customers = await SeedCustomersAsync(db, company, location, ct);

            // মাসে মাসে: আগে কেনা (stock ঢোকে), তারপর বিক্রি (যা আছে তার মধ্যেই)।
            for (var month = 0; month < SeedMonths; month++)
            {
                var monthStart = SeedStart.AddMonths(month);

                for (var i = 0; i < 2; i++)
                    await SeedPurchaseAsync(db, stock, random, company, location, suppliers, products, monthStart, ct);

                for (var i = 0; i < 4; i++)
                    await SeedSaleAsync(db, stock, random, company, location, customers, products, monthStart, ct);
            }
        }

        // সব branch-এর product বসার পরে — একবারেই সবগুলোর warranty।
        await SeedProductWarrantiesAsync(db, ct);
    }

    // Warranty = product-এর offer (কোন unit বিক্রি হলো তার সাথে সম্পর্ক নেই)।
    // মেয়াদ কবে শেষ সেটা query-র সময় SalesOrder.InvoiceDate + DurationMonths দিয়ে বেরোয়।
    private static async Task SeedProductWarrantiesAsync(ErpDbContext db, CancellationToken ct)
    {
        if (await db.ProductWarranties.AnyAsync(ct))
            return;

        // ⚠️ Product location-wise আলাদা row — তাই একই নামের পণ্য প্রতি branch-এ নিজের warranty row পায়।
        var products = await db.Products
            .Select(p => new { p.ProductId, p.Name })
            .ToListAsync(ct);

        var warranties =
            from product in products
            join spec in WarrantyCatalog on product.Name equals spec.ProductName
            select new ProductWarranty
            {
                ProductId = product.ProductId,
                WarrantyType = spec.Type,
                DurationMonths = spec.Months,
                Terms = spec.Terms,
                CreatedBy = SystemUserId
            };

        db.ProductWarranties.AddRange(warranties);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Company> SeedCompanyAsync(ErpDbContext db, CancellationToken ct)
    {
        var company = new Company
        {
            Code = "APC",
            Name = "Ai Platform Company",
            Address = "Dhaka, Bangladesh",
            OpeningDate = new DateTime(2024, 1, 1),
            CreatedBy = SystemUserId
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync(ct);
        return company;
    }

    private static async Task<List<Location>> SeedLocationsAsync(ErpDbContext db, Company company, CancellationToken ct)
    {
        var locations = Branches
            .Select(b => new Location { Code = b.Code, Name = b.Name, CompanyId = company.CompanyId, CreatedBy = SystemUserId })
            .ToList();

        db.Locations.AddRange(locations);
        await db.SaveChangesAsync(ct);
        return locations;
    }

    private static async Task SeedUsersAsync(ErpDbContext db, Company company, List<Location> locations, CancellationToken ct)
    {
        var users = locations.Select((location, index) => new ErpUser
        {
            Code = Code("USR", company, location, index + 1),
            Name = index == 0 ? "Saiful Islam" : "Nazmul Hasan",
            Username = index == 0 ? "saiful" : "nazmul",
            Designation = index == 0 ? "Admin" : "Branch Manager",
            LocationId = location.LocationId,
            CompanyId = company.CompanyId,
            CreatedBy = SystemUserId
        });

        db.ErpUsers.AddRange(users);
        await db.SaveChangesAsync(ct);
    }

    // ⚠️ Product location-wise আলাদা row — তাই প্রতি branch-এ পুরো catalog আবার বসে।
    private static async Task<List<Product>> SeedProductsAsync(
        ErpDbContext db, Company company, Location location, CancellationToken ct)
    {
        var products = Catalog.Select(item => new Product
        {
            Name = item.Name,
            Category = item.Category,
            Brand = item.Brand,
            Unit = "Pcs",
            IsSerialized = item.Serialized,
            LocationId = location.LocationId,
            CompanyId = company.CompanyId,
            CreatedBy = SystemUserId
        }).ToList();

        db.Products.AddRange(products);
        await db.SaveChangesAsync(ct);
        return products;
    }

    private static async Task<List<Supplier>> SeedSuppliersAsync(
        ErpDbContext db, Company company, Location location, CancellationToken ct)
    {
        var suppliers = SupplierNames.Select((name, index) => new Supplier
        {
            Code = Code("SUP", company, location, index + 1),
            Name = name,
            Phone = $"0170000{index:D4}",
            Address = $"{location.Name}, Bangladesh",
            LocationId = location.LocationId,
            CompanyId = company.CompanyId,
            CreatedBy = SystemUserId
        }).ToList();

        db.Suppliers.AddRange(suppliers);
        await db.SaveChangesAsync(ct);
        return suppliers;
    }

    private static async Task<List<Customer>> SeedCustomersAsync(
        ErpDbContext db, Company company, Location location, CancellationToken ct)
    {
        var customers = CustomerNames.Select((name, index) => new Customer
        {
            Code = Code("CUS", company, location, index + 1),
            Name = name,
            Phone = $"0180000{index:D4}",
            Address = $"{location.Name}, Bangladesh",
            LocationId = location.LocationId,
            CompanyId = company.CompanyId,
            CreatedBy = SystemUserId
        }).ToList();

        db.Customers.AddRange(customers);
        await db.SaveChangesAsync(ct);
        return customers;
    }

    private static async Task SeedPurchaseAsync(
        ErpDbContext db, ErpStockService stock, Random random, Company company, Location location,
        List<Supplier> suppliers, List<Product> products, DateOnly monthStart, CancellationToken ct)
    {
        var orderNo = await NextOrderNoAsync(db, "PO", company, location, monthStart.Year, ct);
        var order = new PurchaseOrder
        {
            PurchaseOrderNo = orderNo,
            SupplierId = suppliers[random.Next(suppliers.Count)].SupplierId,
            OrderDate = RandomDayInMonth(random, monthStart),
            LocationId = location.LocationId,
            CompanyId = company.CompanyId,
            CreatedBy = SystemUserId
        };

        foreach (var product in PickDistinct(random, products, count: random.Next(2, 5)))
        {
            var quantity = product.IsSerialized ? random.Next(2, 6) : random.Next(10, 41);
            var unitPrice = CostOf(product.Name);

            var detail = new PurchaseOrderDetail
            {
                ProductId = product.ProductId,
                Quantity = quantity,
                UnitPrice = unitPrice,
                TotalAmount = quantity * unitPrice,
                CreatedBy = SystemUserId
            };

            if (product.IsSerialized)
            {
                for (var unit = 1; unit <= quantity; unit++)
                {
                    detail.Serials.Add(new PurchaseOrderSerial
                    {
                        ProductId = product.ProductId,
                        SerialNo = $"{product.Brand.ToUpperInvariant()}-{product.ProductId}-{orderNo[^6..]}-{unit:D2}",
                        CreatedBy = SystemUserId
                    });
                }
            }

            order.Details.Add(detail);
        }

        order.TotalAmount = order.Details.Sum(d => d.TotalAmount);
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(ct);

        await stock.ApprovePurchaseAsync(order.PurchaseOrderId, SystemUserId, ct);
    }

    private static async Task SeedSaleAsync(
        ErpDbContext db, ErpStockService stock, Random random, Company company, Location location,
        List<Customer> customers, List<Product> products, DateOnly monthStart, CancellationToken ct)
    {
        // যেসব পণ্য সত্যিই গুদামে আছে কেবল সেগুলোই বিক্রি হতে পারে।
        var balances = await db.StockBalances
            .Where(b => b.LocationId == location.LocationId && b.QuantityOnHand > 0)
            .ToDictionaryAsync(b => b.ProductId, b => b.QuantityOnHand, ct);

        var sellable = products.Where(p => balances.ContainsKey(p.ProductId)).ToList();
        if (sellable.Count == 0)
            return;

        var orderNo = await NextOrderNoAsync(db, "SO", company, location, monthStart.Year, ct);
        var order = new SalesOrder
        {
            SalesOrderNo = orderNo,
            CustomerId = customers[random.Next(customers.Count)].CustomerId,
            OrderDate = RandomDayInMonth(random, monthStart),
            LocationId = location.LocationId,
            CompanyId = company.CompanyId,
            CreatedBy = SystemUserId
        };

        foreach (var product in PickDistinct(random, sellable, count: random.Next(1, 4)))
        {
            // হাতে যা আছে তার বেশি কখনোই নয় — নইলে approve throw করত।
            var available = balances[product.ProductId];
            var quantity = product.IsSerialized
                ? random.Next(1, Math.Min(available, 3) + 1)
                : random.Next(1, Math.Min(available, 10) + 1);

            var unitPrice = Math.Round(CostOf(product.Name) * SalesMargin, 2);

            var detail = new SalesOrderDetail
            {
                ProductId = product.ProductId,
                Quantity = quantity,
                UnitPrice = unitPrice,
                TotalAmount = quantity * unitPrice,
                CreatedBy = SystemUserId
            };

            if (product.IsSerialized)
            {
                // গুদামে থাকা আসল serial থেকেই বেছে নিই (কাল্পনিক serial দিলে approve fail করত)।
                var inStock = await db.CurrentStockDetails
                    .Where(x => x.ProductId == product.ProductId)
                    .OrderBy(x => x.CurrentStockDetailId)
                    .Take(quantity)
                    .Select(x => x.SerialNo)
                    .ToListAsync(ct);

                if (inStock.Count < quantity)
                    continue;

                foreach (var serialNo in inStock)
                {
                    detail.Serials.Add(new SalesOrderSerial
                    {
                        ProductId = product.ProductId,
                        SerialNo = serialNo,
                        CreatedBy = SystemUserId
                    });
                }
            }

            order.Details.Add(detail);
        }

        if (order.Details.Count == 0)
            return;

        order.TotalAmount = order.Details.Sum(d => d.TotalAmount);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync(ct);

        await stock.ApproveSalesAsync(order.SalesOrderId, SystemUserId, ct);
    }

    private static string Code(string prefix, Company company, Location location, int sequence)
        => $"{prefix}-{company.Code}-{location.Code}-{SeedStart.Year}-{sequence:D6}";

    private static async Task<string> NextOrderNoAsync(
        ErpDbContext db, string prefix, Company company, Location location, int year, CancellationToken ct)
    {
        var pattern = $"{prefix}-{company.Code}-{location.Code}-{year}-";
        var used = prefix == "PO"
            ? await db.PurchaseOrders.CountAsync(o => o.PurchaseOrderNo.StartsWith(pattern), ct)
            : await db.SalesOrders.CountAsync(o => o.SalesOrderNo.StartsWith(pattern), ct);

        return $"{pattern}{used + 1:D6}";
    }

    private static decimal CostOf(string productName)
        => Catalog.First(c => c.Name == productName).Cost;

    private static DateOnly RandomDayInMonth(Random random, DateOnly monthStart)
        => monthStart.AddDays(random.Next(DateTime.DaysInMonth(monthStart.Year, monthStart.Month)));

    private static List<T> PickDistinct<T>(Random random, List<T> source, int count)
        => [.. source.OrderBy(_ => random.Next()).Take(Math.Min(count, source.Count))];
}
