using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public class GetPurchaseStockAnalysisRequest : IRequest<GetPurchaseStockAnalysisResponse>
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public StockStatusFilter StockStatus { get; set; } = StockStatusFilter.All;

    public bool OnlyConfigured { get; set; } = false;

    public string? SearchTerm { get; set; }

    public int PageNumber { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    public StockAnalysisSortBy SortBy { get; set; } = StockAnalysisSortBy.StockEfficiency;

    public bool SortDescending { get; set; } = false;
}

public enum StockStatusFilter
{
    All,
    Critical,
    Low,
    Optimal,
    Overstocked,
    NotConfigured
}

public enum StockAnalysisSortBy
{
    ProductCode,
    ProductName,
    AvailableStock,
    Consumption,
    StockEfficiency,
    LastPurchaseDate
}