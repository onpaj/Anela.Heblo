namespace Anela.Heblo.Application.Features.Purchase.Contracts;

/// <summary>
/// Filter criteria applied to analyzed stock items, independent of any use-case request type.
/// </summary>
public class StockAnalysisFilter
{
    public bool OnlyConfigured { get; set; }
    public StockStatusFilter StockStatus { get; set; } = StockStatusFilter.All;
}
