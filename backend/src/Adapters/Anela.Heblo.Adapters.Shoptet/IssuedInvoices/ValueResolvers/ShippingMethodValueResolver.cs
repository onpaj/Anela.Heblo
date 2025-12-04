using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;

namespace Anela.Heblo.Adapters.Shoptet.IssuedInvoices.ValueResolvers
{
    public class ShippingMethodValueResolver : IValueResolver<Invoice, IssuedInvoiceDetail, ShippingMethod>
    {
        private readonly IShippingMethodResolver _shippingMethodResolver;

        public ShippingMethodValueResolver(IShippingMethodResolver shippingMethodResolver)
        {
            _shippingMethodResolver = shippingMethodResolver;
        }

        public ShippingMethod Resolve(Invoice source, IssuedInvoiceDetail destination, ShippingMethod destMember, ResolutionContext context)
        {
            var invoiceItemTexts = source.InvoiceDetail?.InvoiceItems?.Select(item => item.Text ?? string.Empty) ?? Enumerable.Empty<string>();
            
            return _shippingMethodResolver.ResolveShippingMethod(invoiceItemTexts);
        }
    }
}