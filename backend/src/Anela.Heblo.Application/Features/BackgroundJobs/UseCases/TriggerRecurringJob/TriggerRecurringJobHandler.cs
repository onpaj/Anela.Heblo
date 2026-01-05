using Anela.Heblo.Application.Shared;
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
            return new TriggerRecurringJobResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string>
                {
                    { "jobName", request.JobName },
                    { "forceDisabled", request.ForceDisabled.ToString() }
                }
            );
        }

        return new TriggerRecurringJobResponse
        {
            JobId = jobId
        };
    }
}
