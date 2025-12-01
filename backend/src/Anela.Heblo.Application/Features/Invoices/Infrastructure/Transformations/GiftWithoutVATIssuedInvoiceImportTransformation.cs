using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Rem.FlexiBeeSDK.Model.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;

public class GiftWithoutVATIssuedInvoiceImportTransformation : IIssuedInvoiceImportTransformation
{
    private const string ProductCode = "GOODYDO0001";
    
    public Task<IssuedInvoiceDetailFlexiDto> TransformAsync(IssuedInvoiceDetailFlexiDto invoiceDetail, CancellationToken cancellationToken)
    {
        foreach(var item in invoiceDetail.Items)
        {
            if (item.Code == ProductCode)
            {
                item.Store = null;
                item.Amount = "1";
                item.PricePerUnit = item.SumBase ?? item.SumBaseC ?? 0;
                item.AccountBaseDal = "code:325002"; // 325002 - Dary a sponzoring
                item.CategoryVatReport = "code:0.0.";
                item.CategoryVat = "code:000U";
                item.CopyCategoryVatReport = false;
                item.CopyCategoryVat = false;
            }
        }

        return Task.FromResult(invoiceDetail);
    }
}