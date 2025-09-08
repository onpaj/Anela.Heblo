using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;

public class GetManufacturingStockAnalysisRequest : IRequest<GetManufacturingStockAnalysisResponse>
{
    public TimePeriodFilter TimePeriod { get; set; } = TimePeriodFilter.PreviousQuarter;

    public DateTime? CustomFromDate { get; set; }

    public DateTime? CustomToDate { get; set; }

    public string? ProductFamily { get; set; }

    public bool CriticalItemsOnly { get; set; } = false;

    public bool MajorItemsOnly { get; set; } = false;

    public bool AdequateItemsOnly { get; set; } = false;

    public bool UnconfiguredOnly { get; set; } = false;

    public string? SearchTerm { get; set; }

    /// <summary>
    /// Page number for pagination. Must be at least 1.
    /// </summary>
    [Range(Anela.Heblo.Application.Features.Manufacture.ManufactureConstants.MIN_PAGE_NUMBER, int.MaxValue,
           ErrorMessage = "PageNumber must be at least 1")]
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// Number of items per page. Must be between 1 and 100.
    /// </summary>
    [Range(Anela.Heblo.Application.Features.Manufacture.ManufactureConstants.MIN_PAGE_SIZE,
           Anela.Heblo.Application.Features.Manufacture.ManufactureConstants.MAX_PAGE_SIZE,
           ErrorMessage = "PageSize must be between 1 and 100")]
    public int PageSize { get; set; } = 20;

    public ManufacturingStockSortBy SortBy { get; set; } = ManufacturingStockSortBy.StockDaysAvailable;

    public bool SortDescending { get; set; } = false;
}

public enum TimePeriodFilter
{
    PreviousQuarter,
    FutureQuarter,
    Y2Y,
    PreviousSeason,
    CustomPeriod
}

public enum ManufacturingStockSortBy
{
    ProductCode,
    ProductName,
    CurrentStock,
    Reserve,
    SalesInPeriod,
    DailySales,
    OptimalDaysSetup,
    StockDaysAvailable,
    MinimumStock,
    OverstockPercentage,
    BatchSize
}