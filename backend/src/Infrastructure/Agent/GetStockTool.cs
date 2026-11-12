using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiPlatform.Infrastructure.Agent;

// এই মুহূর্তে গুদামে কী কত আছে। StockBalance (summary) পড়ে — ledger যোগ করে না,
// তাই দ্রুত। serial চাইলে CurrentStockDetail থেকে আলাদা করে আনে।
//
// ⚠️ get_sales-এর মতোই: LLM শুধু parameter ভরে, query fixed। GetSalesTool-এর সাথে গঠন
// ইচ্ছাকৃতভাবে এক রকম — দুটোর আচরণ আলাদা হলে LLM-ও গুলিয়ে ফেলে।
public class GetStockTool(ErpDbContext db) : ITool
{
    private const int MaxRows = 50;

    // serial সংখ্যা বেশি হলে context ভরে যায় — এর বেশি চাইলে শুধু গুনে বলি।
    private const int MaxSerialsPerRow = 20;

    public string Name => "get_stock";

    public string Description =>
        "Returns current inventory on hand from the ERP database — what is physically in the warehouse right now. " +
        "Use for questions about stock levels, availability, or what is running low. " +
        "Filters (productName, location) can be combined. " +
        "Set belowQuantity to find items running low (e.g. belowQuantity=10 lists everything with fewer than 10 units); " +
        "results are then sorted lowest-first, otherwise highest-first. " +
        "Set includeSerials=true to list the serial numbers of tracked items (laptops, PCs) — " +
        "accessories like cables and mice have no serials. " +
        "This tool does NOT answer sales questions — use get_sales for what was sold. " +
        "Answer the user in Bengali.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            productName = new { type = "string", description = "Optional. Partial product name, e.g. 'Dell' or 'Mouse'. Omit to list everything." },
            location = new { type = "string", description = "Optional. Branch code or name, e.g. 'HO', 'CB', 'Chittagong'." },
            belowQuantity = new { type = "integer", description = "Optional. Only items with fewer than this many units on hand. Use for 'what is running low'." },
            includeSerials = new { type = "boolean", description = "Optional, default false. Include serial numbers of serialized items." }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var belowQuantity = ReadInt(arguments, "belowQuantity");
        if (belowQuantity is < 0)
            return Error("belowQuantity must not be negative.");

        var productName = ReadString(arguments, "productName");
        var location = ReadString(arguments, "location");
        var includeSerials = ReadBool(arguments, "includeSerials") ?? false;

        var query = db.StockBalances.AsQueryable();

        if (!string.IsNullOrWhiteSpace(productName))
            query = query.Where(b => EF.Functions.Like(b.Product.Name, $"%{productName}%"));

        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(b => b.Location.Code == location
                                     || EF.Functions.Like(b.Location.Name, $"%{location}%"));

        if (belowQuantity is not null)
            query = query.Where(b => b.QuantityOnHand < belowQuantity);

        var projected = query.Select(b => new StockRow
        {
            Product = b.Product.Name,
            Category = b.Product.Category,
            LocationCode = b.Location.Code,
            QuantityOnHand = b.QuantityOnHand,
            IsSerialized = b.Product.IsSerialized,
            ProductId = b.ProductId,
            LocationId = b.LocationId
        });

        // "কী কমে যাচ্ছে" জিজ্ঞেস করলে কম-আগে; নইলে বেশি-আগে (কী মজুত আছে)।
        var rows = await (belowQuantity is not null
                ? projected.OrderBy(r => r.QuantityOnHand)
                : projected.OrderByDescending(r => r.QuantityOnHand))
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return Json(new
            {
                rowCount = 0,
                note = belowQuantity is not null
                    ? "No product is below that quantity — nothing is running low with these filters."
                    : "No stock record matched. The product or location name may not exist in the ERP database."
            });
        }

        var serialsByRow = includeSerials
            ? await LoadSerialsAsync(rows, cancellationToken)
            : null;

        return Json(new
        {
            filters = new { productName, location, belowQuantity },
            asOf = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            rowCount = rows.Count,
            totalUnits = rows.Sum(r => r.QuantityOnHand),
            rows = rows.Select(r => new
            {
                product = r.Product,
                category = r.Category,
                location = r.LocationCode,
                quantityOnHand = r.QuantityOnHand,
                serialized = r.IsSerialized,
                serials = serialsByRow?.GetValueOrDefault((r.ProductId, r.LocationId))
            })
        });
    }

    // serial গুলো Location-এ বাঁধা নয় (CurrentStockDetail-এ LocationId নেই) — কোন movement-এ
    // ঢুকেছিল তার মাধ্যমে location বের করি, নইলে দুই branch-এর serial মিশে যেত।
    private async Task<Dictionary<(long ProductId, long LocationId), object>> LoadSerialsAsync(
        List<StockRow> rows, CancellationToken cancellationToken)
    {
        var productIds = rows.Where(r => r.IsSerialized).Select(r => r.ProductId).Distinct().ToList();
        if (productIds.Count == 0)
            return [];

        var serials = await db.CurrentStockDetails
            .Where(d => productIds.Contains(d.ProductId))
            .Select(d => new { d.ProductId, d.CurrentStock.LocationId, d.SerialNo })
            .ToListAsync(cancellationToken);

        return serials
            .GroupBy(s => (s.ProductId, s.LocationId))
            .ToDictionary(
                g => g.Key,
                g => (object)(g.Count() > MaxSerialsPerRow
                    // এত serial উত্তরে ঢাললে LLM-এর context ভরে যায় — সংখ্যা + নমুনা যথেষ্ট।
                    ? new { count = g.Count(), sample = g.Take(MaxSerialsPerRow).Select(s => s.SerialNo), truncated = true }
                    : new { count = g.Count(), sample = g.Select(s => s.SerialNo), truncated = false }));
    }

    private static string? ReadString(JsonElement arguments, string name)
        => arguments.ValueKind == JsonValueKind.Object
           && arguments.TryGetProperty(name, out var property)
           && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? ReadInt(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var property))
            return null;

        // ছোট model সংখ্যাকে মাঝে মাঝে string হিসেবে পাঠায় ("10")।
        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string Error(string message) => Json(new { error = message });

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ProductId/LocationId উত্তরে যায় না — শুধু serial জোড়া লাগাতে লাগে।
    // ⚠️ record constructor নয়, object-initializer — positional record EF SQL-এ নামাতে পারে না
    // ("could not be translated")। GetSalesTool-এও একই কারণে class (2026-07-22)।
    private sealed class StockRow
    {
        public string Product { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string LocationCode { get; set; } = string.Empty;
        public int QuantityOnHand { get; set; }
        public bool IsSerialized { get; set; }
        public long ProductId { get; set; }
        public long LocationId { get; set; }
    }
}
