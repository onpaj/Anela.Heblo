using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Repositories;

public class OverheadCostRepository : IOverheadCostRepository
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OverheadCostRepository> _logger;

    public OverheadCostRepository(
        TimeProvider timeProvider,
        ILogger<OverheadCostRepository> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
        List<string>? productCodes = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Calculating total costs from {DateFrom} to {DateTo}", dateFrom, dateTo);

            var result = new Dictionary<string, List<MonthlyCost>>();

            if (productCodes == null || productCodes.Count == 0)
            {
                _logger.LogDebug("No product codes specified for total costs calculation");
                return result;
            }

            var now = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
            var endDate = dateTo ?? now;
            var startDate = dateFrom ?? endDate.AddMonths(-12);

            // Add mock overhead costs (e.g., general administration, utilities, etc.)
            var overheadCostPerMonth = 15; // Mock overhead cost per product per month

            foreach (var productCode in productCodes)
            {
                var monthlyCosts = new List<MonthlyCost>();

                for (var date = startDate; date <= endDate; date = date.AddMonths(1))
                {
                    var monthStart = new DateTime(date.Year, date.Month, 1);

                    decimal totalCost = 0;
                    // Add overhead costs
                    totalCost += overheadCostPerMonth;

                    monthlyCosts.Add(new MonthlyCost(monthStart, totalCost));
                }

                if (monthlyCosts.Count > 0)
                {
                    result[productCode] = monthlyCosts;
                }
            }

            _logger.LogDebug("Calculated total costs for {ProductCount} products over {MonthCount} months",
                result.Count, result.Values.FirstOrDefault()?.Count ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating total costs");
            throw;
        }
    }
}