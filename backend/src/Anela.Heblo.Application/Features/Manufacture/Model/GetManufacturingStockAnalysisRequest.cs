using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.Model;

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

    public int PageNumber { get; set; } = 1;

    [Range(1, 100)]
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
    SalesInPeriod,
    DailySales,
    OptimalDaysSetup,
    StockDaysAvailable,
    MinimumStock,
    OverstockPercentage,
    BatchSize
}