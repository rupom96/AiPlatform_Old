namespace AiPlatform.Domain.Entities.Erp;

// IT পণ্য (computer / laptop / accessories)।
// ⚠️ Product location-wise আলাদা row — একই laptop HO আর CB-তে দুই ProductId।
public class Product : ErpEntity
{
    public long ProductId { get; set; }
    public string Name { get; set; } = string.Empty;      // "Dell XPS 13"
    public string Category { get; set; } = string.Empty;  // Computer / Laptop / Accessory
    public string Brand { get; set; } = string.Empty;     // Dell, HP, Logitech
    public string Unit { get; set; } = "Pcs";             // unit of measure

    // true হলে প্রতি unit-এর serial track হয় (laptop/PC); false হলে শুধু quantity (cable/mouse)।
    public bool IsSerialized { get; set; }

    public long LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public long CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
