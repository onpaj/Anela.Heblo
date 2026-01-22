using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;

public class GiftPackageManufactureLog : Entity<int>
{
    public string GiftPackageCode { get; private set; }
    public int QuantityCreated { get; private set; }
    public bool StockOverrideApplied { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; }
    public GiftPackageOperationType OperationType { get; private set; }

    public List<GiftPackageManufactureItem> ConsumedItems { get; private set; } = new();

    // Constructor for manufacture operations (backward compatibility)
    public GiftPackageManufactureLog(
        string giftPackageCode,
        int quantityCreated,
        bool stockOverrideApplied,
        DateTime createdAt,
        string createdBy)
    {
        GiftPackageCode = giftPackageCode;
        QuantityCreated = quantityCreated;
        StockOverrideApplied = stockOverrideApplied;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        OperationType = GiftPackageOperationType.Manufacture;
    }

    // Constructor for disassembly operations
    public GiftPackageManufactureLog(
        string giftPackageCode,
        int quantityCreated,
        DateTime createdAt,
        string createdBy,
        GiftPackageOperationType operationType)
    {
        GiftPackageCode = giftPackageCode;
        QuantityCreated = quantityCreated;
        StockOverrideApplied = false; // Not applicable for disassembly
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        OperationType = operationType;
    }

    // EF Core constructor
    private GiftPackageManufactureLog() { }

    public void AddConsumedItem(string productCode, int quantityConsumed)
    {
        ConsumedItems.Add(new GiftPackageManufactureItem(Id, productCode, quantityConsumed));
    }
}