namespace Anela.Heblo.Application.Features.Invoices.Contracts;

public class IssuedInvoiceSingleImportArgs
{
    public string InvoiceId { get; set; } = string.Empty;
    public string Currency { get; set; } = "CZK";

    public IssuedInvoiceSingleImportArgs()
    {
    }

    public IssuedInvoiceSingleImportArgs(string invoiceId, string currency)
    {
        InvoiceId = invoiceId;
        Currency = currency;
    }
}