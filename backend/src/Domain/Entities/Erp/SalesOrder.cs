namespace AiPlatform.Domain.Entities.Erp;

// Sales header — approve হলে stock কমে এবং InvoiceNo বসে।
public class SalesOrder : ErpEntity
{
    public long SalesOrderId { get; set; }
    public string SalesOrderNo { get; set; } = string.Empty;   // "SO-APC-HO-2026-000001"

    // approve না হওয়া পর্যন্ত null — approve-এর সময় assign হয়।
    public string? InvoiceNo { get; set; }                     // "INV-APC-HO-2026-000001"

    public long CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public DateOnly OrderDate { get; set; }
    public decimal TotalAmount { get; set; }   // Σ Details.TotalAmount

    public bool Approved { get; set; }
    public long? ApprovedBy { get; set; }
    public ErpUser? ApprovedByUser { get; set; }
    public DateTime? ApprovalDateTime { get; set; }

    public long LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public ICollection<SalesOrderDetail> Details { get; set; } = [];
}
