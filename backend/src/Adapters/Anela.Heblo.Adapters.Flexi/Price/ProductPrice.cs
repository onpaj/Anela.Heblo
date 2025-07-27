using Volo.Abp.Domain.Entities;

namespace Anela.Heblo.Adapters.Flexi.Price;

public class ProductPrice : Entity<string>
{
    public string ProductCode
    {
        get => Id;
        set => Id = value;
    }
    public decimal Price { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal VatPerc { get; set; }

    public decimal PriceWithVat => Price * ((100 + VatPerc) / (decimal)100);
    public decimal PurchasePriceWithVat => PurchasePrice * ((100 + VatPerc) / (decimal)100);
    public int? BoMId { get; set; }
}