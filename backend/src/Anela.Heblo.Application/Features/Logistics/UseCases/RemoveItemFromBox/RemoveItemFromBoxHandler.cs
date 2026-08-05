using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.RemoveItemFromBox;

public class RemoveItemFromBoxHandler : IRequestHandler<RemoveItemFromBoxRequest, RemoveItemFromBoxResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly IInventoryReservationService _inventoryReservationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<RemoveItemFromBoxHandler> _logger;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;

    public RemoveItemFromBoxHandler(
        ITransportBoxRepository repository,
        IInventoryReservationService inventoryReservationService,
        ICurrentUserService currentUserService,
        ILogger<RemoveItemFromBoxHandler> logger,
        IMapper mapper,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _inventoryReservationService = inventoryReservationService;
        _currentUserService = currentUserService;
        _logger = logger;
        _mapper = mapper;
        _timeProvider = timeProvider;
    }

    public async Task<RemoveItemFromBoxResponse> Handle(RemoveItemFromBoxRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var userName = currentUser.Name;
            var timestamp = _timeProvider.GetUtcNow().UtcDateTime;

            var transportBox = await _repository.GetByIdWithDetailsAsync(request.BoxId);
            if (transportBox == null)
            {
                return new RemoveItemFromBoxResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.TransportBoxNotFound,
                    Params = new Dictionary<string, string> { { "boxId", request.BoxId.ToString() } }
                };
            }

            var item = transportBox.Items.SingleOrDefault(i => i.Id == request.ItemId);
            if (item == null)
            {
                return new RemoveItemFromBoxResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ResourceNotFound,
                    Params = new Dictionary<string, string> { { "itemId", request.ItemId.ToString() } }
                };
            }

            if (request.Amount.HasValue && request.Amount.Value <= 0)
            {
                return new RemoveItemFromBoxResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "details", "Amount must be greater than zero." } }
                };
            }

            var amountToRemove = request.Amount ?? item.Amount;
            var decreaseResult = transportBox.DecreaseItem(request.ItemId, amountToRemove);

            if (item.SourceInventoryId != null && decreaseResult!.RemovedAmount > 0)
            {
                await _inventoryReservationService.RestoreAsync(
                    inventoryId: item.SourceInventoryId.Value,
                    amount: (decimal)decreaseResult.RemovedAmount,
                    userName: userName,
                    timestamp: timestamp,
                    boxId: transportBox.Id,
                    boxCode: transportBox.Code,
                    cancellationToken: cancellationToken);
            }

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Removed {RemovedAmount} of item {ItemId} ({ProductCode}) from transport box {BoxId} by user {UserName} (row removed: {RowRemoved})",
                decreaseResult!.RemovedAmount, request.ItemId, item.ProductCode, request.BoxId, userName, decreaseResult.RowRemoved);

            var transportBoxDto = _mapper.Map<TransportBoxDto>(transportBox);

            return new RemoveItemFromBoxResponse
            {
                Success = true,
                TransportBox = transportBoxDto
            };
        }
        catch (TransportBoxInvalidStateTransitionException ex)
        {
            _logger.LogWarning("Invalid state for removing item from transport box {BoxId}: {Message}",
                request.BoxId, ex.Message);

            return new RemoveItemFromBoxResponse
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
            _logger.LogWarning("Validation error removing item from transport box {BoxId}: {Error}",
                request.BoxId, ex.Message);

            return new RemoveItemFromBoxResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "details", ex.Message } }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing item {ItemId} from transport box {BoxId}",
                request.ItemId, request.BoxId);

            return new RemoveItemFromBoxResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception,
                Params = new Dictionary<string, string> { { "details", ex.Message } }
            };
        }
    }
}
