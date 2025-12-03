using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;

namespace Anela.Heblo.Adapters.Shoptet.IssuedInvoices.ValueResolvers
{
    public class ForeignCurrencyPriceValueResolver : ITypeConverter<ForeignCurrency, InvoicePrice>
    {
        private readonly IInvoicePriceCalculator _priceCalculator;

        public ForeignCurrencyPriceValueResolver(IInvoicePriceCalculator priceCalculator)
        {
            _priceCalculator = priceCalculator;
        }

        public InvoicePrice Convert(ForeignCurrency source, InvoicePrice destination, ResolutionContext context)
        {
            return _priceCalculator.CalculateItemPrice(source.UnitPrice, source.Price, source.PriceVAT);
        }
    }
}