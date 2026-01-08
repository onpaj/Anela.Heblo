using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJobsList;

public class GetRecurringJobsListHandler : IRequestHandler<GetRecurringJobsListRequest, GetRecurringJobsListResponse>
{
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetRecurringJobsListHandler> _logger;

    public GetRecurringJobsListHandler(
        IRecurringJobConfigurationRepository repository,
        IMapper mapper,
        ILogger<GetRecurringJobsListHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GetRecurringJobsListResponse> Handle(
        GetRecurringJobsListRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting recurring jobs list");

        var jobs = await _repository.GetAllAsync(cancellationToken);
        var jobDtos = _mapper.Map<List<RecurringJobDto>>(jobs);

        _logger.LogInformation("Retrieved {Count} recurring jobs", jobDtos.Count);

        return new GetRecurringJobsListResponse
        {
            Jobs = jobDtos
        };
    }
}
