using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;

public class DeleteManufacturedInventoryItemHandler
    : IRequestHandler<DeleteManufacturedInventoryItemRequest, DeleteManufacturedInventoryItemResponse>
{
    private readonly IManufacturedProductInventoryRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public DeleteManufacturedInventoryItemHandler(
        IManufacturedProductInventoryRepository repository,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<DeleteManufacturedInventoryItemResponse> Handle(
        DeleteManufacturedInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        var item = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (item is null)
            return new DeleteManufacturedInventoryItemResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ManufacturedInventoryItemNotFound,
            };

        var user = _currentUserService.GetCurrentUser();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        item.ManualRemove(user.Name ?? "System", now, request.Note);

        await _repository.UpdateAsync(item, cancellationToken);
        await _repository.DeleteAsync(item, cancellationToken);

        return new DeleteManufacturedInventoryItemResponse();
    }
}
