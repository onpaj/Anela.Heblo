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

    public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
    public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; } = null!;
    public DbSet<PurchaseOrderHistory> PurchaseOrderHistory { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations from current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}