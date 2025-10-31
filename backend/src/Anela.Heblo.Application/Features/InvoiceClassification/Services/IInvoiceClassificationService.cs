using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Services;

public interface IInvoiceClassificationService
{
    Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoiceDto invoice);
}