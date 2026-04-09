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

public class FlexiManufactureException : Exception
{
    public FlexiManufactureOperationKind OperationKind { get; }
    public int? WarehouseId { get; }
    public string? RawFlexiError { get; }

    public FlexiManufactureException(
        FlexiManufactureOperationKind operationKind,
        string message,
        int? warehouseId = null,
        string? rawFlexiError = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        OperationKind = operationKind;
        WarehouseId = warehouseId;
        RawFlexiError = rawFlexiError;
    }
}
