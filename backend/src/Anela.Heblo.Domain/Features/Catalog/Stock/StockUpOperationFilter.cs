namespace Anela.Heblo.Domain.Features.Catalog.Stock;

public class StockUpOperationFilter
{
    public string? State { get; init; }
    public StockUpSourceType? SourceType { get; init; }
    public int? SourceId { get; init; }
    public string? ProductCode { get; init; }
    public string? DocumentNumber { get; init; }
    public DateTime? CreatedFrom { get; init; }
    public DateTime? CreatedTo { get; init; }
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
