using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Handlers;

public class GetTransportBoxByIdHandler : IRequestHandler<GetTransportBoxByIdRequest, GetTransportBoxByIdResponse>
{
    private readonly ILogger<GetTransportBoxByIdHandler> _logger;
    private readonly ITransportBoxRepository _repository;

    public GetTransportBoxByIdHandler(
        ILogger<GetTransportBoxByIdHandler> logger,
        ITransportBoxRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<GetTransportBoxByIdResponse> Handle(GetTransportBoxByIdRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting transport box with ID: {Id}", request.Id);

        var transportBox = await _repository.GetByIdWithDetailsAsync(request.Id);

        if (transportBox == null)
        {
            _logger.LogWarning("Transport box with ID {Id} not found", request.Id);
            return new GetTransportBoxByIdResponse { TransportBox = null };
        }

        var dto = new TransportBoxDto
        {
            Id = transportBox.Id,
            Code = transportBox.Code,
            State = transportBox.State.ToString(),
            DefaultReceiveState = transportBox.DefaultReceiveState.ToString(),
            Description = transportBox.Description,
            LastStateChanged = transportBox.LastStateChanged,
            Location = transportBox.Location,
            IsInTransit = transportBox.IsInTransit,
            IsInReserve = transportBox.IsInReserve,
            ItemCount = transportBox.Items.Count,
            Items = transportBox.Items.Select(item => new TransportBoxItemDto
            {
                Id = item.Id,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                Amount = item.Amount,
                DateAdded = item.DateAdded,
                UserAdded = item.UserAdded
            }).ToList(),
            StateLog = transportBox.StateLog.Select(log => new TransportBoxStateLogDto
            {
                Id = log.Id,
                State = log.State.ToString(),
                StateDate = log.StateDate,
                User = log.User,
                Description = log.Description
            }).OrderByDescending(log => log.StateDate).ToList()
        };

        _logger.LogInformation("Retrieved transport box {Id} with {ItemCount} items", 
            transportBox.Id, transportBox.Items.Count);

        return new GetTransportBoxByIdResponse { TransportBox = dto };
    }
}