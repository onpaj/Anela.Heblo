namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public sealed class StockOperationSnapshot
{
    public required string ProductCode { get; init; }
    public required int Amount { get; init; }
    public required string DocumentNumber { get; init; }
    public required StockOperationStateSnapshot State { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
}
