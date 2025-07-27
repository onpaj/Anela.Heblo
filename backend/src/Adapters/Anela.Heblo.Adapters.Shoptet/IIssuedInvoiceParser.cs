using Anela.Heblo.IssuedInvoices;

namespace Anela.Heblo.Adapters.Shoptet;

public interface IIssuedInvoiceParser
{
    Task<List<IssuedInvoiceDetail>> ParseAsync(string readToEndAsync);
}