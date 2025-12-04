using Anela.Heblo.Xcc.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetRunningInvoiceImportJobs;

public class GetRunningInvoiceImportJobsRequest : IRequest<IList<BackgroundJobInfo>>
{
}