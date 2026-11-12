namespace AiPlatform.Domain.Entities.Erp;

// Purchase header — approve হলে stock ঢোকে (CurrentStock + StockBalance)।
public class PurchaseOrder : ErpEntity
{
    public long PurchaseOrderId { get; set; }
    public string PurchaseOrderNo { get; set; } = string.Empty;   // "PO-APC-HO-2026-000001"

    public long SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public DateOnly OrderDate { get; set; }
    public decimal TotalAmount { get; set; }   // Σ Details.TotalAmount

    // approve করার আগে stock-এ কোনো প্রভাব নেই।
    public bool Approved { get; set; }
    public long? ApprovedBy { get; set; }
    public ErpUser? ApprovedByUser { get; set; }
    public DateTime? ApprovalDateTime { get; set; }

    public long LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public ICollection<PurchaseOrderDetail> Details { get; set; } = [];
}
