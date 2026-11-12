namespace AiPlatform.Domain.Entities.Erp;

// Stock SUMMARY — Product+Location wise চলতি ব্যালেন্স (এক জোড়া = এক row)।
// "কতটা আছে" জানতে ledger যোগ করা লাগে না, এক row পড়লেই হয়।
// invariant: QuantityOnHand == SUM(CurrentStock.Quantity) একই Product+Location-এ।
public class StockBalance : ErpEntity
{
    public long StockBalanceId { get; set; }

    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public long LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int QuantityOnHand { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
