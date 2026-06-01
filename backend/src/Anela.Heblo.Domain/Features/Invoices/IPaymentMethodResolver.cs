namespace Anela.Heblo.Domain.Features.Invoices
{
    public interface IPaymentMethodResolver
    {
        BillingMethod ResolvePaymentMethod(string? headerPaymentType, IEnumerable<string> invoiceItemTexts);
    }
}