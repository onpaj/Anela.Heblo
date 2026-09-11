using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Application.Features.ProductPricing.Contracts;

public class ProductPriceDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal PriceWithVat { get; set; }
    public decimal PriceWithoutVat { get; set; }
    public decimal VatRate { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;

    public PriceSyncStatus ShoptetStatus { get; set; }
    public decimal? ShoptetRemoteValue { get; set; }
    public PriceSyncStatus FlexiStatus { get; set; }
    public decimal? FlexiRemoteValue { get; set; }
}
