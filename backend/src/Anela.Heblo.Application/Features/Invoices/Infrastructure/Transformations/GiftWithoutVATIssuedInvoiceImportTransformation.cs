using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;

public class GiftWithoutVATIssuedInvoiceImportTransformation : IIssuedInvoiceImportTransformation
{
    private const string ProductCode = "GOODYDO0001";

    public Task<IssuedInvoiceDetail> TransformAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
        // GOODYDO0001 is a non-stock gift product; FlexiBee rejects a warehouse (sklad)
        // on non-stock items. Flag it so the FlexiBee mapper omits the warehouse.
        foreach (var item in invoiceDetail.Items)
        {
            if (item.Code == ProductCode)
            {
                item.IsNonStock = true;
            }
        }

        return Task.FromResult(invoiceDetail);
    }
}