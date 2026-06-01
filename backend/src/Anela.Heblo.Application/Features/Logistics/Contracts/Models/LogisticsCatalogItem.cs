namespace Anela.Heblo.Application.Features.Logistics.Contracts.Models;

public sealed class LogisticsCatalogItem
{
    public required string ProductCode { get; init; }
    public string? Image { get; init; }
    public decimal EshopStock { get; init; }
    public decimal AvailableStock { get; init; }
}
