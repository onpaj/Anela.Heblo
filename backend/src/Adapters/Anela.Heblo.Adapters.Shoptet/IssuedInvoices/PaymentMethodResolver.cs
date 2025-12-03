using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Adapters.Shoptet.IssuedInvoices
{
    public class PaymentMethodResolver : IPaymentMethodResolver
    {
        public BillingMethod ResolvePaymentMethod(string? headerPaymentType, IEnumerable<string> invoiceItemTexts)
        {
            // First check header payment type (most reliable)
            if (!string.IsNullOrEmpty(headerPaymentType))
            {
                switch (headerPaymentType.ToLowerInvariant())
                {
                    case "creditcard":
                        return BillingMethod.CreditCard;
                    case "cash":
                        return BillingMethod.Cash;
                }
            }

            // Fallback to invoice item text analysis
            var itemTexts = invoiceItemTexts.ToList();
            
            if (itemTexts.Any(text => string.Equals(text, "Převodem", StringComparison.Ordinal)))
                return BillingMethod.BankTransfer;
                
            if (itemTexts.Any(text => string.Equals(text, "Hotově", StringComparison.Ordinal)))
                return BillingMethod.Cash;
                
            if (itemTexts.Any(text => text.Contains("kart", StringComparison.InvariantCultureIgnoreCase)))
                return BillingMethod.Comgate;
                
            if (itemTexts.Any(text => string.Equals(text, "Dobírkou", StringComparison.Ordinal)))
                return BillingMethod.CoD;

            // Default fallback
            return BillingMethod.BankTransfer;
        }
    }
}