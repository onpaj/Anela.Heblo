using AutoMapper;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;

public class CreateManufacturedInventoryItemHandler
    : IRequestHandler<CreateManufacturedInventoryItemRequest, CreateManufacturedInventoryItemResponse>
{
    private readonly IManufacturedProductInventoryRepository _repository;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public CreateManufacturedInventoryItemHandler(
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

    public async Task<CreateManufacturedInventoryItemResponse> Handle(
        CreateManufacturedInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var item = new ManufacturedProductInventoryItem(
            request.ProductCode,
            request.ProductName,
            request.Amount,
            user.Name ?? "System",
            now,
            request.LotNumber,
            request.ExpirationDate,
            request.ManufactureOrderId);

        var created = await _repository.AddAsync(item, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateManufacturedInventoryItemResponse
        {
            Item = _mapper.Map<ManufacturedProductInventoryItemDto>(created),
        };
    }
}
