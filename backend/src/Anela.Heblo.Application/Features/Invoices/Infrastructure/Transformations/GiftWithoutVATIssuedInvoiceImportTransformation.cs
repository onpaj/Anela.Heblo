using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;

public class GiftWithoutVATIssuedInvoiceImportTransformation : IIssuedInvoiceImportTransformation
{
    private const string ProductCode = "GOODYDO0001";

    public Task<IssuedInvoiceDetail> TransformAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
        // TODO: Implement gift transformation logic once domain model is properly defined
        // This transformation should modify invoiceDetail items for gift products
        // Setting VAT category, store, and accounting classification

        return Task.FromResult(invoiceDetail);
    }
}