using System;

namespace Anela.Heblo.IssuedInvoices
{
    public class IssuedInvoiceDetailItem
    {
        public string Code { get; set; }

        public string Name { get; set; }

        public string VariantName { get; set; }

        public string Amount { get; set; }

        public Guid? ProductGuid { get; set; }

        public string AmountUnit { get; set; }

        public InvoicePrice ItemPrice { get; set; }

        public InvoicePrice BuyPrice { get; set; }
    }
}