using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;

public class GiftPackageManufactureItem : Entity<int>
{
    public int ManufactureLogId { get; private set; }
    public string ProductCode { get; private set; }
    public int QuantityConsumed { get; private set; }

    public GiftPackageManufactureLog ManufactureLog { get; private set; }

    public GiftPackageManufactureItem(
        int manufactureLogId,
        string productCode,
        int quantityConsumed)
    {
        ManufactureLogId = manufactureLogId;
        ProductCode = productCode;
        QuantityConsumed = quantityConsumed;
    }

    // EF Core constructor
    private GiftPackageManufactureItem() { }
}