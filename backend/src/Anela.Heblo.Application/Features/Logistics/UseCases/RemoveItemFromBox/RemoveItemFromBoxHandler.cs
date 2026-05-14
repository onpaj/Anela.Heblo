using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.RemoveItemFromBox;

public class RemoveItemFromBoxHandler : IRequestHandler<RemoveItemFromBoxRequest, RemoveItemFromBoxResponse>
{
    private readonly ITransportBoxRepository _repository;
    private readonly IManufacturedProductInventoryRepository _inventoryRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<RemoveItemFromBoxHandler> _logger;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;

    public RemoveItemFromBoxHandler(
        ITransportBoxRepository repository,
        IManufacturedProductInventoryRepository inventoryRepository,
        ICurrentUserService currentUserService,
        ILogger<RemoveItemFromBoxHandler> logger,
        IMapper mapper,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _inventoryRepository = inventoryRepository;
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

            var removedItem = transportBox.DeleteItem(request.ItemId);
            if (removedItem == null)
            {
                return new RemoveItemFromBoxResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ResourceNotFound,
                    Params = new Dictionary<string, string> { { "itemId", request.ItemId.ToString() } }
                };
            }

            if (removedItem.SourceInventoryId != null)
            {
                var inventoryItem = await _inventoryRepository.GetByIdAsync(removedItem.SourceInventoryId.Value, cancellationToken);
                if (inventoryItem != null)
                {
                    inventoryItem.Restore((decimal)removedItem.Amount, userName, timestamp, transportBox.Id, transportBox.Code);
                    await _inventoryRepository.UpdateAsync(inventoryItem, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("InventoryItem {InventoryId} not found during restore for BoxItem {BoxItemId} — skipping restore",
                        removedItem.SourceInventoryId, removedItem.Id);
                }
            }

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Removed item {ItemId} ({ProductCode}) from transport box {BoxId} by user {UserName}",
                request.ItemId, removedItem.ProductCode, request.BoxId, userName);

            var transportBoxDto = _mapper.Map<TransportBoxDto>(transportBox);

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
