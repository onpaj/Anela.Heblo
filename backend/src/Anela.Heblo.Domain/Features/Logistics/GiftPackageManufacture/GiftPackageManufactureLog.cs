using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;

public class GiftPackageManufactureLog : Entity<int>
{
    public string GiftPackageCode { get; private set; }
    public int QuantityCreated { get; private set; }
    public bool StockOverrideApplied { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    public List<GiftPackageManufactureItem> ConsumedItems { get; private set; } = new();

    public GiftPackageManufactureLog(
        string giftPackageCode,
        int quantityCreated,
        bool stockOverrideApplied,
        DateTime createdAt,
        Guid createdBy)
    {
        GiftPackageCode = giftPackageCode;
        QuantityCreated = quantityCreated;
        StockOverrideApplied = stockOverrideApplied;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    // EF Core constructor
    private GiftPackageManufactureLog() { }

    public void AddConsumedItem(string productCode, int quantityConsumed)
    {
        ConsumedItems.Add(new GiftPackageManufactureItem(Id, productCode, quantityConsumed));
    }
}