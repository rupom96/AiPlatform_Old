namespace AiPlatform.Domain.Entities.Erp;

// Stock LEDGER — প্রতিটা approved movement-এ এক row (purchase = +, sales = −)। কখনো update হয় না।
// চলতি ব্যালেন্স StockBalance-এ; এই table ইতিহাস ও valuation-এর জন্য।
public class CurrentStock : ErpEntity
{
    public long CurrentStockId { get; set; }

    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }   // purchase → +qty, sales → −qty

    // ⚠️ সবসময় *কেনার* খরচ (বিক্রয়মূল্য নয়) — এই column stock valuation-এর জন্য।
    // বিক্রয়মূল্য SalesOrderDetail.UnitPrice-এ; লাভ = দুইয়ের পার্থক্য।
    public decimal Cost { get; set; }

    // কোন order-এর কারণে এই movement — PurchaseOrderNo বা SalesOrderNo।
    public string ReferenceNo { get; set; } = string.Empty;

    public long LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    // শুধু purchase movement-এ থাকে (কেনা serial গুলো গুদামে ঢুকল)।
    public ICollection<CurrentStockDetail> Serials { get; set; } = [];
}
