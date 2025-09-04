using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IItemFilterService
{
    List<ManufacturingStockItemDto> FilterItems(
        List<ManufacturingStockItemDto> items,
        GetManufacturingStockAnalysisRequest request);

    List<ManufacturingStockItemDto> SortItems(
        List<ManufacturingStockItemDto> items,
        ManufacturingStockSortBy sortBy,
        bool descending);

    ManufacturingStockSummaryDto CalculateSummary(
        List<ManufacturingStockItemDto> items,
        DateTime fromDate,
        DateTime toDate,
        List<string> productFamilies);
}