using Rem.FlexiBeeSDK.Model.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

public interface IIssuedInvoiceImportTransformation
{
    Task<IssuedInvoiceDetailFlexiDto> TransformAsync(IssuedInvoiceDetailFlexiDto invoiceDetail, CancellationToken cancellationToken);
}