using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;

public class TriggerRecurringJobRequest : IRequest<TriggerRecurringJobResponse>
{
    public string JobName { get; set; } = string.Empty;
    public bool ForceDisabled { get; set; }
}
