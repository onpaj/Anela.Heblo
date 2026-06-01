namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public enum InventoryChangeType
{
    InitialWriteDown = 1,
    ConsumedByTransportBox = 2,
    RestoredFromTransportBox = 3,
    ManualAdjustment = 4,
    ManualRemoval = 5,
    ManualAddition = 6
}
