namespace AiPlatform.Domain.Entities.Erp;

// প্রতিটা ERP table-এ যে দুটো column থাকে (audit)।
// CreatedBy = ErpUser.ErpUserId, কিন্তু FK নয় — ১৫টা table থেকে ErpUser-এ FK দিলে
// SQL Server "multiple cascade paths" error দেয়। plain column + index-ই যথেষ্ট।
public abstract class ErpEntity
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }
}
