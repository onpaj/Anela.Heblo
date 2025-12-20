using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;

namespace Anela.Heblo.Adapters.Shoptet.IssuedInvoices.ValueResolvers
{
    public class PaymentMethodValueResolver : IValueResolver<Invoice, IssuedInvoiceDetail, BillingMethod>
    {
        private readonly IPaymentMethodResolver _paymentMethodResolver;

        public PaymentMethodValueResolver(IPaymentMethodResolver paymentMethodResolver)
        {
            _paymentMethodResolver = paymentMethodResolver;
        }

        public BillingMethod Resolve(Invoice source, IssuedInvoiceDetail destination, BillingMethod destMember, ResolutionContext context)
        {
            var headerPaymentType = source.InvoiceHeader?.PaymentType?.PaymentType;
            var invoiceItemTexts = source.InvoiceDetail?.InvoiceItems?.Select(item => item.Text ?? string.Empty) ?? Enumerable.Empty<string>();

            return _paymentMethodResolver.ResolvePaymentMethod(headerPaymentType, invoiceItemTexts);
        }
    }
}