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

    // This will be populated as we add features
    // For now, keep it minimal to support existing functionality

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations from all assemblies that contain entity configurations
        // This will be expanded as we add features
    }
}