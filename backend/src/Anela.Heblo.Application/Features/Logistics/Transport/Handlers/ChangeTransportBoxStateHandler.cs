using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Infrastructure;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Handlers;

public class ChangeTransportBoxStateHandler : IRequestHandler<ChangeTransportBoxStateRequest, ChangeTransportBoxStateResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransportBoxRepository _repository;
    private readonly IMediator _mediator;
    private readonly ILogger<ChangeTransportBoxStateHandler> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public ChangeTransportBoxStateHandler(
        IUnitOfWork unitOfWork,
        ITransportBoxRepository repository,
        IMediator mediator,
        ILogger<ChangeTransportBoxStateHandler> logger,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _unitOfWork = unitOfWork;
        _repository = repository;
        _mediator = mediator;
        _logger = logger;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<ChangeTransportBoxStateResponse> Handle(ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var box = await _repository.GetByIdWithDetailsAsync(request.BoxId);
            if (box == null)
            {
                return new ChangeTransportBoxStateResponse
                {
                    Success = false,
                    ErrorMessage = $"Transport box with ID {request.BoxId} not found."
                };
            }


            // Special handling for New -> Opened transition with BoxCode
            if (box.State == TransportBoxState.New && request.NewState == TransportBoxState.Opened)
            {
                if (string.IsNullOrEmpty(request.BoxCode))
                {
                    return new ChangeTransportBoxStateResponse
                    {
                        Success = false,
                        ErrorMessage = "BoxCode is required for transition from New to Opened"
                    };
                }
            }
            if (box.State == TransportBoxState.Opened && request.NewState == TransportBoxState.Reserve)
            {
                if (string.IsNullOrEmpty(request.Location))
                {
                    return new ChangeTransportBoxStateResponse
                    {
                        Success = false,
                        ErrorMessage = "Location is required for transition from Opened to Reserved"
                    };
                }
            }

            box.AssignBoxCodeIfAny(request.BoxCode);
            box.AssignLocationIfAny(request.Location);

            // Get the transition action
            var transition = box.TransitionNode.GetTransition(request.NewState);

            // Check condition if exists
            if (transition.Condition != null && !transition.Condition(box))
            {
                return new ChangeTransportBoxStateResponse
                {
                    Success = false,
                    ErrorMessage = $"Condition not met for transition to {request.NewState}"
                };
            }

            // Set location if provided (typically for Reserve state)
            if (!string.IsNullOrEmpty(request.Location))
            {
                box.Location = request.Location;
            }

            // Set description if provided
            if (!string.IsNullOrEmpty(request.Description))
            {
                box.Description = request.Description;
            }

            // Execute the transition
            var currentUser = _currentUserService.GetCurrentUser();
            var currentTime = _timeProvider.GetUtcNow().UtcDateTime;
            var userName = currentUser.IsAuthenticated ? currentUser.Name ?? "Unknown User" : "Anonymous";

            await transition.ChangeStateAsync(box, currentTime, userName);

            // Save changes
            await _repository.UpdateAsync(box, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Get updated box details
            var updatedBoxRequest = new GetTransportBoxByIdRequest { Id = request.BoxId };
            var updatedBox = await _mediator.Send(updatedBoxRequest, cancellationToken);

            _logger.LogInformation("Transport box {BoxId} state changed to {NewState}", request.BoxId, request.NewState);

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