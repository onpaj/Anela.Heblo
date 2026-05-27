using AutoMapper;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;

public class UpdateManufacturedInventoryItemHandler
    : IRequestHandler<UpdateManufacturedInventoryItemRequest, UpdateManufacturedInventoryItemResponse>
{
    private readonly IManufacturedProductInventoryRepository _repository;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public UpdateManufacturedInventoryItemHandler(
        IManufacturedProductInventoryRepository repository,
        IMapper mapper,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _mapper = mapper;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<UpdateManufacturedInventoryItemResponse> Handle(
        UpdateManufacturedInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        var item = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (item is null)
            return new UpdateManufacturedInventoryItemResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ManufacturedInventoryItemNotFound,
            };

        var user = _currentUserService.GetCurrentUser();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        item.ManualAdjust(request.NewAmount, user.Name ?? "System", now, request.Note);

        await _repository.UpdateAsync(item, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateManufacturedInventoryItemResponse
        {
            Item = _mapper.Map<ManufacturedProductInventoryItemDto>(item),
        };
    }
}
