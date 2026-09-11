using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class ChangeTransportBoxStateHandler : IRequestHandler<ChangeTransportBoxStateRequest, ChangeTransportBoxStateResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<ChangeTransportBoxStateHandler> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;
    private readonly IEnumerable<ITransportBoxTransitionSideEffect> _sideEffects;
    private readonly ITransportBoxInventoryRestorer _inventoryRestorer;

    public ChangeTransportBoxStateHandler(
        ITransportBoxRepository repository,
        IMapper mapper,
        ILogger<ChangeTransportBoxStateHandler> logger,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider,
        IEnumerable<ITransportBoxTransitionSideEffect> sideEffects,
        ITransportBoxInventoryRestorer inventoryRestorer)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
        _sideEffects = sideEffects;
        _inventoryRestorer = inventoryRestorer;
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
                    ErrorCode = ErrorCodes.TransportBoxNotFound,
                    Params = new Dictionary<string, string>() { { nameof(request.BoxId), request.BoxId.ToString() } },
                };
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
                    ErrorCode = ErrorCodes.TransportBoxStateChangeError,
                    Params = new Dictionary<string, string> { { "state", request.NewState.ToString() } }
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


            var sideEffect = _sideEffects.FirstOrDefault(s => s.Supports(box.State, request.NewState));
            if (sideEffect != null)
            {
                var sideEffectResult = await sideEffect.ExecuteAsync(box, request, cancellationToken);
                if (sideEffectResult != null)
                {
                    return sideEffectResult;
                }
            }

            // Execute the transition
            var currentUser = _currentUserService.GetCurrentUser();
            var currentTime = DateTime.SpecifyKind(_timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
            var userName = currentUser.IsAuthenticated ? currentUser.Name ?? "Unknown User" : "Anonymous";

            // Capture items before Reset so we can restore inventory
            var itemsToRestore = box.State == TransportBoxState.Opened && request.NewState == TransportBoxState.New
                ? box.Items.Where(i => i.SourceInventoryId != null).ToList()
                : null;

            await transition.ChangeStateAsync(box, currentTime, userName);

            if (itemsToRestore != null)
            {
                await _inventoryRestorer.RestoreAsync(itemsToRestore, userName, currentTime, box.Id, box.Code, cancellationToken);
            }

            // Save changes
            await _repository.UpdateAsync(box, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            // Map the already-updated box directly — avoids a redundant DB read and MediatR round-trip
            var updatedBoxDto = _mapper.Map<TransportBoxDto>(box);
            var updatedBox = new GetTransportBoxByIdResponse { TransportBox = updatedBoxDto };

            _logger.LogInformation("Transport box {BoxId} state changed to {NewState}", request.BoxId, request.NewState);

            return new ChangeTransportBoxStateResponse
            {
                Success = true,
                UpdatedBox = updatedBox
            };
        }
        catch (TransportBoxCodeRequiredException ex)
        {
            _logger.LogWarning("Box code required for box {BoxId}", request.BoxId);
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.TransportBoxCodeRequired,
            };
        }
        catch (TransportBoxCodeFormatException ex)
        {
            _logger.LogWarning("Invalid box code format for box {BoxId}: {Code}", request.BoxId, ex.EnteredCode);
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.TransportBoxCodeInvalidFormat,
                Params = new Dictionary<string, string> { { "code", ex.EnteredCode } }
            };
        }
        catch (TransportBoxEmptyException ex)
        {
            _logger.LogWarning("Attempted to dispatch empty box {BoxId}", request.BoxId);
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.TransportBoxEmpty,
                Params = new Dictionary<string, string> { { "code", ex.BoxCode ?? "" } }
            };
        }
        catch (TransportBoxInvalidStateTransitionException ex)
        {
            _logger.LogWarning("Invalid state transition for box {BoxId}: {Message}", request.BoxId, ex.Message);
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.TransportBoxInvalidStateTransition,
                Params = new Dictionary<string, string>
                {
                    { "currentState", ex.CurrentState.ToString() },
                    { "allowedStates", string.Join(", ", ex.AllowedStates) }
                }
            };
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("State transition validation failed for box {BoxId}: {Message}", request.BoxId, ex.Message);
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "details", ex.Message } }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing state for transport box {BoxId}", request.BoxId);
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.TransportBoxStateChangeError,
                Params = new Dictionary<string, string> { { "boxId", request.BoxId.ToString() } }
            };
        }
    }
}