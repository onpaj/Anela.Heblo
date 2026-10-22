namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ProductStockTakingSnapshot
{
    public int Id { get; init; }
    public StockTakingKind Type { get; init; }
    public string Code { get; init; } = "";
    public double AmountNew { get; init; }
    public double AmountOld { get; init; }
    public DateTime Date { get; init; }
    public string? User { get; init; }
    public string? Error { get; init; }
}
