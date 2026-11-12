using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Domain.Entities.Erp;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiPlatform.Infrastructure.Agent;

// একমাত্র write tool — খসড়া (unapproved) SalesOrder বানায়।
//
// ⚠️ কখনো approve করে না। approve-এ stock কমে, ledger row বসে আর InvoiceNo assign হয় —
//    সেটা মানুষের সিদ্ধান্ত। AI ভুল বুঝে approve করলে গুদামের হিসাব বদলে যেত, আর ledger
//    থেকে ফেরানো কঠিন। খসড়া order নিরাপদ: stock-এ কোনো প্রভাব নেই, ভুল হলে মুছে দিলেই হলো।
//    এটাই এই tool-এর মূল guardrail — আলাদা confirm-flow ছাড়াই।
//
// তাই নাম create_invoice নয়: invoice নম্বর approve-এর আগে জন্মায়ই না। যে কাজ করে না,
// সেই নাম দিলে LLM-ও ভুল প্রতিশ্রুতি দিত ("ইনভয়েস তৈরি হয়েছে")।
public class CreateSalesOrderTool(ErpDbContext db) : ITool
{
    // খসড়ার CreatedBy। AppUser → ErpUser ম্যাপিং এখনো নেই (erp-integration-plan §খোলা প্রশ্ন),
    // তাই আপাতত seed-এর system user। ম্যাপিং এলে আসল user id বসবে।
    private const long SystemUserId = 1;

    private const int MaxItems = 20;

    public string Name => "create_sales_order";

    public string Description =>
        "Creates a DRAFT sales order (an unapproved invoice) in the ERP database for a customer. " +
        "Use when the user asks to make a bill, invoice, or sales order. " +
        "The draft does NOT move stock and gets NO invoice number — a human approves it in the ERP, " +
        "and only then stock is deducted and the invoice number is assigned. Say this plainly in your answer; " +
        "never tell the user an invoice has been issued. " +
        "Every unit price must come from the user — if a price is missing, ask for it instead of guessing. " +
        "Answer the user in Bengali.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            customerName = new { type = "string", description = "Customer name or code, e.g. 'Karim Traders' or 'CUS-APC-HO-2026-000001'." },
            location = new { type = "string", description = "Branch code or name the order belongs to, e.g. 'HO', 'CB', 'Chittagong'." },
            orderDate = new { type = "string", description = "Optional, yyyy-MM-dd. Defaults to today." },
            items = new
            {
                type = "array",
                description = "Order lines. At least one.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        productName = new { type = "string", description = "Product name or part of it, e.g. 'Dell XPS 13'." },
                        quantity = new { type = "integer", description = "Units to sell. Must be greater than zero." },
                        unitPrice = new { type = "number", description = "Selling price per unit, as told by the user. Never invent this." }
                    },
                    required = new[] { "productName", "quantity", "unitPrice" }
                }
            }
        },
        required = new[] { "customerName", "location", "items" }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return Error("Arguments must be an object with customerName, location and items.");

        var customerName = ReadString(arguments, "customerName");
        var location = ReadString(arguments, "location");

        if (string.IsNullOrWhiteSpace(customerName))
            return Error("customerName is required.");
        if (string.IsNullOrWhiteSpace(location))
            return Error("location is required — a product exists separately per branch.");

        if (!TryReadItems(arguments, out var requested, out var itemError))
            return Error(itemError);

        var branch = await ResolveLocationAsync(location, cancellationToken);
        if (branch is null)
            return Error($"No branch matched '{location}'.");

        var customer = await ResolveCustomerAsync(customerName, branch.LocationId, cancellationToken);
        if (customer.Error is not null)
            return Error(customer.Error);

        var lines = new List<(Product Product, int Quantity, decimal UnitPrice)>(requested.Count);
        foreach (var item in requested)
        {
            var resolved = await ResolveProductAsync(item.ProductName, branch.LocationId, cancellationToken);
            if (resolved.Error is not null)
                return Error(resolved.Error);

            // approve-এ stock না থাকলে throw হতো — খসড়া বানানোর সময়েই ধরিয়ে দিই, তখন
            // LLM পরিমাণ কমিয়ে বা অন্য branch দেখে শুধরাতে পারে।
            var available = await db.StockBalances
                .Where(b => b.ProductId == resolved.Product!.ProductId && b.LocationId == branch.LocationId)
                .Select(b => (int?)b.QuantityOnHand)
                .FirstOrDefaultAsync(cancellationToken) ?? 0;

            if (available < item.Quantity)
            {
                return Error($"Insufficient stock for '{resolved.Product!.Name}' at {branch.Code}: " +
                             $"{available} on hand, {item.Quantity} requested.");
            }

            lines.Add((resolved.Product!, item.Quantity, item.UnitPrice));
        }

        var orderDate = ReadDate(arguments, "orderDate") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var order = await BuildDraftAsync(customer.Customer!, branch, orderDate, lines, cancellationToken);

        db.SalesOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        return Json(new
        {
            created = true,
            salesOrderNo = order.SalesOrderNo,
            approved = false,
            invoiceNo = (string?)null,
            customer = customer.Customer!.Name,
            location = branch.Code,
            orderDate = order.OrderDate.ToString("yyyy-MM-dd"),
            // অঙ্ক সাজানো string — কাঁচা সংখ্যা দিলে LLM গোল করে ভুল লিখত (ToolFormat দেখো)।
            totalAmount = ToolFormat.Money(order.TotalAmount),
            lines = order.Details.Select(d => new
            {
                product = lines.First(l => l.Product.ProductId == d.ProductId).Product.Name,
                quantity = d.Quantity,
                unitPrice = ToolFormat.Money(d.UnitPrice),
                totalAmount = ToolFormat.Money(d.TotalAmount),
                serials = d.Serials.Select(s => s.SerialNo)
            }),
            note = "Draft only. Stock is unchanged and no invoice number exists yet — " +
                   "a human must approve this order in the ERP for that to happen."
        });
    }

    private async Task<SalesOrder> BuildDraftAsync(
        Customer customer, Location branch, DateOnly orderDate,
        List<(Product Product, int Quantity, decimal UnitPrice)> lines, CancellationToken cancellationToken)
    {
        var order = new SalesOrder
        {
            SalesOrderNo = await NextOrderNoAsync(branch, orderDate.Year, cancellationToken),
            CustomerId = customer.CustomerId,
            OrderDate = orderDate,
            LocationId = branch.LocationId,
            CompanyId = branch.CompanyId,
            CreatedBy = SystemUserId
        };

        foreach (var (product, quantity, unitPrice) in lines)
        {
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
                // গুদামে থাকা আসল serial থেকেই নিই (পুরনো আগে), seeder যেভাবে করে। কাল্পনিক serial
                // দিলে approve-এ "Serial(s) not in stock" throw করত। ⚠️ reserve নয় — এই serial
                // অন্য খসড়াতেও যেতে পারে; সংঘর্ষ হলে approve সশব্দে ব্যর্থ হবে, চুপচাপ নয়।
                var serials = await db.CurrentStockDetails
                    .Where(x => x.ProductId == product.ProductId)
                    .OrderBy(x => x.CurrentStockDetailId)
                    .Take(quantity)
                    .Select(x => x.SerialNo)
                    .ToListAsync(cancellationToken);

                foreach (var serialNo in serials)
                    detail.Serials.Add(new SalesOrderSerial { ProductId = product.ProductId, SerialNo = serialNo, CreatedBy = SystemUserId });
            }

            order.Details.Add(detail);
        }

        order.TotalAmount = order.Details.Sum(d => d.TotalAmount);
        return order;
    }

    private async Task<Location?> ResolveLocationAsync(string location, CancellationToken cancellationToken)
        => await db.Locations
            .FirstOrDefaultAsync(l => l.Code == location || EF.Functions.Like(l.Name, $"%{location}%"), cancellationToken);

    private async Task<(Customer? Customer, string? Error)> ResolveCustomerAsync(
        string name, long locationId, CancellationToken cancellationToken)
    {
        var matches = await db.Customers
            .Where(c => c.LocationId == locationId
                        && (c.Code == name || EF.Functions.Like(c.Name, $"%{name}%")))
            .Take(5)
            .ToListAsync(cancellationToken);

        return matches switch
        {
            { Count: 0 } => (null, $"No customer matched '{name}' at this branch."),
            // একাধিক মিললে নিজে বেছে নেওয়া বিপজ্জনক — ভুল customer-এর নামে order বসে যেত।
            { Count: > 1 } => (null, $"'{name}' matched {matches.Count} customers: " +
                                    $"{string.Join(", ", matches.Select(c => c.Name))}. Ask the user which one."),
            _ => (matches[0], null)
        };
    }

    private async Task<(Product? Product, string? Error)> ResolveProductAsync(
        string name, long locationId, CancellationToken cancellationToken)
    {
        var matches = await db.Products
            .Where(p => p.LocationId == locationId && EF.Functions.Like(p.Name, $"%{name}%"))
            .Take(5)
            .ToListAsync(cancellationToken);

        return matches switch
        {
            { Count: 0 } => (null, $"No product matched '{name}' at this branch."),
            { Count: > 1 } => (null, $"'{name}' matched {matches.Count} products: " +
                                    $"{string.Join(", ", matches.Select(p => p.Name))}. Ask the user which one."),
            _ => (matches[0], null)
        };
    }

    // "SO-{Company}-{Location}-{Year}-{000001}" — ErpSeeder ও ErpStockService-এর একই ধারা।
    // ponytail: count-ভিত্তিক, তাই একসাথে দুটো খসড়া তৈরি হলে সংঘর্ষ হতে পারে (unique index ধরবে)।
    // concurrent তৈরি দরকার হলে sequence table নাও — invoice নম্বরের ক্ষেত্রেও একই সীমা।
    private async Task<string> NextOrderNoAsync(Location branch, int year, CancellationToken cancellationToken)
    {
        var companyCode = await db.Companies
            .Where(c => c.CompanyId == branch.CompanyId).Select(c => c.Code).FirstAsync(cancellationToken);

        var prefix = $"SO-{companyCode}-{branch.Code}-{year}-";
        var used = await db.SalesOrders.CountAsync(o => o.SalesOrderNo.StartsWith(prefix), cancellationToken);

        return $"{prefix}{used + 1:D6}";
    }

    private static bool TryReadItems(
        JsonElement arguments, out List<(string ProductName, int Quantity, decimal UnitPrice)> items, out string error)
    {
        items = [];
        error = string.Empty;

        if (!arguments.TryGetProperty("items", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            error = "items is required and must be an array of { productName, quantity, unitPrice }.";
            return false;
        }

        foreach (var element in array.EnumerateArray())
        {
            var productName = ReadString(element, "productName");
            if (string.IsNullOrWhiteSpace(productName))
            {
                error = "Every item needs a productName.";
                return false;
            }

            var quantity = ReadInt(element, "quantity");
            if (quantity is null or <= 0)
            {
                error = $"Item '{productName}' needs a quantity greater than zero.";
                return false;
            }

            var unitPrice = ReadDecimal(element, "unitPrice");
            if (unitPrice is null or < 0)
            {
                // দাম আবিষ্কার করার চেয়ে জিজ্ঞেস করা ভালো — ভুল দামের order টাকার ক্ষতি।
                error = $"Item '{productName}' needs a unitPrice. Ask the user for the price instead of assuming one.";
                return false;
            }

            items.Add((productName, quantity.Value, unitPrice.Value));
        }

        if (items.Count == 0)
        {
            error = "items must contain at least one line.";
            return false;
        }

        if (items.Count > MaxItems)
        {
            error = $"Too many lines ({items.Count}); at most {MaxItems} per order.";
            return false;
        }

        return true;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var property)
           && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    // ছোট model সংখ্যাকে মাঝে মাঝে string হিসেবে পাঠায় ("10") — অন্য ERP tool-এও একই আচরণ।
    private static int? ReadInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static DateOnly? ReadDate(JsonElement element, string name)
        => DateOnly.TryParse(ReadString(element, name), out var date) ? date : null;

    private static string Error(string message) => Json(new { created = false, error = message });

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
