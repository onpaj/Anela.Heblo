using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFlexiManufactureDocumentService
{
    // Per-product (Product manufacture) path
    Task<ConsolidatedConsumptionCodes> SubmitConsolidatedConsumptionAsync(
        SubmitManufactureClientRequest request,
        List<ConsumptionItem> consumptionItems,
        Dictionary<string, double> productCosts,
        CancellationToken cancellationToken);

    Task<string?> SubmitConsolidatedProductionAsync(
        SubmitManufactureClientRequest request,
        Dictionary<string, double> productCosts,
        CancellationToken cancellationToken);

    // Aggregated (SemiProduct manufacture) path
    Task<ConsumptionResult> SubmitConsumptionAsync(
        SubmitManufactureClientRequest request,
        List<ConsumptionItem> consumptionItems,
        CancellationToken cancellationToken);

    Task<string?> SubmitProductionAsync(
        SubmitManufactureClientRequest request,
        double totalConsumptionCost,
        CancellationToken cancellationToken);
}
