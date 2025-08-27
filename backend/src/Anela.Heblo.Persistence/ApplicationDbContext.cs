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
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
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
            var now = DateTime.Now; // Use local time for PostgreSQL timestamp without time zone

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

        //modelBuilder.ConfigureScheduledTasks();
        //modelBuilder.ConfigureIssuedInvoices();
        //modelBuilder.ConfigureRecurringJobs();
        //modelBuilder.ConfigureTransportBoxes();
        //modelBuilder.ConfigureStockTaking();
        //modelBuilder.ConfigureBankStatements();
    }
}