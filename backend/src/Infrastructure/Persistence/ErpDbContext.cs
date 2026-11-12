using AiPlatform.Domain.Entities.Erp;
using Microsoft.EntityFrameworkCore;

namespace AiPlatform.Infrastructure.Persistence;

// Sample ERP database — AppDbContext থেকে সম্পূর্ণ আলাদা (নিজস্ব connection string + database)।
// আলাদা রাখার কারণ: পরে এই sample সরিয়ে আসল ERP DB বসানো যাবে, auth/app data ছোঁয়া লাগবে না।
public class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<ErpUser> ErpUsers => Set<ErpUser>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductWarranty> ProductWarranties => Set<ProductWarranty>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderDetail> PurchaseOrderDetails => Set<PurchaseOrderDetail>();
    public DbSet<PurchaseOrderSerial> PurchaseOrderSerials => Set<PurchaseOrderSerial>();

    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderDetail> SalesOrderDetails => Set<SalesOrderDetail>();
    public DbSet<SalesOrderSerial> SalesOrderSerials => Set<SalesOrderSerial>();

    public DbSet<CurrentStock> CurrentStocks => Set<CurrentStock>();
    public DbSet<CurrentStockDetail> CurrentStockDetails => Set<CurrentStockDetail>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Company>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Address).HasMaxLength(200);
            e.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<Location>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(2).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        });

        builder.Entity<ErpUser>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Username).HasMaxLength(100).IsRequired();
            e.Property(x => x.Designation).HasMaxLength(100);
            e.HasIndex(x => new { x.CompanyId, x.LocationId, x.Code }).IsUnique();
            e.HasIndex(x => x.Username).IsUnique();
        });

        builder.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Category).HasMaxLength(50).IsRequired();
            e.Property(x => x.Brand).HasMaxLength(50).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(20).IsRequired();
            // Product location-wise আলাদা row — এই index-ই সেই নিয়ম রক্ষা করে।
            e.HasIndex(x => new { x.CompanyId, x.LocationId, x.Name }).IsUnique();
        });

        builder.Entity<ProductWarranty>(e =>
        {
            // এক product-এ এক ধরনের warranty একবারই।
            e.HasIndex(x => new { x.ProductId, x.WarrantyType }).IsUnique();
            // enum int নয় — DB পড়ে বোঝা যায়, আর tool-এর JSON output-এও নাম যায়।
            e.Property(x => x.WarrantyType).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Terms).HasMaxLength(500);
        });

        builder.Entity<Supplier>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Address).HasMaxLength(250);
            e.HasIndex(x => new { x.CompanyId, x.LocationId, x.Code }).IsUnique();
        });

        builder.Entity<Customer>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Address).HasMaxLength(250);
            e.HasIndex(x => new { x.CompanyId, x.LocationId, x.Code }).IsUnique();
        });

        builder.Entity<PurchaseOrder>(e =>
        {
            e.Property(x => x.PurchaseOrderNo).HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.CompanyId, x.LocationId, x.PurchaseOrderNo }).IsUnique();
            e.HasIndex(x => x.OrderDate);   // "জুলাই মাসের কেনা" — date-range query
        });

        builder.Entity<PurchaseOrderSerial>(e =>
        {
            e.Property(x => x.SerialNo).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.SerialNo);
        });

        builder.Entity<SalesOrder>(e =>
        {
            e.Property(x => x.SalesOrderNo).HasMaxLength(30).IsRequired();
            e.Property(x => x.InvoiceNo).HasMaxLength(30);
            e.HasIndex(x => new { x.CompanyId, x.LocationId, x.SalesOrderNo }).IsUnique();
            // InvoiceNo approve-এর আগে NULL। SQL Server unique index একাধিক NULL-কে duplicate ধরে,
            // তাই filtered index — শুধু assign হওয়া গুলোর মধ্যে unique।
            e.HasIndex(x => x.InvoiceNo).IsUnique().HasFilter("[InvoiceNo] IS NOT NULL");
            e.HasIndex(x => x.OrderDate);   // "জুলাই মাসের বিক্রি" — মূল AI query
        });

        builder.Entity<SalesOrderSerial>(e =>
        {
            e.Property(x => x.SerialNo).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.SerialNo);
        });

        builder.Entity<CurrentStock>(e =>
        {
            e.Property(x => x.ReferenceNo).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.CompanyId, x.LocationId, x.ProductId });
            e.HasIndex(x => x.ReferenceNo);
        });

        builder.Entity<CurrentStockDetail>(e =>
        {
            e.Property(x => x.SerialNo).HasMaxLength(100).IsRequired();
            // গুদামে এক serial একবারই থাকতে পারে।
            e.HasIndex(x => new { x.ProductId, x.SerialNo }).IsUnique();
        });

        builder.Entity<StockBalance>(e =>
        {
            // এক Product+Location = এক row (summary-র মূল নিয়ম)।
            e.HasIndex(x => new { x.ProductId, x.LocationId }).IsUnique();
        });

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            // টাকা: SQL Server default decimal(18,2)-এ silently truncate করে — স্পষ্ট করে দিই।
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(decimal)))
                property.SetPrecision(18);
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(decimal)))
                property.SetScale(2);

            // ponytail: সব FK-তে Restrict। Company/Location-এ বহু table থেকে FK যায়, cascade রাখলে
            // SQL Server "multiple cascade paths" error দেয়। ERP-তে master delete করার কথাও নয়
            // (ledger ইতিহাস)। order-এর সাথে detail মুছতে হলে হাতে মোছো।
            foreach (var fk in entity.GetForeignKeys())
                fk.DeleteBehavior = DeleteBehavior.Restrict;

            // "কে করল" খোঁজা audit query-র জন্য।
            if (entity.FindProperty(nameof(ErpEntity.CreatedBy)) is not null)
                entity.AddIndex(entity.FindProperty(nameof(ErpEntity.CreatedBy))!);
        }
    }
}
