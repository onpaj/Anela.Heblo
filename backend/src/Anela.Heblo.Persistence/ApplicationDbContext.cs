using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Purchase;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence;

/// <summary>
/// Main application database context
/// Phase 1: Single DbContext for all modules
/// Phase 2: Will be split into module-specific contexts
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    //public DbSet<ScheduledTask> Tasks { get; set; }
    public DbSet<IssuedInvoice> IssuedInvoices { get; set; } = null!;
    public DbSet<BankStatementImport> BankStatements { get; set; } = null!;
    //public DbSet<RecurringJob> Jobs { get; set; }
    public DbSet<TransportBox> TransportBoxes { get; set; }
    public DbSet<StockTakingRecord> StockTakings { get; set; }
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
    public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; } = null!;
    public DbSet<PurchaseOrderHistory> PurchaseOrderHistory { get; set; } = null!;

    // Catalog module
    public DbSet<ManufactureDifficultySetting> ManufactureDifficultySettings { get; set; } = null!;

    // Journal module
    public DbSet<JournalEntry> JournalEntries { get; set; } = null!;
    public DbSet<JournalEntryProduct> JournalEntryProducts { get; set; } = null!;
    public DbSet<JournalEntryTag> JournalEntryTags { get; set; } = null!;
    public DbSet<JournalEntryTagAssignment> JournalEntryTagAssignments { get; set; } = null!;

    // Gift Package Manufacturing module
    public DbSet<GiftPackageManufactureLog> GiftPackageManufactureLogs { get; set; } = null!;
    public DbSet<GiftPackageManufactureItem> GiftPackageManufactureItems { get; set; } = null!;

    // Manufacture Order Management module
    public DbSet<ManufactureOrder> ManufactureOrders { get; set; } = null!;
    public DbSet<ManufactureOrderSemiProduct> ManufactureOrderSemiProducts { get; set; } = null!;
    public DbSet<ManufactureOrderProduct> ManufactureOrderProducts { get; set; } = null!;
    public DbSet<ManufactureOrderNote> ManufactureOrderNotes { get; set; } = null!;
    public DbSet<ManufactureOrderAuditLog> ManufactureOrderAuditLogs { get; set; } = null!;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations from current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Handle DateTime conversion for PostgreSQL "timestamp without time zone"
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    // Convert UTC to Unspecified for PostgreSQL, and Unspecified to UTC when reading
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                            v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified), // Convert to Unspecified for PostgreSQL
                            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)          // Treat as UTC when reading
                        ));
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
                            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Unspecified) : null, // Convert to Unspecified for PostgreSQL
                            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null          // Treat as UTC when reading
                        ));
                    }
                }
            }
        }

        //modelBuilder.ConfigureScheduledTasks();
        //modelBuilder.ConfigureIssuedInvoices();
        //modelBuilder.ConfigureRecurringJobs();
        //modelBuilder.ConfigureTransportBoxes();
        //modelBuilder.ConfigureStockTaking();
        //modelBuilder.ConfigureBankStatements();
    }
}