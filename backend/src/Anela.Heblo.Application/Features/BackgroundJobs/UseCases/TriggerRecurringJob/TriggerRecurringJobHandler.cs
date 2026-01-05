using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;

public class TriggerRecurringJobHandler : IRequestHandler<TriggerRecurringJobRequest, TriggerRecurringJobResponse>
{
    private readonly IRecurringJobTriggerService _triggerService;

    public TriggerRecurringJobHandler(IRecurringJobTriggerService triggerService)
    {
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
    }

    public async Task<TriggerRecurringJobResponse> Handle(TriggerRecurringJobRequest request, CancellationToken cancellationToken)
    {
        var jobId = await _triggerService.TriggerJobAsync(request.JobName, request.ForceDisabled, cancellationToken);

        if (jobId == null)
        {
            return new TriggerRecurringJobResponse
            {
                Success = false,
                ErrorMessage = $"Job '{request.JobName}' not found or is disabled (use forceDisabled to override)"
            };
        }

        return new TriggerRecurringJobResponse
        {
            Success = true,
            JobId = jobId
        };
    }
}
