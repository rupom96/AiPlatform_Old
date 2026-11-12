namespace AiPlatform.Domain.Entities.Erp;

// ERP tenant root — এক Company-র নিচে একাধিক Location (branch)।
public class Company : ErpEntity
{
    public long CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;   // "APC"
    public string Name { get; set; } = string.Empty;   // "Ai Platform Company"
    public string? Address { get; set; }
    public DateTime OpeningDate { get; set; }

    public ICollection<Location> Locations { get; set; } = [];
}
