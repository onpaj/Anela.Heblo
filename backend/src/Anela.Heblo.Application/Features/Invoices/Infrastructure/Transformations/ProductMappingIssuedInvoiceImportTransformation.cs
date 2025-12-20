using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;

public class ProductMappingIssuedInvoiceImportTransformation : IIssuedInvoiceImportTransformation
{
    private readonly string _originalProductCode;
    private readonly string _newProductCode;

    public ProductMappingIssuedInvoiceImportTransformation(string originalProductCode, string newProductCode)
    {
        _originalProductCode = originalProductCode;
        _newProductCode = newProductCode;
    }

    public Task<IssuedInvoiceDetail> TransformAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
        // TODO: Implement product code mapping once domain model is properly defined
        // This transformation should replace product codes in invoice items

        return Task.FromResult(invoiceDetail);
    }
}