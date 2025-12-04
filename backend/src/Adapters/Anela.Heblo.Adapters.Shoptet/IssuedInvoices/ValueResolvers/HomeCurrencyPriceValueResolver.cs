using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;

namespace Anela.Heblo.Adapters.Shoptet.IssuedInvoices.ValueResolvers
{
    public class HomeCurrencyPriceValueResolver : ITypeConverter<HomeCurrency, InvoicePrice>
    {
        private readonly IInvoicePriceCalculator _priceCalculator;

        public HomeCurrencyPriceValueResolver(IInvoicePriceCalculator priceCalculator)
        {
            _priceCalculator = priceCalculator;
        }

        public InvoicePrice Convert(HomeCurrency source, InvoicePrice destination, ResolutionContext context)
        {
            return _priceCalculator.CalculateItemPrice(source.UnitPrice, source.Price, source.PriceVAT);
        }
    }
}