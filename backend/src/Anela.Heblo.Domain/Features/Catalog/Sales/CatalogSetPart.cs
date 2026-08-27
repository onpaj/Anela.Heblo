namespace Anela.Heblo.Domain.Features.Catalog.Sales;

/// <summary>
/// One component of a bundle, as defined in the ERP "sady-a-komplety" evidence.
/// </summary>
public record CatalogSetPart
{
    public required string SetCode { get; init; }
    public required string ComponentCode { get; init; }
    public required string ComponentName { get; init; }
    public double Amount { get; init; }
}
