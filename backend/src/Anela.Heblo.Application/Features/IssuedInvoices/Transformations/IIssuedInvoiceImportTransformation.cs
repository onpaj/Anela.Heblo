using Rem.FlexiBeeSDK.Model.Invoices;

namespace Anela.Heblo.Invoices.Transformations;

public interface IIssuedInvoiceImportTransformation
{
    Task<IssuedInvoiceDetailFlexiDto> TransformAsync(IssuedInvoiceDetailFlexiDto invoiceDetail, CancellationToken cancellationToken);
}