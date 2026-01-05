using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobStatus;

public class UpdateRecurringJobStatusRequest : IRequest<UpdateRecurringJobStatusResponse>
{
    public string JobName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}
