using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Handlers;

public class RemoveItemFromBoxHandler : IRequestHandler<RemoveItemFromBoxRequest, RemoveItemFromBoxResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<RemoveItemFromBoxHandler> _logger;

    public RemoveItemFromBoxHandler(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        ILogger<RemoveItemFromBoxHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<RemoveItemFromBoxResponse> Handle(RemoveItemFromBoxRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var userName = currentUser.Name;

            var transportBox = await _repository.GetByIdWithDetailsAsync(request.BoxId);
            if (transportBox == null)
            {
                return new RemoveItemFromBoxResponse
                {
                    Success = false,
                    ErrorMessage = $"Transport box with ID {request.BoxId} not found"
                };
            }

            var removedItem = transportBox.DeleteItem(request.ItemId);
            if (removedItem == null)
            {
                return new RemoveItemFromBoxResponse
                {
                    Success = false,
                    ErrorMessage = $"Item with ID {request.ItemId} not found in transport box"
                };
            }

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Removed item {ItemId} ({ProductCode}) from transport box {BoxId} by user {UserName}", 
                request.ItemId, removedItem.ProductCode, request.BoxId, userName);

            var transportBoxDto = MapToDto(transportBox);

            return new RemoveItemFromBoxResponse
            {
                Success = true,
                TransportBox = transportBoxDto
            };
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error removing item from transport box {BoxId}: {Error}", 
                request.BoxId, ex.Message);
            
            return new RemoveItemFromBoxResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing item {ItemId} from transport box {BoxId}", 
                request.ItemId, request.BoxId);
            
            return new RemoveItemFromBoxResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static TransportBoxDto MapToDto(TransportBox transportBox)
    {
        return new TransportBoxDto
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
            }).ToList()
        };
    }
}