using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Rem.FlexiBeeSDK.Model.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;

public class RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation : IIssuedInvoiceImportTransformation
{
    public Task<IssuedInvoiceDetailFlexiDto> TransformAsync(IssuedInvoiceDetailFlexiDto invoiceDetail, CancellationToken cancellationToken)
    {
        foreach (var issuedInvoiceItem in invoiceDetail.Items)
        {
            // Match TON100050D -> TON100050
            if (issuedInvoiceItem.Code != null && Regex.IsMatch(issuedInvoiceItem.Code, "[a-zA-Z]{3}[0-9]{6,7}D"))
            {
                issuedInvoiceItem.Code = issuedInvoiceItem.Code[..^1];
            }
        }

        return Task.FromResult(invoiceDetail);
    }
}