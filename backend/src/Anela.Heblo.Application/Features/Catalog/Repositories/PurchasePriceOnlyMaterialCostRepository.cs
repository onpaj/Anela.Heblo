using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Repositories;

// Loads material cost from manufacture history or purchase price directly
public class PurchasePriceOnlyMaterialCostRepository : IMaterialCostRepository
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogMaterialCostRepository> _logger;

    public PurchasePriceOnlyMaterialCostRepository(
        ICatalogRepository catalogRepository,
        TimeProvider timeProvider,
        ILogger<CatalogMaterialCostRepository> logger)
    {
        _catalogRepository = catalogRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(List<string>? productCodes = null, DateOnly? dateFrom = null, DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IEnumerable<CatalogAggregate> products;
            if (productCodes?.Count == 1)
            {
                var product = await _catalogRepository.GetByIdAsync(productCodes.Single(), cancellationToken);
                products = [product];
            }
            else
            {
                products = await _catalogRepository.GetAllAsync(cancellationToken);
                // Filter by product codes if specified
                if (productCodes != null && productCodes.Count > 0)
                {
                    products = products.Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode)).ToList();
                }
            }

            return await GetCostsAsync(products, dateFrom, dateTo, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating material costs");
            throw;
        }
    }

    private async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(IEnumerable<CatalogAggregate> products,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        try
        {


            var result = new Dictionary<string, List<MonthlyCost>>();
            var now = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);

            // Default date range: last 13 months
            var endDate = dateTo ?? now;
            var startDate = dateFrom ?? endDate.AddMonths(-12);

            _logger.LogDebug("Calculating material costs from {StartDate} to {EndDate} for {ProductCount} products",
                startDate, endDate, products.Count());

            foreach (var product in products)
            {
                if (string.IsNullOrEmpty(product.ProductCode))
                    continue;

                var monthlyCosts = new List<MonthlyCost>();

                // Calculate monthly costs from ManufactureHistory with ERP price as fallback
                for (var date = startDate; date <= endDate; date = date.AddMonths(1))
                {
                    var monthStart = new DateTime(date.Year, date.Month, 1);

                    decimal materialCost;


                    materialCost = product.ErpPrice?.PurchasePrice ?? 0;

                    if (materialCost > 0)
                    {
                        _logger.LogTrace("Using ERP price fallback for {ProductCode} in {Month}: {Cost:F2}",
                            product.ProductCode, monthStart.ToString("yyyy-MM"), materialCost);
                    }

                    monthlyCosts.Add(new MonthlyCost(monthStart, materialCost));
                }

                if (monthlyCosts.Count > 0)
                {
                    result[product.ProductCode] = monthlyCosts;
                }
            }

            _logger.LogDebug("Calculated material costs for {ProductCount} products over {MonthCount} months",
                result.Count, result.Values.FirstOrDefault()?.Count ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating material costs");
            throw;
        }
    }
}