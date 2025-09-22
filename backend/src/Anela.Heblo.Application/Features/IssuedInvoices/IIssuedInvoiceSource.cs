using System.Collections.Generic;
using System.Threading.Tasks;
using Anela.Heblo.IssuedInvoices;
using Anela.Heblo.IssuedInvoices.Model;

namespace Anela.Heblo.Invoices;

public interface IIssuedInvoiceSource
{
    Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query);

    Task CommitAsync(IssuedInvoiceDetailBatch batch, string? commitMessage = default);

    Task FailAsync(IssuedInvoiceDetailBatch batch, string? errorMessage = default);
}