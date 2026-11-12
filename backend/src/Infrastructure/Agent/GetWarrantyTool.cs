using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiPlatform.Infrastructure.Agent;

// warranty আছে কি নেই, আর থাকলে কবে শেষ।
//
// দুটো আলাদা প্রশ্নের উত্তর দেয়:
//  · serialNo / invoiceNo → বিক্রি হওয়া নির্দিষ্ট unit — মেয়াদ হিসাব হয় (কবে শেষ, শেষ হয়ে গেছে কি না)
//  · productName          → শুধু offer ("এই laptop-এ কী warranty আসে") — মেয়াদ নেই, কারণ বিক্রির তারিখ নেই
//
// ⚠️ মেয়াদ C#-এ হিসাব হয়, LLM-এ নয়। date arithmetic ("২৪ মাস পরে কোন তারিখ, সেটা কি পেরিয়ে গেছে")
//    ছোট model প্রায়ই ভুল করে, আর ভুলটা আত্মবিশ্বাসের সাথে বলে।
// ⚠️ Warranty শুরুর তারিখ = SalesOrder.OrderDate। ApprovalDateTime নয় — seed/migration-এ ওটা
//    আসল বিক্রির সময় নয়, চালানোর সময় বসে; OrderDate-ই ব্যবসায়িক তারিখ।
public class GetWarrantyTool(ErpDbContext db) : ITool
{
    private const int MaxRows = 50;

    public string Name => "get_warranty";

    public string Description =>
        "Returns warranty coverage from the ERP database. " +
        "Give serialNo or invoiceNo for a unit that was actually sold — the reply then includes the sale date, " +
        "the expiry date of each warranty, and whether it has already expired. " +
        "Give productName instead to see only what warranty a product comes with (no expiry — nothing was sold yet). " +
        "A product may carry several warranties at once (e.g. service 24 months and parts 12 months), " +
        "may be covered for life, or may have none at all. " +
        "This tool does NOT explain how to claim a warranty, what voids it, or which documents are needed — " +
        "use search_knowledge_base for policy and process. " +
        "Answer the user in Bengali.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            serialNo = new { type = "string", description = "Optional. Serial number of a sold unit, e.g. 'DELL-3-000001-01'. Most precise." },
            invoiceNo = new { type = "string", description = "Optional. Invoice number, e.g. 'INV-APC-HO-2026-000001'. Covers every item on that invoice." },
            productName = new { type = "string", description = "Optional. Partial product name, e.g. 'Dell' or 'Mouse'. Returns the offered warranty only, without expiry dates." }
        }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var serialNo = ReadString(arguments, "serialNo");
        var invoiceNo = ReadString(arguments, "invoiceNo");
        var productName = ReadString(arguments, "productName");

        // নির্দিষ্ট থেকে অনির্দিষ্ট — serial একটা unit, invoice একটা বিক্রি, নাম শুধু offer।
        if (!string.IsNullOrWhiteSpace(serialNo))
            return await BySerialAsync(serialNo, cancellationToken);

        if (!string.IsNullOrWhiteSpace(invoiceNo))
            return await ByInvoiceAsync(invoiceNo, cancellationToken);

        if (!string.IsNullOrWhiteSpace(productName))
            return await ByProductAsync(productName, cancellationToken);

        return Error("Provide at least one of: serialNo, invoiceNo, productName.");
    }

    private async Task<string> BySerialAsync(string serialNo, CancellationToken cancellationToken)
    {
        var sold = await db.SalesOrderSerials
            .Where(s => s.SerialNo == serialNo && s.SalesOrderDetail.SalesOrder.Approved)
            .OrderByDescending(s => s.SalesOrderDetail.SalesOrder.OrderDate)
            .Select(s => new SoldLine
            {
                ProductId = s.ProductId,
                Product = s.Product.Name,
                SoldOn = s.SalesOrderDetail.SalesOrder.OrderDate,
                InvoiceNo = s.SalesOrderDetail.SalesOrder.InvoiceNo,
                SerialNo = s.SerialNo
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (sold is null)
        {
            // serial থাকতে পারে অথচ বিক্রি না হয়ে থাকতে পারে — তখন warranty শুরুই হয়নি,
            // "খুঁজে পাইনি" বলাটা ভুল উত্তর হতো।
            var inStock = await db.CurrentStockDetails
                .Where(d => d.SerialNo == serialNo)
                .Select(d => d.Product.Name)
                .FirstOrDefaultAsync(cancellationToken);

            return Json(new
            {
                serialNo,
                found = false,
                note = inStock is null
                    ? "No such serial number in the ERP database."
                    : $"Serial belongs to '{inStock}' but is still in stock — not sold, so no warranty has started yet."
            });
        }

        return Json(new
        {
            asOf = Today.ToString("yyyy-MM-dd"),
            serialNo,
            found = true,
            items = new[] { await CoverageOfAsync(sold, cancellationToken) }
        });
    }

    private async Task<string> ByInvoiceAsync(string invoiceNo, CancellationToken cancellationToken)
    {
        // Detail থেকে, Serial থেকে নয় — mouse/cable-এর serial নেই, কিন্তু warranty আছে।
        // তাই serial শুধু serialized পণ্যের ক্ষেত্রে জোড়া লাগে (একাধিক হলে গোনা হয়)।
        var sold = await db.SalesOrderDetails
            .Where(d => d.SalesOrder.InvoiceNo == invoiceNo && d.SalesOrder.Approved)
            .Select(d => new SoldLine
            {
                ProductId = d.ProductId,
                Product = d.Product.Name,
                SoldOn = d.SalesOrder.OrderDate,
                InvoiceNo = d.SalesOrder.InvoiceNo,
                Quantity = d.Quantity,
                Serials = d.Serials.Select(s => s.SerialNo).ToList()
            })
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        if (sold.Count == 0)
        {
            return Json(new
            {
                invoiceNo,
                found = false,
                note = "No approved sale with this invoice number. Unapproved orders have no invoice number yet."
            });
        }

        var items = new List<object>(sold.Count);
        foreach (var line in sold)
            items.Add(await CoverageOfAsync(line, cancellationToken));

        return Json(new
        {
            asOf = Today.ToString("yyyy-MM-dd"),
            invoiceNo,
            found = true,
            items
        });
    }

    private async Task<string> ByProductAsync(string productName, CancellationToken cancellationToken)
    {
        // Product location-wise আলাদা row, তাই একই নাম একাধিকবার আসে — warranty দুই branch-এ
        // একই, তাই নাম ধরে একবারই দেখাই।
        var rows = await db.ProductWarranties
            .Where(w => EF.Functions.Like(w.Product.Name, $"%{productName}%"))
            .Select(w => new { Product = w.Product.Name, w.WarrantyType, w.DurationMonths, w.Terms })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            var exists = await db.Products
                .AnyAsync(p => EF.Functions.Like(p.Name, $"%{productName}%"), cancellationToken);

            return Json(new
            {
                productName,
                found = false,
                note = exists
                    ? "This product carries no warranty at all."
                    : "No product matched that name in the ERP database."
            });
        }

        return Json(new
        {
            productName,
            found = true,
            note = "Offered warranty only. Expiry needs a sale — ask again with a serialNo or invoiceNo.",
            products = rows
                .GroupBy(r => r.Product)
                .Take(MaxRows)
                .Select(g => new
                {
                    product = g.Key,
                    warranties = g.Select(w => new
                    {
                        type = w.WarrantyType.ToString(),
                        durationMonths = w.DurationMonths,
                        lifetime = w.DurationMonths is null,
                        terms = w.Terms
                    })
                })
        });
    }

    private async Task<object> CoverageOfAsync(SoldLine sold, CancellationToken cancellationToken)
    {
        var warranties = await db.ProductWarranties
            .Where(w => w.ProductId == sold.ProductId)
            .Select(w => new { w.WarrantyType, w.DurationMonths, w.Terms })
            .ToListAsync(cancellationToken);

        return new
        {
            product = sold.Product,
            serialNo = sold.SerialNo,
            serials = sold.Serials,
            quantity = sold.Quantity,
            invoiceNo = sold.InvoiceNo,
            soldOn = sold.SoldOn.ToString("yyyy-MM-dd"),
            hasWarranty = warranties.Count > 0,
            warranties = warranties.Select(w =>
            {
                // DurationMonths null = আজীবন — কোনো শেষ তারিখ নেই, কখনো expire করে না।
                var expiresOn = w.DurationMonths is null ? null : (DateOnly?)sold.SoldOn.AddMonths(w.DurationMonths.Value);

                return new
                {
                    type = w.WarrantyType.ToString(),
                    durationMonths = w.DurationMonths,
                    lifetime = w.DurationMonths is null,
                    expiresOn = expiresOn?.ToString("yyyy-MM-dd"),
                    isExpired = expiresOn is not null && expiresOn < Today,
                    daysRemaining = expiresOn is null ? null : (int?)Math.Max(0, expiresOn.Value.DayNumber - Today.DayNumber),
                    terms = w.Terms
                };
            })
        };
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string? ReadString(JsonElement arguments, string name)
        => arguments.ValueKind == JsonValueKind.Object
           && arguments.TryGetProperty(name, out var property)
           && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string Error(string message) => Json(new { error = message });

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ⚠️ record নয়, class + object-initializer — positional record EF SQL-এ নামাতে পারে না
    //    (GetStockTool/GetSalesTool-এও একই কারণ)।
    private sealed class SoldLine
    {
        public long ProductId { get; set; }
        public string Product { get; set; } = string.Empty;
        public DateOnly SoldOn { get; set; }
        public string? InvoiceNo { get; set; }

        // serial দিয়ে খুঁজলে একটাই unit; invoice দিয়ে খুঁজলে পুরো line (quantity + সব serial)।
        public string? SerialNo { get; set; }
        public int? Quantity { get; set; }
        public List<string>? Serials { get; set; }
    }
}
