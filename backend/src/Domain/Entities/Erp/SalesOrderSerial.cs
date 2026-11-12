namespace AiPlatform.Domain.Entities.Erp;

// বিক্রি হওয়া প্রতিটা unit-এর serial। approve-এ এই serial গুলো CurrentStockDetail থেকে মুছে যায়।
public class SalesOrderSerial : ErpEntity
{
    public long SalesOrderSerialId { get; set; }

    public long SalesOrderDetailId { get; set; }
    public SalesOrderDetail SalesOrderDetail { get; set; } = null!;

    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string SerialNo { get; set; } = string.Empty;
}
