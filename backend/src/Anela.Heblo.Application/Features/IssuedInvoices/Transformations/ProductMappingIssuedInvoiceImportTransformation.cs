using System.Threading;
using System.Threading.Tasks;
using Rem.FlexiBeeSDK.Model.Invoices;

namespace Anela.Heblo.Invoices.Transformations;

public class ProductMappingIssuedInvoiceImportTransformation : IIssuedInvoiceImportTransformation
{
    private readonly string _originalProductCode;
    private readonly string _newProductCode;

    public ProductMappingIssuedInvoiceImportTransformation(string originalProductCode, string newProductCode)
    {
        _originalProductCode = originalProductCode;
        _newProductCode = newProductCode;
    }
    
    public Task<IssuedInvoiceDetailFlexiDto> TransformAsync(IssuedInvoiceDetailFlexiDto invoiceDetail, CancellationToken cancellationToken)
    {
        foreach (var issuedInvoiceItem in invoiceDetail.Items)
        {
            if (!string.IsNullOrEmpty(issuedInvoiceItem.Code) && _originalProductCode == issuedInvoiceItem.Code)
            {
                issuedInvoiceItem.Code = _newProductCode;
            }
        }

        return Task.FromResult(invoiceDetail);
    }
}