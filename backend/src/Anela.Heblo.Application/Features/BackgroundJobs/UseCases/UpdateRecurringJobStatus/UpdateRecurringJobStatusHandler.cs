using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobStatus;

public class UpdateRecurringJobStatusHandler : IRequestHandler<UpdateRecurringJobStatusRequest, UpdateRecurringJobStatusResponse>
{
    private readonly ILogger<UpdateRecurringJobStatusHandler> _logger;
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public UpdateRecurringJobStatusHandler(
        ILogger<UpdateRecurringJobStatusHandler> logger,
        IRecurringJobConfigurationRepository repository,
        ICurrentUserService currentUserService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    public async Task<UpdateRecurringJobStatusResponse> Handle(
        UpdateRecurringJobStatusRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating recurring job status for {JobName} to {IsEnabled}",
            request.JobName,
            request.IsEnabled);

        var job = await _repository.GetByJobNameAsync(request.JobName, cancellationToken);

        if (job == null)
        {
            _logger.LogWarning("Recurring job not found: {JobName}", request.JobName);
            return new UpdateRecurringJobStatusResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string> { { "JobName", request.JobName } });
        }

        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var modifiedBy = currentUser.Name ?? "System";

            if (request.IsEnabled)
            {
                job.Enable(modifiedBy);
            }
            else
            {
                job.Disable(modifiedBy);
            }

            await _repository.UpdateAsync(job, cancellationToken);

            _logger.LogInformation(
                "Recurring job {JobName} status updated to {IsEnabled}",
                job.JobName,
                job.IsEnabled);

            return new UpdateRecurringJobStatusResponse
            {
                JobName = job.JobName,
                IsEnabled = job.IsEnabled,
                LastModifiedAt = job.LastModifiedAt,
                LastModifiedBy = job.LastModifiedBy
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error updating recurring job status for {JobName}",
                request.JobName);
            return new UpdateRecurringJobStatusResponse(
                ErrorCodes.RecurringJobUpdateFailed,
                new Dictionary<string, string>
                {
                    { "JobName", request.JobName },
                    { "Message", ex.Message }
                });
        }
    }
}
