using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;

public class CreateMaterialContainersHandler : IRequestHandler<CreateMaterialContainersRequest, CreateMaterialContainersResponse>
{
    private readonly ILogger<CreateMaterialContainersHandler> _logger;
    private readonly IMaterialContainerRepository _containerRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;

    public CreateMaterialContainersHandler(
        ILogger<CreateMaterialContainersHandler> logger,
        IMaterialContainerRepository containerRepository,
        ICurrentUserService currentUserService,
        IPurchaseOrderRepository purchaseOrderRepository)
    {
        _logger = logger;
        _containerRepository = containerRepository;
        _currentUserService = currentUserService;
        _purchaseOrderRepository = purchaseOrderRepository;
    }

    public async Task<CreateMaterialContainersResponse> Handle(
        CreateMaterialContainersRequest request, CancellationToken cancellationToken)
    {
        var duplicateCode = request.Items
            .GroupBy(i => i.Code)
            .FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicateCode != null)
        {
            return new CreateMaterialContainersResponse(
                ErrorCodes.MaterialContainerCodeExists,
                new Dictionary<string, string> { { "Code", duplicateCode } });
        }

        foreach (var item in request.Items)
        {
            var existing = await _containerRepository.GetByCodeAsync(item.Code, cancellationToken);
            if (existing != null)
            {
                return new CreateMaterialContainersResponse(
                    ErrorCodes.MaterialContainerCodeExists,
                    new Dictionary<string, string>
                    {
                        { "Code", item.Code },
                        { "MaterialCode", existing.MaterialCode },
                        { "LotCode", existing.LotCode },
                        { "Status", existing.Status.ToString() }
                    });
            }
        }

        foreach (var item in request.Items.Where(i => i.PurchaseOrderLineId.HasValue))
        {
            var line = await _purchaseOrderRepository.GetLineByIdAsync(item.PurchaseOrderLineId!.Value, cancellationToken);
            if (line == null)
            {
                return new CreateMaterialContainersResponse(
                    ErrorCodes.PurchaseOrderLineNotFound,
                    new Dictionary<string, string> { { "PurchaseOrderLineId", item.PurchaseOrderLineId.Value.ToString() } });
            }
        }

        var currentUser = _currentUserService.GetCurrentUser();
        var createdBy = currentUser.Name ?? "System";

        var containers = request.Items
            .Select(item => new MaterialContainer(
                item.Code, item.MaterialCode, item.LotCode,
                item.Amount, item.Unit, createdBy, item.PurchaseOrderLineId))
            .ToList();

        await _containerRepository.AddRangeAsync(containers, cancellationToken);
        await _containerRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created {Count} MaterialContainers", containers.Count);

        return new CreateMaterialContainersResponse { Containers = containers.Select(MapToDto).ToList() };
    }

    internal static MaterialContainerDto MapToDto(MaterialContainer c) => new()
    {
        Id = c.Id,
        Code = c.Code,
        MaterialCode = c.MaterialCode,
        LotCode = c.LotCode,
        Amount = c.Amount,
        Unit = c.Unit,
        Status = c.Status.ToString(),
        CreatedAt = c.CreatedAt,
        CreatedBy = c.CreatedBy,
        PurchaseOrderLineId = c.PurchaseOrderLineId
    };
}
