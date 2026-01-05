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
        // NOTE: Job names MUST match exactly with HangfireJobSchedulerService.cs registration
        var defaultConfigurations = new[]
        {
            new RecurringJobConfiguration(
                "purchase-price-recalculation",
                "Purchase Price Recalculation",
                "Recalculates purchase prices for all materials and products",
                "0 2 * * *", // Daily at 2:00 AM Czech time
                true,
                "System"),

            new RecurringJobConfiguration(
                "product-export-download",
                "Product Export Download",
                "Downloads product export data from external systems",
                "0 2 * * *", // Daily at 2:00 AM Czech time
                true,
                "System"),

            new RecurringJobConfiguration(
                "product-weight-recalculation",
                "Product Weight Recalculation",
                "Recalculates product weights based on current material composition",
                "0 2 * * *", // Daily at 2:00 AM Czech time
                true,
                "System"),

            new RecurringJobConfiguration(
                "invoice-classification",
                "Invoice Classification",
                "Classifies and categorizes incoming invoices",
                "0 * * * *", // Hourly at the top of each hour Czech time
                true,
                "System"),

            new RecurringJobConfiguration(
                "daily-consumption-calculation",
                "Daily Consumption Calculation",
                "Calculates daily consumption of packing materials",
                "0 3 * * *", // Daily at 3:00 AM Czech time
                true,
                "System"),

            new RecurringJobConfiguration(
                "daily-invoice-import-eur",
                "Daily Invoice Import (EUR)",
                "Imports EUR invoices from Shoptet to ABRA Flexi",
                "0 4 * * *", // Daily at 4:00 AM Czech time
                true,
                "System"),

            new RecurringJobConfiguration(
                "daily-invoice-import-czk",
                "Daily Invoice Import (CZK)",
                "Imports CZK invoices from Shoptet to ABRA Flexi",
                "15 4 * * *", // Daily at 4:15 AM Czech time
                true,
                "System"),

            new RecurringJobConfiguration(
                "daily-comgate-czk-import",
                "Daily Comgate CZK Import",
                "Imports Comgate CZK payment statements from previous day",
                "30 4 * * *", // Daily at 4:30 AM Czech time
                true,
                "System"),

            new RecurringJobConfiguration(
                "daily-comgate-eur-import",
                "Daily Comgate EUR Import",
                "Imports Comgate EUR payment statements from previous day",
                "40 4 * * *", // Daily at 4:40 AM Czech time
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
