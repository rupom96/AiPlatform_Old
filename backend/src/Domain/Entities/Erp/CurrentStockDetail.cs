namespace AiPlatform.Domain.Entities.Erp;

// এই মুহূর্তে গুদামে থাকা প্রতিটা serial — এক row = এক unit হাতে আছে।
// বিক্রি approve হলে row মুছে যায়, তাই এই table সবসময় "এখন যা আছে" দেখায়।
public class CurrentStockDetail : ErpEntity
{
    public long CurrentStockDetailId { get; set; }

    // যে purchase movement-এ ঢুকেছিল (এর মাধ্যমেই ঐ unit-এর আসল Cost জানা যায়)।
    public long CurrentStockId { get; set; }
    public CurrentStock CurrentStock { get; set; } = null!;

    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string SerialNo { get; set; } = string.Empty;

    public long PurchaseOrderSerialId { get; set; }
    public PurchaseOrderSerial PurchaseOrderSerial { get; set; } = null!;

    public DateTime InDate { get; set; } = DateTime.UtcNow;
}
