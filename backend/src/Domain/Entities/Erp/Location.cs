namespace AiPlatform.Domain.Entities.Erp;

// Company-র branch। Code Company-র মধ্যে unique ("HO", "MB", "CB")।
public class Location : ErpEntity
{
    public long LocationId { get; set; }
    public string Code { get; set; } = string.Empty;   // "HO"
    public string Name { get; set; } = string.Empty;   // "Head Office"

    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
