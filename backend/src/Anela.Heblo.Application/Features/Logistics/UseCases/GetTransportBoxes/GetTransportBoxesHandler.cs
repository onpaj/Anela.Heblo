using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxes;

public class GetTransportBoxesHandler : IRequestHandler<GetTransportBoxesRequest, GetTransportBoxesResponse>
{
    private readonly ILogger<GetTransportBoxesHandler> _logger;
    private readonly ITransportBoxRepository _repository;
    private readonly IMapper _mapper;

    public GetTransportBoxesHandler(
        ILogger<GetTransportBoxesHandler> logger,
        ITransportBoxRepository repository,
        IMapper mapper)
    {
        _logger = logger;
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<GetTransportBoxesResponse> Handle(GetTransportBoxesRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting transport boxes with filters - Code: {Code}, State: {State}, ProductCode: {ProductCode}, Skip: {Skip}, Take: {Take}",
            request.Code, request.State, request.ProductCode, request.Skip, request.Take);

        // Parse state if provided
        TransportBoxState? stateFilter = null;
        bool isActiveFilter = false;

        if (!string.IsNullOrWhiteSpace(request.State))
        {
            if (request.State.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                // Special case: Active means all states except Closed
                isActiveFilter = true;
            }
            else if (Enum.TryParse<TransportBoxState>(request.State, true, out var parsedState))
            {
                stateFilter = parsedState;
            }
        }

        var (items, totalCount) = await _repository.GetPagedListAsync(
            request.Skip,
            request.Take,
            request.Code,
            stateFilter,
            request.ProductCode,
            request.SortBy,
            request.SortDescending,
            isActiveFilter);

        var transportBoxDtos = _mapper.Map<List<TransportBoxDto>>(items);

        _logger.LogInformation("Retrieved {Count} transport boxes out of {TotalCount} total",
            transportBoxDtos.Count, totalCount);

        return new GetTransportBoxesResponse
        {
            Items = transportBoxDtos,
            TotalCount = totalCount,
            Skip = request.Skip,
            Take = request.Take
        };
    }
}