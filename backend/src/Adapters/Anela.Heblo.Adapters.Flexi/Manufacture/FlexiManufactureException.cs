using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public enum FlexiManufactureOperationKind
{
    StockValidation,
    TemplateFetch,
    LotLoading,
    ConsumptionMovement,
    ProductionMovement,
    BoMUpdate,
    Allocation
}

public class FlexiManufactureException : Exception, IHasFailedConsumptionItems
{
    public FlexiManufactureOperationKind OperationKind { get; }
    public int? WarehouseId { get; }
    public string? RawFlexiError { get; }
    public IReadOnlyList<FailedConsumptionItem> FailedItems { get; }

    public override string Message =>
        string.IsNullOrEmpty(RawFlexiError)
            ? base.Message
            : $"{base.Message}: {RawFlexiError}";

    public FlexiManufactureException(
        FlexiManufactureOperationKind operationKind,
        string message,
        int? warehouseId = null,
        string? rawFlexiError = null,
        IReadOnlyList<FailedConsumptionItem>? failedItems = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        OperationKind = operationKind;
        WarehouseId = warehouseId;
        RawFlexiError = rawFlexiError;
        FailedItems = failedItems ?? [];
    }
}
