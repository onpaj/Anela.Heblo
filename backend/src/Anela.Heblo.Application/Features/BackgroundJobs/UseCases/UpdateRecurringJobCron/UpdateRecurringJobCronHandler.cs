using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using NCrontab.Advanced;
using NCrontab.Advanced.Exceptions;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;

public class UpdateRecurringJobCronHandler : IRequestHandler<UpdateRecurringJobCronRequest, UpdateRecurringJobCronResponse>
{
    private readonly ILogger<UpdateRecurringJobCronHandler> _logger;
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHangfireRecurringJobScheduler _scheduler;

    public UpdateRecurringJobCronHandler(
        ILogger<UpdateRecurringJobCronHandler> logger,
        IRecurringJobConfigurationRepository repository,
        ICurrentUserService currentUserService,
        IHangfireRecurringJobScheduler scheduler)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public async Task<UpdateRecurringJobCronResponse> Handle(
        UpdateRecurringJobCronRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating CRON expression for {JobName} to {CronExpression}",
            request.JobName, request.CronExpression);

        if (!IsValidCronExpression(request.CronExpression))
        {
            _logger.LogWarning("Invalid CRON expression supplied for {JobName}: '{CronExpression}'",
                request.JobName, request.CronExpression);
            return new UpdateRecurringJobCronResponse(
                ErrorCodes.InvalidCronExpression,
                new Dictionary<string, string>
                {
                    { "JobName", request.JobName },
                    { "CronExpression", request.CronExpression }
                });
        }

        var job = await _repository.GetByJobNameAsync(request.JobName, cancellationToken);

        if (job == null)
        {
            _logger.LogWarning("Recurring job not found: {JobName}", request.JobName);
            return new UpdateRecurringJobCronResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string> { { "JobName", request.JobName } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        var modifiedBy = currentUser.Name ?? "System";

        job.UpdateCronExpression(request.CronExpression, modifiedBy);
        await _repository.UpdateAsync(job, cancellationToken);

        _scheduler.UpdateCronSchedule(job.JobName, job.CronExpression);

        _logger.LogInformation(
            "CRON expression for {JobName} updated to {CronExpression} by {ModifiedBy}",
            job.JobName, job.CronExpression, modifiedBy);

        return new UpdateRecurringJobCronResponse
        {
            JobName = job.JobName,
            CronExpression = job.CronExpression,
            LastModifiedAt = job.LastModifiedAt,
            LastModifiedBy = job.LastModifiedBy
        };
    }

    private static bool IsValidCronExpression(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return false;

        try
        {
            CrontabSchedule.Parse(cronExpression);
            return true;
        }
        catch (CrontabException)
        {
            return false;
        }
    }
}
