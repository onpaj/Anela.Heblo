namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public sealed class MaterialInfo
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public string? Note { get; init; }
    public bool HasBoM { get; init; }
    public int? BoMId { get; init; }
}
