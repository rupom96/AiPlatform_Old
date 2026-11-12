namespace AiPlatform.Domain.Entities.Erp;

// Sales-এর party। Code = "CUS-{Company.Code}-{Location.Code}-{Year}-{000001}"।
public class Customer : ErpEntity
{
    public long CustomerId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }

    public long LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
