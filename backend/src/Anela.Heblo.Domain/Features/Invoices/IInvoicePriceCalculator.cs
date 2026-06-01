namespace Anela.Heblo.Domain.Features.Invoices
{
    public interface IInvoicePriceCalculator
    {
        InvoicePrice CalculateItemPrice(decimal unitPrice, decimal totalPrice, decimal totalVatPrice);
    }
}