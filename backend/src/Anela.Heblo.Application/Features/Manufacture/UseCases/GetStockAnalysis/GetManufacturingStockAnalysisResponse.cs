using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;

public class GetManufacturingStockAnalysisResponse : BaseResponse
{
    public List<ManufacturingStockItemDto> Items { get; set; } = new();

    public int TotalCount { get; set; }

    public int PageNumber { get; set; }

    public int PageSize { get; set; }

    public ManufacturingStockSummaryDto Summary { get; set; } = new();
}

public class ManufacturingStockItemDto
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double CurrentStock { get; set; }

    public double ErpStock { get; set; }

    public double EshopStock { get; set; }

    public double TransportStock { get; set; }

    public string PrimaryStockSource { get; set; } = string.Empty;

    public double Reserve { get; set; }

    public double Planned { get; set; }

    public double SalesInPeriod { get; set; }

    public double DailySalesRate { get; set; }

    public int OptimalDaysSetup { get; set; }

    public double StockDaysAvailable { get; set; }

    public double MinimumStock { get; set; }

    public double OverstockPercentage { get; set; }

    public string BatchSize { get; set; } = string.Empty;

    public string? ProductFamily { get; set; }

    public ManufacturingStockSeverity Severity { get; set; }

    public bool IsConfigured { get; set; }
}

public class ManufacturingStockSummaryDto
{
    public int TotalProducts { get; set; }

    public int CriticalCount { get; set; }

    public int MajorCount { get; set; }

    public int MinorCount { get; set; }

    public int AdequateCount { get; set; }

    public int UnconfiguredCount { get; set; }

    public DateTime AnalysisPeriodStart { get; set; }

    public DateTime AnalysisPeriodEnd { get; set; }

    public List<string> ProductFamilies { get; set; } = new();
}

public enum ManufacturingStockSeverity
{
    Critical,    // < 8 days of stock available
    Major,       // < 15 days of stock OR below minimum stock setup
    Minor,       // Below optimal stock days setup
    Adequate,    // Above optimal stock levels
    Unconfigured // Missing OptimalStockDaysSetup
}