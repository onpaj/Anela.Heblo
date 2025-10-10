using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxes;

public class GetTransportBoxesHandler : IRequestHandler<GetTransportBoxesRequest, GetTransportBoxesResponse>
{
    private readonly ILogger<GetTransportBoxesHandler> _logger;
    private readonly ITransportBoxRepository _repository;

    public GetTransportBoxesHandler(
        ILogger<GetTransportBoxesHandler> logger,
        ITransportBoxRepository repository)
    {
        _logger = logger;
        _repository = repository;
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

        var transportBoxDtos = items.Select(box => new TransportBoxDto
        {
            Id = box.Id,
            Code = box.Code,
            State = box.State.ToString(),
            DefaultReceiveState = box.DefaultReceiveState.ToString(),
            Description = box.Description,
            LastStateChanged = box.LastStateChanged,
            Location = box.Location,
            IsInTransit = box.IsInTransit,
            IsInReserve = box.IsInReserve,
            ItemCount = box.Items.Count,
            Items = box.Items.Select(item => new TransportBoxItemDto
            {
                Id = item.Id,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                Amount = item.Amount,
                DateAdded = item.DateAdded,
                UserAdded = item.UserAdded
            }).ToList(),
            StateLog = box.StateLog.Select(log => new TransportBoxStateLogDto
            {
                Id = log.Id,
                State = log.State.ToString(),
                StateDate = log.StateDate,
                User = log.User,
                Description = log.Description
            }).OrderByDescending(log => log.StateDate).ToList()
        }).ToList();

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