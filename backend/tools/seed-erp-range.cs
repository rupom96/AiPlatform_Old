#:project ../src/Infrastructure/AiPlatform.Infrastructure.csproj
#:property PublishAot=false
#:property InvariantGlobalization=false

// ERP sample data — মাসের পর মাস purchase + sales বানায়, ErpSeeder যেভাবে বানায় ঠিক সেভাবে।
//
// ErpSeeder শুধু জানুয়ারি→জুলাই ২০২৬ বসায় আর data থাকলে আর চলে না। এই script বাকি মাসগুলো
// ভরে — বা যেকোনো পরিসর, যতবার খুশি। কারণ এটা **মাস-ভিত্তিক idempotent**: যে মাসে ঐ branch-এর
// purchase order আগেই আছে, সে মাস ছুঁয়েও দেখে না। তাই দুবার চালালেও দ্বিগুণ data হয় না।
//
// চালানোর নিয়ম (project root থেকে):
//   dotnet run backend/tools/seed-erp-range.cs                      → ২০২৬-০১ থেকে আজ পর্যন্ত
//   dotnet run backend/tools/seed-erp-range.cs 2026-08 2026-11      → নির্দিষ্ট পরিসর
//   dotnet run backend/tools/seed-erp-range.cs --dry-run            → কী হবে শুধু দেখায়, লেখে না
//
// শর্ত: Company/Location/Product/Customer/Supplier আগে থেকে থাকতে হবে — অর্থাৎ API অন্তত
// একবার চলে ErpSeeder-কে বসিয়ে থাকতে হবে। খালি DB-তে এই script কিছু করবে না, বলে দেবে।
//
// ⚠️ Stock নিজে হাতে লেখে না — ErpStockService.Approve* ডাকে, ErpSeeder-এর মতোই।
// তাই ledger (CurrentStock), summary (StockBalance), serial (CurrentStockDetail) আর InvoiceNo
// সবসময় সামঞ্জস্যপূর্ণ থাকে। হাতে INSERT লিখলে ঠিক এখানেই গন্ডগোল হতো।

using System.Runtime.CompilerServices;
using System.Text.Json;
using AiPlatform.Domain.Entities.Erp;
using AiPlatform.Infrastructure.Erp;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

const long SystemUserId = 1;      // seed data-র CreatedBy (Admin) — ErpSeeder-এর মতোই
const int BaseRandomSeed = 20260722;
const decimal SalesMargin = 1.18m;

var dryRun = args.Contains("--dry-run");
var months = args.Where(a => !a.StartsWith("--")).ToArray();

var today = DateOnly.FromDateTime(DateTime.Now);
var fromMonth = ParseMonth(months.ElementAtOrDefault(0)) ?? new DateOnly(2026, 1, 1);
var toMonth = ParseMonth(months.ElementAtOrDefault(1)) ?? new DateOnly(today.Year, today.Month, 1);

if (toMonth < fromMonth)
{
    Console.Error.WriteLine("Error: the 'to' month is before the 'from' month.");
    return 1;
}

var connection = ResolveConnectionString();
if (connection is null)
{
    Console.Error.WriteLine("Error: ERP connection string not found. Set ERP_CONNECTION, or keep it in backend/src/Api/appsettings.Development.json under ConnectionStrings:Erp.");
    return 1;
}

// DI-র ErpDbContext যেভাবে তৈরি হয় হুবহু সেভাবে — NoTracking default
// (DependencyInjection.cs)। ErpStockService এই আচরণ ধরেই লেখা, তাই এখানে বদলাই না।
var options = new DbContextOptionsBuilder<ErpDbContext>()
    .UseSqlServer(connection)
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
    .Options;

await using var db = new ErpDbContext(options);
var stock = new ErpStockService(db);

var company = await db.Companies.OrderBy(c => c.CompanyId).FirstOrDefaultAsync();
if (company is null)
{
    Console.Error.WriteLine("Error: no company in the ERP database. Start the API once so ErpSeeder can create the base data, then run this again.");
    return 1;
}

var locations = await db.Locations.Where(l => l.CompanyId == company.CompanyId)
    .OrderBy(l => l.LocationId).ToListAsync();
var productsByLocation = (await db.Products.Where(p => p.CompanyId == company.CompanyId).ToListAsync())
    .GroupBy(p => p.LocationId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.ProductId).ToList());
var suppliersByLocation = (await db.Suppliers.Where(s => s.CompanyId == company.CompanyId).ToListAsync())
    .GroupBy(s => s.LocationId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.SupplierId).ToList());
var customersByLocation = (await db.Customers.Where(c => c.CompanyId == company.CompanyId).ToListAsync())
    .GroupBy(c => c.LocationId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.CustomerId).ToList());

// প্রতিটা পণ্যের দাম DB থেকেই — script-এ আলাদা দামের তালিকা রাখলে ErpSeeder-এর Catalog
// বদলালে দুটো আলাদা হয়ে যেত। যে পণ্য কখনো কেনা হয়নি তার দাম জানা নেই, তাই বাদ।
var costByProductId = await db.PurchaseOrderDetails
    .GroupBy(d => d.ProductId)
    .Select(g => new { ProductId = g.Key, Cost = g.OrderByDescending(d => d.PurchaseOrderDetailId).First().UnitPrice })
    .ToDictionaryAsync(x => x.ProductId, x => x.Cost);

Console.WriteLine($"Company  : {company.Code} — {company.Name}");
Console.WriteLine($"Range    : {fromMonth:yyyy-MM} → {toMonth:yyyy-MM}   (today {today:yyyy-MM-dd})");
Console.WriteLine($"Locations: {string.Join(", ", locations.Select(l => l.Code))}");
Console.WriteLine(dryRun ? "Mode     : DRY RUN — nothing will be written\n" : "Mode     : WRITE\n");

var createdPurchases = 0;
var createdSales = 0;
var skippedMonths = 0;

for (var month = fromMonth; month <= toMonth; month = month.AddMonths(1))
{
    foreach (var location in locations)
    {
        var monthEnd = month.AddMonths(1).AddDays(-1);

        // চলতি মাসে ভবিষ্যতের তারিখ বসাই না — "আজ পর্যন্ত" মানে সত্যিই আজ পর্যন্ত।
        var lastDay = monthEnd > today ? today : monthEnd;
        if (lastDay < month)
            continue;

        // ঐ branch-এ ঐ মাসের purchase order থাকলে মাসটা আগেই ভরা। sales দিয়ে যাচাই করি না,
        // কারণ create_sales_order tool-এর খসড়াও SalesOrders-এ বসে — ওটা seed নয়।
        var alreadySeeded = await db.PurchaseOrders.AnyAsync(o =>
            o.LocationId == location.LocationId && o.OrderDate >= month && o.OrderDate <= monthEnd);

        if (alreadySeeded)
        {
            Console.WriteLine($"{month:yyyy-MM} {location.Code}  skipped — already has purchase orders");
            skippedMonths++;
            continue;
        }

        if (!productsByLocation.TryGetValue(location.LocationId, out var products) ||
            !suppliersByLocation.TryGetValue(location.LocationId, out var suppliers) ||
            !customersByLocation.TryGetValue(location.LocationId, out var customers))
        {
            Console.WriteLine($"{month:yyyy-MM} {location.Code}  skipped — no products/suppliers/customers for this branch");
            skippedMonths++;
            continue;
        }

        // Deterministic — একই মাস একই branch সবসময় একই সংখ্যা দেয়, তাই AI-র উত্তর যাচাই করা যায়।
        var random = new Random(BaseRandomSeed + (int)location.LocationId * 10_000 + month.Year * 100 + month.Month);

        var purchases = 0;
        var sales = 0;

        // আগে কেনা (stock ঢোকে), তারপর বিক্রি (যা আছে তার মধ্যেই) — ErpSeeder-এর ক্রম।
        for (var i = 0; i < 2; i++)
            if (await SeedPurchaseAsync(random, location, suppliers, products, month, lastDay))
                purchases++;

        for (var i = 0; i < 4; i++)
            if (await SeedSaleAsync(random, location, customers, products, month, lastDay))
                sales++;

        createdPurchases += purchases;
        createdSales += sales;
        Console.WriteLine($"{month:yyyy-MM} {location.Code}  +{purchases} purchase, +{sales} sales");
    }
}

Console.WriteLine();
Console.WriteLine(dryRun
    ? $"DRY RUN — would create {createdPurchases} purchase and {createdSales} sales orders ({skippedMonths} month/branch skipped)."
    : $"Done. Created {createdPurchases} purchase and {createdSales} sales orders ({skippedMonths} month/branch skipped).");
return 0;

async Task<bool> SeedPurchaseAsync(
    Random random, Location location, List<Supplier> suppliers, List<Product> products,
    DateOnly monthStart, DateOnly lastDay)
{
    var picked = PickDistinct(random, products, random.Next(2, 5))
        .Where(p => costByProductId.ContainsKey(p.ProductId))
        .ToList();

    if (picked.Count == 0)
        return false;

    if (dryRun)
        return true;

    var orderNo = await NextOrderNoAsync("PO", location, monthStart.Year);
    var order = new PurchaseOrder
    {
        PurchaseOrderNo = orderNo,
        SupplierId = suppliers[random.Next(suppliers.Count)].SupplierId,
        OrderDate = RandomDay(random, monthStart, lastDay),
        LocationId = location.LocationId,
        CompanyId = company.CompanyId,
        CreatedBy = SystemUserId
    };

    foreach (var product in picked)
    {
        var quantity = product.IsSerialized ? random.Next(2, 6) : random.Next(10, 41);
        var unitPrice = costByProductId[product.ProductId];

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
            // ErpSeeder-এর হুবহু একই ছক — BRAND-productId-orderNo শেষ ৬ অঙ্ক-unit।
            // orderNo প্রতি branch+বছরে অনন্য, productId branch-ভেদে আলাদা, তাই serial-ও অনন্য।
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
    await db.SaveChangesAsync();

    await stock.ApprovePurchaseAsync(order.PurchaseOrderId, SystemUserId);
    return true;
}

async Task<bool> SeedSaleAsync(
    Random random, Location location, List<Customer> customers, List<Product> products,
    DateOnly monthStart, DateOnly lastDay)
{
    // যা সত্যিই গুদামে আছে কেবল তা-ই বিক্রি হতে পারে — নইলে Approve throw করত।
    var balances = await db.StockBalances
        .Where(b => b.LocationId == location.LocationId && b.QuantityOnHand > 0)
        .ToDictionaryAsync(b => b.ProductId, b => b.QuantityOnHand);

    var sellable = products
        .Where(p => balances.ContainsKey(p.ProductId) && costByProductId.ContainsKey(p.ProductId))
        .ToList();

    if (sellable.Count == 0)
        return false;

    if (dryRun)
        return true;

    var orderNo = await NextOrderNoAsync("SO", location, monthStart.Year);
    var order = new SalesOrder
    {
        SalesOrderNo = orderNo,
        CustomerId = customers[random.Next(customers.Count)].CustomerId,
        OrderDate = RandomDay(random, monthStart, lastDay),
        LocationId = location.LocationId,
        CompanyId = company.CompanyId,
        CreatedBy = SystemUserId
    };

    foreach (var product in PickDistinct(random, sellable, random.Next(1, 4)))
    {
        var available = balances[product.ProductId];
        var quantity = product.IsSerialized
            ? random.Next(1, Math.Min(available, 3) + 1)
            : random.Next(1, Math.Min(available, 10) + 1);

        var unitPrice = Math.Round(costByProductId[product.ProductId] * SalesMargin, 2);

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
            // গুদামে থাকা আসল serial থেকেই (পুরনোটা আগে) — কাল্পনিক serial দিলে Approve fail করত।
            var inStock = await db.CurrentStockDetails
                .Where(x => x.ProductId == product.ProductId)
                .OrderBy(x => x.CurrentStockDetailId)
                .Take(quantity)
                .Select(x => x.SerialNo)
                .ToListAsync();

            // StockBalance আর serial তালিকা একমত না হলে line-টা বাদ — অসম্পূর্ণ serial দিয়ে
            // approve করলে ledger আর গুদাম আলাদা হয়ে যেত।
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
        return false;

    order.TotalAmount = order.Details.Sum(d => d.TotalAmount);
    db.SalesOrders.Add(order);
    await db.SaveChangesAsync();

    await stock.ApproveSalesAsync(order.SalesOrderId, SystemUserId);
    return true;
}

// "PO-APC-HO-2026-000001" — ErpSeeder-এর একই নিয়ম, তাই দুটোর ক্রমিক একসাথে চলে।
async Task<string> NextOrderNoAsync(string prefix, Location location, int year)
{
    var pattern = $"{prefix}-{company!.Code}-{location.Code}-{year}-";
    var used = prefix == "PO"
        ? await db.PurchaseOrders.CountAsync(o => o.PurchaseOrderNo.StartsWith(pattern))
        : await db.SalesOrders.CountAsync(o => o.SalesOrderNo.StartsWith(pattern));

    return $"{pattern}{used + 1:D6}";
}

static DateOnly RandomDay(Random random, DateOnly monthStart, DateOnly lastDay)
    => monthStart.AddDays(random.Next(lastDay.DayNumber - monthStart.DayNumber + 1));

static List<T> PickDistinct<T>(Random random, List<T> source, int count)
    => [.. source.OrderBy(_ => random.Next()).Take(Math.Min(count, source.Count))];

static DateOnly? ParseMonth(string? value)
    => DateOnly.TryParseExact(value, "yyyy-MM", out var month) ? month : null;

// ENV আগে (CI/অন্য মেশিন), নইলে dev-এর appsettings। connection string কখনো এখানে লিখি না।
static string? ResolveConnectionString()
{
    var fromEnv = Environment.GetEnvironmentVariable("ERP_CONNECTION");
    if (!string.IsNullOrWhiteSpace(fromEnv))
        return fromEnv;

    var settings = Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "src", "Api", "appsettings.Development.json");
    if (!File.Exists(settings))
        return null;

    using var json = JsonDocument.Parse(File.ReadAllText(settings));
    return json.RootElement.TryGetProperty("ConnectionStrings", out var connections)
        && connections.TryGetProperty("Erp", out var erp)
            ? erp.GetString()
            : null;
}

// file-based app-এ AppContext.BaseDirectory অস্থায়ী build ফোল্ডার দেখায়, তাই script-এর
// নিজের অবস্থান compile-time-এ ধরে রাখি — যেখান থেকেই চালানো হোক, পথ ঠিক থাকে।
static string ThisFile([CallerFilePath] string path = "") => path;
