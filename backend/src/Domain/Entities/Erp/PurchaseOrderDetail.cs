namespace AiPlatform.Domain.Entities.Erp;

// Purchase-এর এক line — কোন product, কতটা, কত দামে।
// tenancy (Company/Location) header থেকে আসে, এখানে আলাদা রাখি না।
public class PurchaseOrderDetail : ErpEntity
{
    public long PurchaseOrderDetailId { get; set; }

    public long PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }   // Quantity × UnitPrice

    public ICollection<PurchaseOrderSerial> Serials { get; set; } = [];
}
