using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Handlers;

public class AddItemToBoxHandler : IRequestHandler<AddItemToBoxRequest, AddItemToBoxResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AddItemToBoxHandler> _logger;

    public AddItemToBoxHandler(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        ILogger<AddItemToBoxHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<AddItemToBoxResponse> Handle(AddItemToBoxRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var userName = currentUser.Name;

            var transportBox = await _repository.GetByIdWithDetailsAsync(request.BoxId);
            if (transportBox == null)
            {
                return new AddItemToBoxResponse
                {
                    Success = false,
                    ErrorMessage = $"Transport box with ID {request.BoxId} not found"
                };
            }

            var addedItem = transportBox.AddItem(
                request.ProductCode, 
                request.ProductName, 
                request.Amount, 
                DateTime.UtcNow, 
                userName);


            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Added item {ProductCode} (amount: {Amount}) to transport box {BoxId} by user {UserName}", 
                request.ProductCode, request.Amount, request.BoxId, userName);

            var itemDto = new TransportBoxItemDto
            {
                Id = addedItem.Id,
                ProductCode = addedItem.ProductCode,
                ProductName = addedItem.ProductName,
                Amount = addedItem.Amount,
                DateAdded = addedItem.DateAdded,
                UserAdded = addedItem.UserAdded
            };

            var transportBoxDto = MapToDto(transportBox);

            return new AddItemToBoxResponse
            {
                Success = true,
                Item = itemDto,
                TransportBox = transportBoxDto
            };
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error adding item to transport box {BoxId}: {Error}", 
                request.BoxId, ex.Message);
            
            return new AddItemToBoxResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding item {ProductCode} to transport box {BoxId}", 
                request.ProductCode, request.BoxId);
            
            return new AddItemToBoxResponse
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