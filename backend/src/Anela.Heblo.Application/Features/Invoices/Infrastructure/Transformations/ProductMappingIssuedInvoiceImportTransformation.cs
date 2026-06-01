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
        foreach (var item in invoiceDetail.Items)
        {
            if (item.Code == _originalProductCode)
            {
                item.Code = _newProductCode;
            }
        }

        return Task.FromResult(invoiceDetail);
    }
}