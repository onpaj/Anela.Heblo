namespace Anela.Heblo.Application.Features.Logistics.Contracts.Models;

public sealed class LogisticsCatalogItem
{
    public required string ProductCode { get; init; }
    public string? Image { get; init; }
    public decimal EshopStock { get; init; }
    /// <summary>
    /// Stock that can actually be picked from the warehouse right now. Not the same as
    /// <c>StockData.Available</c>, which also counts goods in transport and in the manufacture
    /// warehouse - consuming those is what puts the warehouse into minus.
    /// </summary>
    public decimal WarehouseStock { get; init; }
}
