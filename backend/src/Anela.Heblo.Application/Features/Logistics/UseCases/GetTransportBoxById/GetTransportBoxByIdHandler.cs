using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;

public class GetTransportBoxByIdHandler : IRequestHandler<GetTransportBoxByIdRequest, GetTransportBoxByIdResponse>
{
    private readonly ILogger<GetTransportBoxByIdHandler> _logger;
    private readonly ITransportBoxRepository _repository;
    private readonly IMapper _mapper;

    public GetTransportBoxByIdHandler(
        ILogger<GetTransportBoxByIdHandler> logger,
        ITransportBoxRepository repository,
        IMapper mapper)
    {
        _logger = logger;
        _repository = repository;
        _mapper = mapper;
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

        var dto = _mapper.Map<TransportBoxDto>(transportBox);

        _logger.LogInformation("Retrieved transport box {Id} with {ItemCount} items",
            transportBox.Id, transportBox.Items.Count);

        return new GetTransportBoxByIdResponse { TransportBox = dto };
    }
}
