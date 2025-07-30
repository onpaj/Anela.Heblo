using Anela.Heblo.Application.Domain.IssuedInvoices;

namespace Anela.Heblo.Adapters.Shoptet;

public interface IIssuedInvoiceParser
{
    Task<List<IssuedInvoiceDetail>> ParseAsync(string readToEndAsync);
}