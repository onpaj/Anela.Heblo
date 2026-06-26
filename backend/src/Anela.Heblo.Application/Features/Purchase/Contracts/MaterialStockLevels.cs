namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public sealed class MaterialStockLevels
{
    public required decimal Available { get; init; }
    public required decimal Ordered { get; init; }
    public required decimal EffectiveStock { get; init; }
}
