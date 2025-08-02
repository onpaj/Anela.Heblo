using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Adapters.Shoptet.IssuedInvoices;

public interface IIssuedInvoiceParser
{
    Task<List<IssuedInvoiceDetail>> ParseAsync(string readToEndAsync);
}