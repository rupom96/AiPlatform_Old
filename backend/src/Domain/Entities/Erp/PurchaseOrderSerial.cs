namespace AiPlatform.Domain.Entities.Erp;

// কেনা প্রতিটা unit-এর serial (এক unit = এক row)। শুধু IsSerialized product-এ থাকে।
public class PurchaseOrderSerial : ErpEntity
{
    public long PurchaseOrderSerialId { get; set; }

    public long PurchaseOrderDetailId { get; set; }
    public PurchaseOrderDetail PurchaseOrderDetail { get; set; } = null!;

    // denormalized — serial দিয়ে product খুঁজতে detail-এ join লাগে না।
    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string SerialNo { get; set; } = string.Empty;
}
