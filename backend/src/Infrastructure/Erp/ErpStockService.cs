using AiPlatform.Domain.Entities.Erp;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiPlatform.Infrastructure.Erp;

// Order approve হলে stock-এর তিন জায়গা একসাথে বদলায় — ledger (CurrentStock), summary (StockBalance),
// serial (CurrentStockDetail)। তিনটা এক জায়গায় রাখি, নইলে caller ভেদে অসামঞ্জস্য তৈরি হবে।
// ponytail: interface নেই — এক implementation, swap-এর দরকার নেই। লাগলে তখন বের করা যাবে।
public class ErpStockService(ErpDbContext db)
{
    // Purchase approve → stock ঢোকে। আগে approve হয়ে থাকলে কিছুই করে না (idempotent)।
    public async Task ApprovePurchaseAsync(long purchaseOrderId, long approvedBy, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders
            .AsTracking()
            .Include(o => o.Details).ThenInclude(d => d.Serials)
            .FirstOrDefaultAsync(o => o.PurchaseOrderId == purchaseOrderId, ct)
            ?? throw new InvalidOperationException($"PurchaseOrder {purchaseOrderId} not found.");

        if (order.Approved)
            return;

        var serialized = await SerializedProductIdsAsync(order.Details.Select(d => d.ProductId), ct);

        foreach (var detail in order.Details)
        {
            // serialized পণ্যে প্রতিটা unit-এর serial থাকা বাধ্যতামূলক — নইলে গুদামে কী ঢুকল জানা যাবে না।
            if (serialized.Contains(detail.ProductId) && detail.Serials.Count != detail.Quantity)
                throw new InvalidOperationException(
                    $"Product {detail.ProductId} is serialized: expected {detail.Quantity} serials, got {detail.Serials.Count}.");

            var movement = new CurrentStock
            {
                ProductId = detail.ProductId,
                Quantity = detail.Quantity,          // purchase → +
                Cost = detail.UnitPrice,             // কেনার খরচ (valuation-এর ভিত্তি)
                ReferenceNo = order.PurchaseOrderNo,
                LocationId = order.LocationId,
                CompanyId = order.CompanyId,
                CreatedBy = approvedBy
            };
            db.CurrentStocks.Add(movement);

            foreach (var serial in detail.Serials)
            {
                db.CurrentStockDetails.Add(new CurrentStockDetail
                {
                    CurrentStock = movement,         // navigation — FK save-এর সময় EF বসাবে
                    ProductId = detail.ProductId,
                    SerialNo = serial.SerialNo,
                    PurchaseOrderSerialId = serial.PurchaseOrderSerialId,
                    CreatedBy = approvedBy
                });
            }

            AdjustBalance(detail.ProductId, order.LocationId, order.CompanyId, +detail.Quantity, approvedBy);
        }

        order.Approved = true;
        order.ApprovedBy = approvedBy;
        order.ApprovalDateTime = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    // Sales approve → stock কমে + InvoiceNo বসে। stock না থাকলে throw করে, approve হয় না।
    public async Task ApproveSalesAsync(long salesOrderId, long approvedBy, CancellationToken ct = default)
    {
        var order = await db.SalesOrders
            .AsTracking()
            .Include(o => o.Details).ThenInclude(d => d.Serials)
            .FirstOrDefaultAsync(o => o.SalesOrderId == salesOrderId, ct)
            ?? throw new InvalidOperationException($"SalesOrder {salesOrderId} not found.");

        if (order.Approved)
            return;

        foreach (var detail in order.Details)
        {
            var available = await AvailableQuantityAsync(detail.ProductId, order.LocationId, ct);
            if (available < detail.Quantity)
                throw new InvalidOperationException(
                    $"Insufficient stock for product {detail.ProductId}: have {available}, need {detail.Quantity}.");

            var cost = detail.Serials.Count > 0
                ? await ConsumeSerialsAsync(detail, ct)
                : await AverageCostAsync(detail.ProductId, order.LocationId, ct);

            db.CurrentStocks.Add(new CurrentStock
            {
                ProductId = detail.ProductId,
                Quantity = -detail.Quantity,        // sales → −
                Cost = cost,                        // ⚠️ কেনার খরচ, বিক্রয়মূল্য নয় (valuation ঠিক রাখতে)
                ReferenceNo = order.SalesOrderNo,
                LocationId = order.LocationId,
                CompanyId = order.CompanyId,
                CreatedBy = approvedBy
            });

            AdjustBalance(detail.ProductId, order.LocationId, order.CompanyId, -detail.Quantity, approvedBy);
        }

        order.InvoiceNo = await NextInvoiceNoAsync(order, ct);
        order.Approved = true;
        order.ApprovedBy = approvedBy;
        order.ApprovalDateTime = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    // বিক্রি হওয়া serial গুলো গুদাম থেকে সরায়, আর ঐ unit গুলোর আসল কেনার খরচ ফেরত দেয়।
    private async Task<decimal> ConsumeSerialsAsync(SalesOrderDetail detail, CancellationToken ct)
    {
        var serialNos = detail.Serials.Select(s => s.SerialNo).ToList();

        var inStock = await db.CurrentStockDetails
            .AsTracking()
            .Include(x => x.CurrentStock)
            .Where(x => x.ProductId == detail.ProductId && serialNos.Contains(x.SerialNo))
            .ToListAsync(ct);

        if (inStock.Count != serialNos.Count)
        {
            var missing = serialNos.Except(inStock.Select(x => x.SerialNo));
            throw new InvalidOperationException($"Serial(s) not in stock: {string.Join(", ", missing)}.");
        }

        // serial ধরে ধরে হিসাব — একই পণ্য ভিন্ন দামে কেনা থাকলেও হুবহু সঠিক খরচ।
        var cost = inStock.Sum(x => x.CurrentStock.Cost) / inStock.Count;
        db.CurrentStockDetails.RemoveRange(inStock);
        return cost;
    }

    // serial ছাড়া পণ্যে কোন unit কোন চালানের বোঝার উপায় নেই → weighted average cost।
    private async Task<decimal> AverageCostAsync(long productId, long locationId, CancellationToken ct)
    {
        var purchases = await db.CurrentStocks
            .Where(s => s.ProductId == productId && s.LocationId == locationId && s.Quantity > 0)
            .Select(s => new { s.Quantity, s.Cost })
            .ToListAsync(ct);

        var totalQuantity = purchases.Sum(p => p.Quantity);
        return totalQuantity == 0 ? 0m : purchases.Sum(p => p.Quantity * p.Cost) / totalQuantity;
    }

    private async Task<int> AvailableQuantityAsync(long productId, long locationId, CancellationToken ct)
    {
        var balance = FindLocalBalance(productId, locationId)
            ?? await db.StockBalances.AsTracking()
                .FirstOrDefaultAsync(b => b.ProductId == productId && b.LocationId == locationId, ct);

        return balance?.QuantityOnHand ?? 0;
    }

    private void AdjustBalance(long productId, long locationId, long companyId, int delta, long userId)
    {
        var balance = FindLocalBalance(productId, locationId)
            ?? db.StockBalances.AsTracking()
                .FirstOrDefault(b => b.ProductId == productId && b.LocationId == locationId);

        if (balance is null)
        {
            balance = new StockBalance
            {
                ProductId = productId,
                LocationId = locationId,
                CompanyId = companyId,
                CreatedBy = userId
            };
            db.StockBalances.Add(balance);
        }

        balance.QuantityOnHand += delta;
        balance.LastUpdated = DateTime.UtcNow;
    }

    // এক order-এ একই product দুই line-এ থাকতে পারে। DB query তখনো unsaved row দেখে না,
    // তাই আগে ChangeTracker-এর local cache দেখি — নইলে দ্বিতীয় line প্রথমটার হিসাব মুছে দিত।
    private StockBalance? FindLocalBalance(long productId, long locationId)
        => db.StockBalances.Local.FirstOrDefault(b => b.ProductId == productId && b.LocationId == locationId);

    private async Task<HashSet<long>> SerializedProductIdsAsync(IEnumerable<long> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        var serialized = await db.Products
            .Where(p => ids.Contains(p.ProductId) && p.IsSerialized)
            .Select(p => p.ProductId)
            .ToListAsync(ct);

        return [.. serialized];
    }

    // "INV-{Company}-{Location}-{Year}-{000001}" — Company+Location+বছর ভিত্তিক ক্রমিক।
    // ponytail: count-ভিত্তিক, তাই একসাথে দুটো approve চললে সংঘর্ষ হতে পারে।
    // concurrent approval দরকার হলে sequence table (বা DB SEQUENCE) নাও।
    private async Task<string> NextInvoiceNoAsync(SalesOrder order, CancellationToken ct)
    {
        var companyCode = await db.Companies
            .Where(c => c.CompanyId == order.CompanyId).Select(c => c.Code).FirstAsync(ct);
        var locationCode = await db.Locations
            .Where(l => l.LocationId == order.LocationId).Select(l => l.Code).FirstAsync(ct);

        var prefix = $"INV-{companyCode}-{locationCode}-{order.OrderDate.Year}-";

        var used = await db.SalesOrders.CountAsync(
            s => s.InvoiceNo != null && s.InvoiceNo.StartsWith(prefix), ct);

        return $"{prefix}{used + 1:D6}";
    }
}
