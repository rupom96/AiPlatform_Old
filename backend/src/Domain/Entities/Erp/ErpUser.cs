namespace AiPlatform.Domain.Entities.Erp;

// ERP-র কর্মী — শুধু "কে করল" বোঝানোর reference (ApprovedBy / CreatedBy)।
// ⚠️ এখানে password/authentication নেই — AiPlatform-এর নিজের Identity আলাদা।
// AppUser-এর সাথে link পরে আসবে (AppUser.ErpUserId, write tool-এর সময়) — erp-integration-plan.md দেখো।
public class ErpUser : ErpEntity
{
    public long ErpUserId { get; set; }
    public string Code { get; set; } = string.Empty;       // "USR-APC-HO-2026-000001"
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Designation { get; set; }               // "Manager", "Sales Officer"
    public bool IsActive { get; set; } = true;

    public long LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
