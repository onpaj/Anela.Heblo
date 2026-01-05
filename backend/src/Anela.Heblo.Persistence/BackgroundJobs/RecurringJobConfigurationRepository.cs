using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.BackgroundJobs;

public class RecurringJobConfigurationRepository : IRecurringJobConfigurationRepository
{
    private readonly ApplicationDbContext _context;

    public RecurringJobConfigurationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.RecurringJobConfigurations
            .OrderBy(c => c.JobName)
            .ToListAsync(cancellationToken);
    }

    public async Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringJobConfigurations
            .FirstOrDefaultAsync(c => c.JobName == jobName, cancellationToken);
    }

    public async Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _context.RecurringJobConfigurations.Update(configuration);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SeedDefaultConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        var defaultConfigurations = new[]
        {
            new RecurringJobConfiguration(
                "SyncIssuedInvoices",
                "Sync Issued Invoices",
                "Synchronizes issued invoices from Shoptet to ABRA Flexi",
                "0 */6 * * *", // Every 6 hours
                true,
                "System"),

            new RecurringJobConfiguration(
                "SyncCatalogFromAbra",
                "Sync Catalog from ABRA",
                "Synchronizes product catalog from ABRA Flexi",
                "0 2 * * *", // Daily at 2 AM
                true,
                "System"),

            new RecurringJobConfiguration(
                "SyncCatalogFromShoptet",
                "Sync Catalog from Shoptet",
                "Synchronizes product catalog from Shoptet",
                "0 3 * * *", // Daily at 3 AM
                true,
                "System"),

            new RecurringJobConfiguration(
                "UpdateProductManufactureDifficulty",
                "Update Product Manufacture Difficulty",
                "Recalculates and updates manufacture difficulty ratings for all products",
                "0 4 * * 0", // Weekly on Sunday at 4 AM
                true,
                "System"),

            new RecurringJobConfiguration(
                "SyncShopOrders",
                "Sync Shop Orders",
                "Synchronizes new orders from Shoptet",
                "*/15 * * * *", // Every 15 minutes
                true,
                "System"),

            new RecurringJobConfiguration(
                "ProcessReceivedShipments",
                "Process Received Shipments",
                "Processes received shipments and updates inventory",
                "0 8-18 * * 1-5", // Every hour from 8 AM to 6 PM on weekdays
                true,
                "System"),

            new RecurringJobConfiguration(
                "UpdateStockOnEshops",
                "Update Stock on E-shops",
                "Updates stock levels on all connected e-shop platforms",
                "0 */4 * * *", // Every 4 hours
                true,
                "System"),

            new RecurringJobConfiguration(
                "ManufactureGiftPackages",
                "Manufacture Gift Packages",
                "Processes gift package manufacturing orders",
                "0 10 * * 1-5", // Weekdays at 10 AM
                true,
                "System"),

            new RecurringJobConfiguration(
                "CleanupOldLogs",
                "Cleanup Old Logs",
                "Removes log entries older than retention period",
                "0 1 * * 0", // Weekly on Sunday at 1 AM
                true,
                "System")
        };

        foreach (var config in defaultConfigurations)
        {
            var existing = await GetByJobNameAsync(config.JobName, cancellationToken);
            if (existing == null)
            {
                await _context.RecurringJobConfigurations.AddAsync(config, cancellationToken);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
