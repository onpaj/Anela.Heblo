using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;

public class GetProductStatisticsHandler
    : IRequestHandler<GetProductStatisticsRequest, GetProductStatisticsResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<GetProductStatisticsHandler> _logger;

    public GetProductStatisticsHandler(
        ICatalogRepository catalogRepository,
        ILogger<GetProductStatisticsHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<GetProductStatisticsResponse> Handle(
        GetProductStatisticsRequest request,
        CancellationToken cancellationToken)
    {
        var months = MonthRange.Expand(request.DateFrom, request.DateTo);

        var catalogItems = await _catalogRepository.GetByIdsAsync(
            request.ProductCodes,
            cancellationToken);

        var series = new List<ProductStatisticsSeriesDto>();

        // Iterate the requested codes, not the dictionary, so series order is the caller's
        // order — that is what keeps chart colors stable as products are added and removed.
        foreach (var productCode in request.ProductCodes)
        {
            if (!catalogItems.TryGetValue(productCode, out var item))
            {
                _logger.LogDebug(
                    "Product {ProductCode} not found in catalog cache, skipping from statistics",
                    productCode);
                continue;
            }

            series.Add(new ProductStatisticsSeriesDto
            {
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                Values = ProjectMetric(item, request.Metric, months)
            });
        }

        return new GetProductStatisticsResponse
        {
            Months = months,
            Products = series
        };
    }

    private static List<double> ProjectMetric(
        CatalogAggregate item,
        ProductStatisticsMetric metric,
        List<string> months)
    {
        var byMonth = BuildMonthLookup(item, metric, months);

        // Dense output: a month with no data is 0, not a gap. Frontend never handles nulls.
        return months
            .Select(month => byMonth.TryGetValue(month, out var value) ? value : 0d)
            .ToList();
    }

    private static Dictionary<string, double> BuildMonthLookup(
        CatalogAggregate item,
        ProductStatisticsMetric metric,
        List<string> months)
    {
        switch (metric)
        {
            case ProductStatisticsMetric.Sales:
                return item.SaleHistorySummary.MonthlyData
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TotalAmount);

            case ProductStatisticsMetric.Purchase:
                return item.PurchaseHistorySummary.MonthlyData
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TotalAmount);

            case ProductStatisticsMetric.Consumption:
                return item.ConsumedHistorySummary.MonthlyData
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TotalAmount);

            case ProductStatisticsMetric.Manufacture:
                // The only metric without a pre-aggregated summary on the aggregate, so it is
                // aggregated per request. Records outside the requested window are dropped before
                // grouping: ProjectMetric only ever reads the requested months, and history runs
                // for years, so grouping all of it would allocate a dictionary mostly thrown away.
                var requestedMonths = months.ToHashSet(StringComparer.Ordinal);
                return item.ManufactureHistory
                    .Select(record => new { Month = MonthRange.Key(record.Date), record.Amount })
                    .Where(record => requestedMonths.Contains(record.Month))
                    .GroupBy(record => record.Month)
                    .ToDictionary(group => group.Key, group => group.Sum(record => record.Amount));

            default:
                return new Dictionary<string, double>();
        }
    }
}
