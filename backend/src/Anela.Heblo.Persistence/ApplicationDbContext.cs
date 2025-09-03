using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Logistics.Transport;
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
    private readonly TimeProvider _timeProvider;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, TimeProvider timeProvider) : base(options)
    {
        _timeProvider = timeProvider;
    }

    //public DbSet<ScheduledTask> Tasks { get; set; }
    //public DbSet<IssuedInvoice> IssuedInvoices { get; set; }
    //public DbSet<BankStatementImport> BankStatements { get; set; }
    //public DbSet<RecurringJob> Jobs { get; set; }
    public DbSet<TransportBox> TransportBoxes { get; set; }
    //public DbSet<StockTakingResult> StockTakings { get; set; }
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

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Update ConcurrencyStamp for TransportBox entities
        var transportBoxEntries = ChangeTracker.Entries<TransportBox>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in transportBoxEntries)
        {
            var now = DateTime.SpecifyKind(_timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Utc); // Use TimeProvider consistently

            entry.Entity.ConcurrencyStamp = Guid.NewGuid().ToString("N")[..32]; // 32 chars to fit in varchar(40)

            // Set ExtraProperties to empty JSON object if null
            if (string.IsNullOrEmpty(entry.Entity.ExtraProperties))
            {
                entry.Entity.ExtraProperties = "{}";
            }

            // Set CreationTime only when adding new entity
            if (entry.State == EntityState.Added && entry.Entity.CreationTime == default)
            {
                entry.Entity.CreationTime = now;
            }

            // Set LastModificationTime for both insert and update
            entry.Entity.LastModificationTime = now;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations from current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        
        // Ensure all DateTimes are treated as UTC for PostgreSQL compatibility
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    // Ensure DateTimes are UTC when reading from/writing to database
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                            v => DateTime.SpecifyKind(v, DateTimeKind.Utc), // Ensure UTC before saving
                            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)  // Ensure UTC when reading
                        ));
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
                            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null, // Ensure UTC before saving
                            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null  // Ensure UTC when reading
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