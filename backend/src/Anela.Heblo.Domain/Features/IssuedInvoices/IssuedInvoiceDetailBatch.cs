using System.Collections.Generic;

namespace Anela.Heblo.IssuedInvoices;

public class IssuedInvoiceDetailBatch
{
    public List<IssuedInvoiceDetail> Invoices { get; set; } = new ();
    public string BatchId { get; set; } = string.Empty;
}