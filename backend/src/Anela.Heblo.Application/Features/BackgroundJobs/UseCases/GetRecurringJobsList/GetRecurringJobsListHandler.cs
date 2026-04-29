using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using NCrontab.Advanced;
using NCrontab.Advanced.Exceptions;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJobsList;

public class GetRecurringJobsListHandler : IRequestHandler<GetRecurringJobsListRequest, GetRecurringJobsListResponse>
{
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetRecurringJobsListHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public GetRecurringJobsListHandler(
        IRecurringJobConfigurationRepository repository,
        IMapper mapper,
        ILogger<GetRecurringJobsListHandler> logger,
        TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<GetRecurringJobsListResponse> Handle(
        GetRecurringJobsListRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting recurring jobs list");

        var jobs = await _repository.GetAllAsync(cancellationToken);
        var jobDtos = _mapper.Map<List<RecurringJobDto>>(jobs);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var dto in jobDtos)
        {
            if (!dto.IsEnabled)
            {
                dto.NextRunAt = null;
                continue;
            }

            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(RecurringJobMetadata.DefaultTimeZoneId);
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
                var nextLocal = CrontabSchedule.Parse(dto.CronExpression).GetNextOccurrence(nowLocal);
                var nextUtc = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), tz);
                dto.NextRunAt = DateTime.SpecifyKind(nextUtc, DateTimeKind.Utc);
            }
            catch (CrontabException ex)
            {
                _logger.LogWarning(ex, "Invalid CRON expression '{CronExpression}' for job '{JobName}', NextRunAt will be null",
                    dto.CronExpression, dto.JobName);
                dto.NextRunAt = null;
            }
            catch (TimeZoneNotFoundException ex)
            {
                _logger.LogWarning(ex, "Timezone '{TimeZoneId}' not found on host, NextRunAt will be null for job '{JobName}'",
                    RecurringJobMetadata.DefaultTimeZoneId, dto.JobName);
                dto.NextRunAt = null;
            }
        }

        _logger.LogInformation("Retrieved {Count} recurring jobs", jobDtos.Count);

        return new GetRecurringJobsListResponse
        {
            Jobs = jobDtos
        };
    }
}
