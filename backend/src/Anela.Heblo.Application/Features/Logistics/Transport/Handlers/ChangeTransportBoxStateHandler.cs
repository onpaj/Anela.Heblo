using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Handlers;

public class ChangeTransportBoxStateHandler : IRequestHandler<ChangeTransportBoxStateRequest, ChangeTransportBoxStateResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly IMediator _mediator;
    private readonly ILogger<ChangeTransportBoxStateHandler> _logger;

    public ChangeTransportBoxStateHandler(
        ITransportBoxRepository repository,
        IMediator mediator,
        ILogger<ChangeTransportBoxStateHandler> logger)
    {
        _repository = repository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<ChangeTransportBoxStateResponse> Handle(ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var box = await _repository.GetByIdAsync(request.BoxId);
            if (box == null)
            {
                return new ChangeTransportBoxStateResponse
                {
                    Success = false,
                    ErrorMessage = $"Transport box with ID {request.BoxId} not found."
                };
            }

            if (!Enum.TryParse<TransportBoxState>(request.NewState, out var newState))
            {
                return new ChangeTransportBoxStateResponse
                {
                    Success = false,
                    ErrorMessage = $"Invalid state: {request.NewState}"
                };
            }

            // Get the transition action
            var transition = box.TransitionNode.GetTransition(newState);
            
            // Check condition if exists
            if (transition.Condition != null && !transition.Condition(box))
            {
                return new ChangeTransportBoxStateResponse
                {
                    Success = false,
                    ErrorMessage = $"Condition not met for transition to {newState}"
                };
            }

            // Execute the transition
            await transition.ChangeStateAsync(box, DateTime.UtcNow, "System"); // TODO: Get actual user

            // Save changes
            await _repository.UpdateAsync(box);

            // Get updated box details
            var updatedBoxRequest = new GetTransportBoxByIdRequest { Id = request.BoxId };
            var updatedBox = await _mediator.Send(updatedBoxRequest, cancellationToken);

            _logger.LogInformation("Transport box {BoxId} state changed to {NewState}", request.BoxId, newState);

            return new ChangeTransportBoxStateResponse
            {
                Success = true,
                UpdatedBox = updatedBox
            };
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("State transition validation failed for box {BoxId}: {Message}", request.BoxId, ex.Message);
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing state for transport box {BoxId}", request.BoxId);
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorMessage = "An error occurred while changing the box state."
            };
        }
    }
}