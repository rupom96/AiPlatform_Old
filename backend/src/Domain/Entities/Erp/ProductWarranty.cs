namespace AiPlatform.Domain.Entities.Erp;

// Product-এর সাথে কী warranty আসে (offer স্তর) — কোনো নির্দিষ্ট বিক্রীত unit-এর নয়।
// ⚠️ Product location-wise আলাদা row, তাই একই laptop-এর warranty HO আর CB-তে দুইবার বসবে
//    (Product convention মেনে — docs/erp-integration-plan.md §Schema)।
//
// ponytail: WarrantyRegistration (per-serial start/end) ও WarrantyClaim table বাদ।
// warranty শুরু ধরা হয় SalesOrder.InvoiceDate থেকে, তাই মেয়াদ query-time-এ হিসাব হয় —
// আলাদা row রাখলে সেটা duplicate data, invoice date বদলালে stale। Extended warranty বিক্রি
// হলে, installation date থেকে warranty শুরু হলে, বা claim history লাগলে তখন ওই দুই table যোগ করো।
public class ProductWarranty : ErpEntity
{
    public long ProductWarrantyId { get; set; }

    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public WarrantyType WarrantyType { get; set; }

    // null = আজীবন (WarrantyType.Lifetime)। কোনো row না থাকা = ঐ ধরনের warranty নেই।
    public int? DurationMonths { get; set; }

    public string? Terms { get; set; }   // "physical damage ও liquid damage বাদ"
}
