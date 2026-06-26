using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;

public class DeleteManufacturedInventoryItemHandler
    : IRequestHandler<DeleteManufacturedInventoryItemRequest, DeleteManufacturedInventoryItemResponse>
{
    private readonly IManufacturedProductInventoryRepository _repository;

    public DeleteManufacturedInventoryItemHandler(
        IManufacturedProductInventoryRepository repository)
    {
        _repository = repository;
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

        await _repository.DeleteAsync(item, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteManufacturedInventoryItemResponse();
    }
}
