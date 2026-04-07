namespace Anela.Heblo.Application.Features.ShoptetOrders;

public class CreateEshopOrderRequest
{
    public string Email { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string ExternalCode { get; set; } = null!;
    public string ShippingGuid { get; set; } = null!;
    public string PaymentMethodGuid { get; set; } = null!;
    public string CurrencyCode { get; set; } = "CZK";
    public EshopOrderAddress BillingAddress { get; set; } = new();
    public List<EshopOrderItem> Items { get; set; } = new();
}

public class EshopOrderAddress
{
    public string FullName { get; set; } = null!;
    public string Street { get; set; } = null!;
    public string City { get; set; } = null!;
    public string Zip { get; set; } = null!;
}

public class EshopOrderItem
{
    public string ItemType { get; set; } = null!;
    public string? Code { get; set; }
    public string Name { get; set; } = null!;
    public string VatRate { get; set; } = null!;
    public string ItemPriceWithVat { get; set; } = null!;
    public string Amount { get; set; } = null!;
}
