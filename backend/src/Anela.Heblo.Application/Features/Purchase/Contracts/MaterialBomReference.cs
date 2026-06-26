namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public sealed class MaterialBomReference
{
    public required string ProductCode { get; init; }
    public required int BoMId { get; init; }
}
