using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Repositories;

public class DirectManufactureCostSource : IDirectManufactureCostSource
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DirectManufactureCostSource> _logger;

    public DirectManufactureCostSource(
        TimeProvider timeProvider,
        ILogger<DirectManufactureCostSource> logger)
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
        // STUB IMPLEMENTATION - Returns constant value of 15
        // TODO: Implement actual direct manufacturing cost calculation based on:
        //  - ILedgerService.GetDirectCosts(department: "VYROBA") per month
        //  - ManufactureHistory per month with historical ManufactureDifficulty
        //  - Monthly cost allocation proportional to weighted production

        var result = new Dictionary<string, List<MonthlyCost>>();

        if (productCodes == null || productCodes.Count == 0)
            return result;

        // Default to last 12 months if not specified
        var now = _timeProvider.GetUtcNow().DateTime;
        var effectiveDateFrom = dateFrom ?? DateOnly.FromDateTime(now.AddMonths(-12));
        var effectiveDateTo = dateTo ?? DateOnly.FromDateTime(now);

        // Generate months
        var months = GenerateMonths(effectiveDateFrom, effectiveDateTo);

        // Generate stub costs (constant 15)
        foreach (var productCode in productCodes)
        {
            result[productCode] = months.Select(month => new MonthlyCost(month, 15m)).ToList();
        }

        return await Task.FromResult(result);
    }

    private static List<DateTime> GenerateMonths(DateOnly dateFrom, DateOnly dateTo)
    {
        var months = new List<DateTime>();
        var current = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var end = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (current <= end)
        {
            months.Add(current);
            current = current.AddMonths(1);
        }

        return months;
    }
}
