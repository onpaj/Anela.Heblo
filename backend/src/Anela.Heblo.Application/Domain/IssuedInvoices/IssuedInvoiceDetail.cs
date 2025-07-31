namespace Anela.Heblo.Application.Domain.IssuedInvoices;

public class IssuedInvoiceDetail
{
    public string Code { get; set; } = string.Empty;

    public string? OrderCode { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime ChangeTime { get; set; }

    public DateTime DueDate { get; set; }

    public DateTime TaxDate { get; set; }

    public bool? AddressesEqual { get; set; }

    public long? VarSymbol { get; set; }

    public long? ConstSymbol { get; set; }

    public long? SpecSymbol { get; set; }

    public BillingMethod BillingMethod { get; set; }

    public ShippingMethod ShippingMethod { get; set; }

    public bool? VatPayer { get; set; }

    public List<IssuedInvoiceDetailItem> Items { get; set; } = new();

    public InvoiceAddress BillingAddress { get; set; } = new();

    public InvoiceAddress DeliveryAddress { get; set; } = new();

    public InvoicePrice Price { get; set; } = new();

    public InvoiceCustomer Customer { get; set; } = new();
}