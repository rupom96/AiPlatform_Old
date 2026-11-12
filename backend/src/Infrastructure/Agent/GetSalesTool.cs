using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiPlatform.Infrastructure.Agent;

// ERP-র বিক্রির হিসাব। এক tool, ঐচ্ছিক filter — প্রতি প্রশ্নের ধরনে আলাদা tool বানালে
// LLM প্রায়-একরকম নামের ভিড়ে ভুল বাছে (tool explosion)।
//
// ⚠️ LLM নিজে SQL লেখে না — শুধু parameter ভরে। query এখানে fixed, তাই ভুল join বা
// data ফাঁসের সুযোগ নেই। এটাই এই নকশার মূল নিরাপত্তা।
//
// DbContext scoped → এই tool-ও Scoped হিসেবে register (DependencyInjection.cs)।
public class GetSalesTool(ErpDbContext db) : ITool
{
    // এক উত্তরে এত সারির বেশি পাঠালে LLM-এর context ভরে যায় ও মান পড়ে।
    private const int MaxRows = 50;

    private static readonly string[] GroupByValues = ["none", "customer", "product", "month", "location"];

    public string Name => "get_sales";

    // এই লেখাটাই LLM পড়ে সিদ্ধান্ত নেয় — কখন ডাকবে, কোন parameter ভরবে।
    public string Description =>
        "Returns approved sales figures from the ERP database for a date range. " +
        "Optional filters (customerName, productName, location) can be combined freely — " +
        "for example customerName plus productName answers 'how much of product Y did customer X buy'. " +
        "Use groupBy to break the total down, and topN to return only the highest-selling groups " +
        "(e.g. groupBy=customer with topN=3 answers 'top 3 customers'). " +
        "Results are always sorted by total amount, highest first. " +
        "If the user's wording is ambiguous about whether they mean customers or products " +
        "(for example 'top sellers'), ask them to clarify instead of guessing. " +
        "Answer the user in Bengali.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            fromDate = new { type = "string", description = "Start date, inclusive. Format yyyy-MM-dd, e.g. 2026-05-01." },
            toDate = new { type = "string", description = "End date, inclusive. Format yyyy-MM-dd, e.g. 2026-05-31." },
            customerName = new { type = "string", description = "Optional. Partial customer name; matches case-insensitively." },
            productName = new { type = "string", description = "Optional. Partial product name, e.g. 'Dell' or 'Mouse'." },
            location = new { type = "string", description = "Optional. Branch code or name, e.g. 'HO', 'CB', 'Chittagong'." },
            groupBy = new
            {
                type = "string",
                @enum = GroupByValues,
                description = "Optional. Break the total down by this dimension. Default 'none' returns one grand total."
            },
            topN = new { type = "integer", description = "Optional. Return only the top N groups by amount. Requires groupBy." }
        },
        required = new[] { "fromDate", "toDate" }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        // trust boundary — argument গুলো LLM-এর তৈরি, বিশ্বাস করা যায় না।
        if (!TryReadDate(arguments, "fromDate", out var fromDate, out var error) ||
            !TryReadDate(arguments, "toDate", out var toDate, out error))
            return Error(error!);

        if (toDate < fromDate)
            return Error("toDate must not be earlier than fromDate.");

        var groupBy = ReadString(arguments, "groupBy")?.ToLowerInvariant() ?? "none";
        if (!GroupByValues.Contains(groupBy))
            return Error($"groupBy must be one of: {string.Join(", ", GroupByValues)}.");

        var topN = ReadInt(arguments, "topN");
        if (topN is <= 0)
            return Error("topN must be a positive number.");

        var customerName = ReadString(arguments, "customerName");
        var productName = ReadString(arguments, "productName");
        var location = ReadString(arguments, "location");

        // detail-level-এ হিসাব: productName filter দিলে ঐ পণ্যের line গুলোই যোগ হবে,
        // পুরো order নয়। filter না থাকলে যোগফল order total-এর সমান (invariant)।
        var query = db.SalesOrderDetails
            .Where(d => d.SalesOrder.Approved
                        && d.SalesOrder.OrderDate >= fromDate
                        && d.SalesOrder.OrderDate <= toDate);

        if (!string.IsNullOrWhiteSpace(customerName))
            query = query.Where(d => EF.Functions.Like(d.SalesOrder.Customer.Name, $"%{customerName}%"));

        if (!string.IsNullOrWhiteSpace(productName))
            query = query.Where(d => EF.Functions.Like(d.Product.Name, $"%{productName}%"));

        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(d => d.SalesOrder.Location.Code == location
                                     || EF.Functions.Like(d.SalesOrder.Location.Name, $"%{location}%"));

        var rows = groupBy switch
        {
            "customer" => await AggregateAsync(
                query.Select(d => new Keyed<string>
                {
                    Key = d.SalesOrder.Customer.Name,
                    SalesOrderId = d.SalesOrderId,
                    Quantity = d.Quantity,
                    TotalAmount = d.TotalAmount
                }),
                key => key, cancellationToken),

            "product" => await AggregateAsync(
                query.Select(d => new Keyed<string>
                {
                    Key = d.Product.Name,
                    SalesOrderId = d.SalesOrderId,
                    Quantity = d.Quantity,
                    TotalAmount = d.TotalAmount
                }),
                key => key, cancellationToken),

            "location" => await AggregateAsync(
                query.Select(d => new Keyed<string>
                {
                    Key = d.SalesOrder.Location.Name,
                    SalesOrderId = d.SalesOrderId,
                    Quantity = d.Quantity,
                    TotalAmount = d.TotalAmount
                }),
                key => key, cancellationToken),

            // SQL-এ "2026-05" বানানো যায় না, তাই 202605 হিসেবে group করে পরে সাজাই।
            "month" => await AggregateAsync(
                query.Select(d => new Keyed<int>
                {
                    Key = d.SalesOrder.OrderDate.Year * 100 + d.SalesOrder.OrderDate.Month,
                    SalesOrderId = d.SalesOrderId,
                    Quantity = d.Quantity,
                    TotalAmount = d.TotalAmount
                }),
                key => $"{key / 100:D4}-{key % 100:D2}", cancellationToken),

            _ => await GrandTotalAsync(query, cancellationToken)
        };

        if (rows.Count == 0)
        {
            return Json(new
            {
                fromDate = Text(fromDate),
                toDate = Text(toDate),
                rowCount = 0,
                note = "No approved sales matched these filters. The filter values may not exist in the ERP database."
            });
        }

        var limited = rows.Take(topN ?? MaxRows).ToList();

        // ⚠️ দুটো আলাদা যোগফল। topN দিলে LLM আগে grandTotal-কে "শীর্ষ N-এর মোট" ভেবে ভুল বলত,
        // তাই নাম স্পষ্ট করা + কোনটা কী তা লিখে দেওয়া।
        return Json(new
        {
            fromDate = Text(fromDate),
            toDate = Text(toDate),
            filters = new { customerName, productName, location },
            groupBy,
            // ⚠️ অঙ্ক আগেই সাজানো string — কাঁচা সংখ্যা দিলে LLM নিজে গোল করে ভুল লিখত
            // (ToolFormat-এ ঘটনাটা লেখা আছে)। হুবহু তুলে দেওয়াই একমাত্র পথ।
            totalAmountAllRows = ToolFormat.Money(rows.Sum(r => r.TotalAmount)),
            totalAmountReturnedRows = ToolFormat.Money(limited.Sum(r => r.TotalAmount)),
            note = limited.Count < rows.Count
                ? $"Showing {limited.Count} of {rows.Count} groups. " +
                  "totalAmountAllRows covers every matching group, not just the ones listed."
                : null,
            rowCount = limited.Count,
            truncated = limited.Count < rows.Count,
            rows = limited.Select(r => new
            {
                r.Group,
                r.OrderCount,
                r.Quantity,
                totalAmount = ToolFormat.Money(r.TotalAmount)
            })
        });
    }

    // groupBy=none — একটাই সারি, তাই group করার দরকার নেই।
    private static async Task<List<SalesRow>> GrandTotalAsync(
        IQueryable<Domain.Entities.Erp.SalesOrderDetail> query, CancellationToken cancellationToken)
    {
        var orderCount = await query.Select(d => d.SalesOrderId).Distinct().CountAsync(cancellationToken);
        if (orderCount == 0)
            return [];

        return
        [
            new SalesRow(
                "Total",
                orderCount,
                await query.SumAsync(d => d.Quantity, cancellationToken),
                await query.SumAsync(d => d.TotalAmount, cancellationToken))
        ];
    }

    // group key যাই হোক হিসাবটা একই — order সংখ্যা, পরিমাণ, টাকা; বেশি টাকা আগে।
    private static async Task<List<SalesRow>> AggregateAsync<TKey>(
        IQueryable<Keyed<TKey>> source, Func<TKey, string> label, CancellationToken cancellationToken)
        where TKey : notnull
    {
        var totals = await source
            .GroupBy(x => x.Key)
            .Select(g => new
            {
                g.Key,
                Quantity = g.Sum(x => x.Quantity),
                Amount = g.Sum(x => x.TotalAmount)
            })
            .OrderByDescending(x => x.Amount)
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        // এক order-এ একই group-এর একাধিক line থাকতে পারে, তাই order গোনার আগে distinct।
        // COUNT(DISTINCT ...) EF group-projection-এ অনুবাদ করে না — তাই আলাদা query।
        var orderCounts = await source
            .Select(x => new { x.Key, x.SalesOrderId })
            .Distinct()
            .GroupBy(x => x.Key)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countByKey = orderCounts.ToDictionary(x => x.Key, x => x.Count);

        return [.. totals.Select(t => new SalesRow(
            label(t.Key), countByKey.GetValueOrDefault(t.Key), t.Quantity, t.Amount))];
    }

    private static bool TryReadDate(JsonElement arguments, string name, out DateOnly value, out string? error)
    {
        value = default;
        var raw = ReadString(arguments, name);

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = $"{name} is required (format yyyy-MM-dd).";
            return false;
        }

        if (!DateOnly.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            error = $"{name} '{raw}' is not a valid date. Use format yyyy-MM-dd.";
            return false;
        }

        error = null;
        return true;
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

        // ছোট model সংখ্যাকে মাঝে মাঝে string হিসেবে পাঠায় ("3") — দুটোই মেনে নিই।
        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd");

    // ব্যর্থতা throw করি না — LLM-কে বলি কী ভুল ছিল, ও নিজে শুধরে আবার ডাকতে পারে।
    private static string Error(string message) => Json(new { error = message });

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // বাংলা নাম \uXXXX-এ escape হলে LLM-এর কাছে অপাঠ্য হয়ে যায়।
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private record SalesRow(string Group, int OrderCount, int Quantity, decimal TotalAmount);

    // group key + যা যোগ করতে হবে — SQL-এ project করার মধ্যবর্তী আকার।
    // ⚠️ constructor নয়, object-initializer — record ctor দিলে EF `new Keyed(...).Key`
    // ছোট করতে পারে না ("could not be translated")। member-init হলে column-এ নেমে আসে।
    private sealed class Keyed<TKey>
    {
        public TKey Key { get; set; } = default!;
        public long SalesOrderId { get; set; }
        public int Quantity { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
