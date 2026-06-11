using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJob;

public class GetRecurringJobHandler : IRequestHandler<GetRecurringJobRequest, GetRecurringJobResponse>
{
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetRecurringJobHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public GetRecurringJobHandler(
        IRecurringJobConfigurationRepository repository,
        IMapper mapper,
        ILogger<GetRecurringJobHandler> logger,
        TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<GetRecurringJobResponse> Handle(
        GetRecurringJobRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting recurring job {JobName}", request.JobName);

        var job = await _repository.GetByJobNameAsync(request.JobName, cancellationToken);

        if (job == null)
        {
            _logger.LogWarning("Recurring job not found: {JobName}", request.JobName);
            return new GetRecurringJobResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string> { { "JobName", request.JobName } });
        }

        var dto = _mapper.Map<RecurringJobDto>(job);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        dto.NextRunAt = RecurringJobNextRunCalculator.Calculate(
            dto.CronExpression, dto.IsEnabled, utcNow, _logger, dto.JobName);

        return new GetRecurringJobResponse { Job = dto };
    }
}
