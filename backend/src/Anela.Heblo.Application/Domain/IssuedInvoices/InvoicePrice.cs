namespace Anela.Heblo.IssuedInvoices
{
    public class InvoicePrice
    {
        public decimal Vat { get; set; }

        public string CurrencyCode { get; set; } = "CZK";

        public decimal WithVat { get; set; }

        public decimal WithoutVat { get; set; }

        public decimal? ExchangeRate { get; set; }
        
        public string? VatRate { get; set; }
        
    }
}