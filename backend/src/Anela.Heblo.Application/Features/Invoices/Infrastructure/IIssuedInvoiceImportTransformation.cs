using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

public interface IIssuedInvoiceImportTransformation
{
    Task<IssuedInvoiceDetail> TransformAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default);
}