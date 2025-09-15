using Anela.Heblo.Application.Features.Transport.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Transport.UseCases.UpdateTransportBoxDescription;

public class UpdateTransportBoxDescriptionHandler : IRequestHandler<UpdateTransportBoxDescriptionRequest, UpdateTransportBoxDescriptionResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly IMediator _mediator;
    private readonly ILogger<UpdateTransportBoxDescriptionHandler> _logger;

    public UpdateTransportBoxDescriptionHandler(
        ITransportBoxRepository repository,
        IMediator mediator,
        ILogger<UpdateTransportBoxDescriptionHandler> logger)
    {
        _repository = repository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<UpdateTransportBoxDescriptionResponse> Handle(UpdateTransportBoxDescriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var box = await _repository.GetByIdWithDetailsAsync(request.BoxId);
            if (box == null)
            {
                return new UpdateTransportBoxDescriptionResponse(
                    ErrorCodes.TransportBoxNotFound,
                    new Dictionary<string, string>() { { nameof(request.BoxId), request.BoxId.ToString() } }
                );
            }

            // Update the description
            box.Description = request.Description;

            // Save changes
            await _repository.UpdateAsync(box, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            // Get updated box details
            var updatedBoxRequest = new GetTransportBoxByIdRequest { Id = request.BoxId };
            var updatedBox = await _mediator.Send(updatedBoxRequest, cancellationToken);

            _logger.LogInformation("Transport box {BoxId} description updated", request.BoxId);

            return new UpdateTransportBoxDescriptionResponse
            {
                UpdatedBox = updatedBox
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating description for transport box {BoxId}", request.BoxId);
            return new UpdateTransportBoxDescriptionResponse(
                ErrorCodes.TransportBoxStateChangeError,
                new Dictionary<string, string> { { "boxId", request.BoxId.ToString() } }
            );
        }
    }
}