using Anela.Heblo.Xcc.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetInvoiceImportJobStatus;

public class GetInvoiceImportJobStatusRequest : IRequest<BackgroundJobInfo?>
{
    public string JobId { get; set; } = string.Empty;
}