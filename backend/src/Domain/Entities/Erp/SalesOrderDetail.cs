namespace AiPlatform.Domain.Entities.Erp;

// Sales-এর এক line। UnitPrice = বিক্রয়মূল্য (কেনার খরচ CurrentStock.Cost-এ)।
public class SalesOrderDetail : ErpEntity
{
    public long SalesOrderDetailId { get; set; }

    public long SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;

    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }   // Quantity × UnitPrice

    public ICollection<SalesOrderSerial> Serials { get; set; } = [];
}
