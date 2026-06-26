namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public sealed class StockTakingSnapshot
{
    public required string Code { get; init; }
    public required double AmountNew { get; init; }
    public string? Error { get; init; }
}
