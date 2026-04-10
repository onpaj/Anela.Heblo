using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class IngredientRequirement
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required ProductType ProductType { get; init; }
    public required double RequiredAmount { get; init; }
    public required bool HasLots { get; init; }
}

internal sealed class ConsumptionItem
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required ProductType ProductType { get; init; }
    public string? LotNumber { get; init; }
    public DateOnly? Expiration { get; init; }
    public required double Amount { get; init; }
    public required string SourceProductCode { get; init; }
}

internal sealed record ConsumptionResult(double TotalCost, string? DocCode);

internal sealed record ConsolidatedConsumptionCodes(string? SemiProductIssueCode, string? MaterialIssueCode);
