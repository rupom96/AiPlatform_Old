namespace AiPlatform.Domain.Entities.Erp;

// Purchase-এর party। Code = "SUP-{Company.Code}-{Location.Code}-{Year}-{000001}"।
public class Supplier : ErpEntity
{
    public long SupplierId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;   // "Star Tech", "Ryans"
    public string? Phone { get; set; }
    public string? Address { get; set; }

    public long LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
