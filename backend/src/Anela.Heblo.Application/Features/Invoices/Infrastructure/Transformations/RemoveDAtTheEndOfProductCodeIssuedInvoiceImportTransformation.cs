using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;

public class RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation : IIssuedInvoiceImportTransformation
{
    public Task<IssuedInvoiceDetail> TransformAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
        // TODO: Implement product code cleanup once domain model is properly defined
        // This transformation should remove 'D' suffix from product codes matching pattern: TON100050D -> TON100050
        
        return Task.FromResult(invoiceDetail);
    }
}