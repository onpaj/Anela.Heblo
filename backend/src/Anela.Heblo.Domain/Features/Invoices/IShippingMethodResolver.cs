namespace Anela.Heblo.Domain.Features.Invoices
{
    public interface IShippingMethodResolver
    {
        ShippingMethod ResolveShippingMethod(IEnumerable<string> invoiceItemTexts);
    }
}