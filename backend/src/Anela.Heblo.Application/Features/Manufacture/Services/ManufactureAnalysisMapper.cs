using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ManufactureAnalysisMapper : IManufactureAnalysisMapper
{
    private readonly ManufactureAnalysisOptions _options;
    private readonly ILogger<ManufactureAnalysisMapper> _logger;

    public ManufactureAnalysisMapper(IOptions<ManufactureAnalysisOptions> options, ILogger<ManufactureAnalysisMapper> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public ManufacturingStockItemDto MapToDto(
        CatalogAggregate catalogItem,
        ManufacturingStockSeverity severity,
        double dailySalesRate,
        double salesInPeriod,
        double stockDaysAvailable,
        double overstockPercentage,
        bool isInProduction)
    {
        var dto = new ManufacturingStockItemDto
        {
            Code = catalogItem.ProductCode,
            Name = catalogItem.ProductName,
            CurrentStock = (double)catalogItem.Stock.Available,
            Reserve = (double)catalogItem.Stock.Reserve,
            SalesInPeriod = salesInPeriod,
            DailySalesRate = dailySalesRate,
            OptimalDaysSetup = catalogItem.Properties.OptimalStockDaysSetup,
            StockDaysAvailable = double.IsInfinity(stockDaysAvailable) ? _options.InfiniteStockIndicator : stockDaysAvailable,
            MinimumStock = (double)catalogItem.Properties.StockMinSetup,
            OverstockPercentage = double.IsInfinity(overstockPercentage) ? 0 : overstockPercentage,
            BatchSize = catalogItem.Properties.BatchSize.ToString(),
            ProductFamily = catalogItem.ProductFamily ?? string.Empty,
            Severity = severity,
            IsConfigured = catalogItem.Properties.OptimalStockDaysSetup > 0
        };

        _logger.LogDebug("Mapped catalog item {Code} to DTO: severity {Severity}, stock days {StockDays}, overstock {Overstock}%",
            catalogItem.ProductCode, severity, dto.StockDaysAvailable, dto.OverstockPercentage);

        return dto;
    }
}