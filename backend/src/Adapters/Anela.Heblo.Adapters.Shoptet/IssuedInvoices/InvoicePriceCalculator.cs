using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Adapters.Shoptet.IssuedInvoices
{
    public class InvoicePriceCalculator : IInvoicePriceCalculator
    {
        public InvoicePrice CalculateItemPrice(decimal unitPrice, decimal totalPriceWithoutVat, decimal totalVatPrice)
        {
            var price = new InvoicePrice();

            // Calculate quantity from totalPrice / unitPrice
            if (unitPrice != 0)
            {
                decimal quantity = totalPriceWithoutVat / unitPrice;
                return CalculateItemPriceFromTotal(totalPriceWithoutVat, totalVatPrice, quantity);
            }
            else
            {
                // Fallback if unitPrice is 0
                price.WithoutVat = 0;
                price.Vat = 0;
                price.WithVat = 0;
                return price;
            }
        }

        public InvoicePrice CalculateItemPriceFromTotal(decimal totalPriceWithoutVat, decimal totalVatPrice, decimal quantity)
        {
            var price = new InvoicePrice();

            if (quantity > 0)
            {
                // Calculate unit price without VAT from total base price (totalPrice - totalVatPrice) / quantity
                price.WithoutVat = Math.Round(totalPriceWithoutVat / quantity, 4);

                // Calculate VAT per unit from totalVatPrice / quantity
                price.Vat = Math.Round(totalVatPrice / quantity, 4);

                // Total price per unit - should match (totalPrice / quantity) when multiplied back
                price.WithVat = Math.Round((totalPriceWithoutVat + totalVatPrice) / quantity, 2);
                price.TotalWithoutVat = totalPriceWithoutVat;
                price.TotalWithVat = totalPriceWithoutVat + totalVatPrice;
            }
            else
            {
                price.WithoutVat = 0;
                price.Vat = 0;
                price.WithVat = 0;
            }

            return price;
        }
    }
}