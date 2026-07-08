namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public sealed class MaterialLotStockSnapshot
{
    public required string ProductCode { get; init; }
    public required decimal ErpStock { get; init; }
    public required IReadOnlyList<decimal> LotAmounts { get; init; }
}
