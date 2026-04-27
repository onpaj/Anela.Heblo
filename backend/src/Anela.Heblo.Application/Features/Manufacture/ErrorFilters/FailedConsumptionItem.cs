namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters;

public sealed record FailedConsumptionItem(
    string ProductCode,
    string ProductName,
    string? LotNumber,
    DateOnly? Expiration,
    double Amount);

public interface IHasFailedConsumptionItems
{
    IReadOnlyList<FailedConsumptionItem> FailedItems { get; }
}
