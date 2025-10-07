using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Repositories;

public class SalesCostRepository : ISalesCostRepository
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SalesCostRepository> _logger;

    public SalesCostRepository(
        TimeProvider timeProvider,
        ILogger<SalesCostRepository> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(List<string>? productCodes = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Calculating sales costs (mocked) from {DateFrom} to {DateTo}", dateFrom, dateTo);

            var result = new Dictionary<string, List<MonthlyCost>>();

            if (productCodes == null || productCodes.Count == 0)
            {
                _logger.LogDebug("No product codes specified for sales costs calculation");
                return result;
            }

            var now = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
            var endDate = dateTo ?? now;
            var startDate = dateFrom ?? endDate.AddMonths(-12);

            // Mock implementation - generate some test data
            foreach (var productCode in productCodes)
            {
                var monthlyCosts = new List<MonthlyCost>();

                for (var date = startDate; date <= endDate; date = date.AddMonths(1))
                {
                    var monthStart = new DateTime(date.Year, date.Month, 1);

                    var salesCost = 10;

                    monthlyCosts.Add(new MonthlyCost(monthStart, salesCost));
                }

                result[productCode] = monthlyCosts;
            }

            _logger.LogInformation("Calculated mock sales costs for {ProductCount} products over {MonthCount} months",
                result.Count, result.Values.FirstOrDefault()?.Count ?? 0);

            await Task.CompletedTask; // Satisfy async contract
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating sales costs");
            throw;
        }
    }
}