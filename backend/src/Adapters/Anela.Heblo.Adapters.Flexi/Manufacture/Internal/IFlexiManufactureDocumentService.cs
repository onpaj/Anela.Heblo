using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFlexiManufactureDocumentService
{
    Task SubmitConsolidatedConsumptionAsync(
        SubmitManufactureClientRequest request,
        List<ConsumptionItem> consumptionItems,
        Dictionary<string, double> productCosts,
        CancellationToken cancellationToken);

    Task SubmitConsolidatedProductionAsync(
        SubmitManufactureClientRequest request,
        Dictionary<string, double> productCosts,
        CancellationToken cancellationToken);
}
