using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRuns;

public class GetDqtRunsHandler : IRequestHandler<GetDqtRunsRequest, GetDqtRunsResponse>
{
    private readonly IDqtRunRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetDqtRunsHandler> _logger;

    public GetDqtRunsHandler(IDqtRunRepository repository, IMapper mapper, ILogger<GetDqtRunsHandler> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<GetDqtRunsResponse> Handle(GetDqtRunsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (items, totalCount) = await _repository.GetPaginatedAsync(
                request.TestType,
                request.Status,
                request.PageNumber,
                request.PageSize,
                cancellationToken);

            return new GetDqtRunsResponse
            {
                Items = _mapper.Map<List<DqtRunDto>>(items),
                TotalCount = totalCount,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting DQT runs");
            return new GetDqtRunsResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception
            };
        }
    }
}
