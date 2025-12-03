using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Adapters.Shoptet.IssuedInvoices
{
    public class ShippingMethodResolver : IShippingMethodResolver
    {
        public ShippingMethod ResolveShippingMethod(IEnumerable<string> invoiceItemTexts)
        {
            var itemTexts = invoiceItemTexts.ToList();

            // Check for specific PPL ParcelShop first (more specific match)
            if (itemTexts.Any(text => string.Equals(text, "PPL - ParcelShop", StringComparison.Ordinal)))
                return ShippingMethod.PPLParcelShop;

            // Check for general PPL
            if (itemTexts.Any(text => text.Contains("PPL", StringComparison.InvariantCultureIgnoreCase)))
                return ShippingMethod.PPL;

            // Check for personal pickup
            if (itemTexts.Any(text => text.Contains("Osobn", StringComparison.InvariantCultureIgnoreCase)))
                return ShippingMethod.PickUp;

            // Check for GLS
            if (itemTexts.Any(text => text.Contains("GLS", StringComparison.InvariantCultureIgnoreCase)))
                return ShippingMethod.GLS;

            // Check for Zásilkovna
            if (itemTexts.Any(text => text.Contains("zásilk", StringComparison.InvariantCultureIgnoreCase)))
                return ShippingMethod.Zasilkovna;

            // Default fallback
            return ShippingMethod.PickUp;
        }
    }
}