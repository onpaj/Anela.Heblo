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

        var currentUser = _currentUserService.GetCurrentUser();
        var updatedBy = currentUser.Name ?? "System";

        var toAssign = new List<(MaterialContainer Container, CreateMaterialContainerItem Item)>();

        foreach (var item in request.Items)
        {
            var existing = await _containerRepository.GetByCodeAsync(item.Code, cancellationToken);

            if (existing is null)
            {
                return new CreateMaterialContainersResponse(
                    ErrorCodes.UnknownMaterialContainerCode,
                    new Dictionary<string, string> { { "Code", item.Code } });
            }

            if (existing.Status != MaterialContainerStatus.Unassigned)
            {
                return new CreateMaterialContainersResponse(
                    ErrorCodes.MaterialContainerCodeExists,
                    new Dictionary<string, string>
                    {
                        { "Code", item.Code },
                        { "MaterialCode", existing.MaterialCode ?? string.Empty },
                        { "LotCode", existing.LotCode ?? string.Empty },
                        { "Status", existing.Status.ToString() }
                    });
            }

            if (item.PurchaseOrderLineId.HasValue)
            {
                var line = await _purchaseOrderRepository.GetLineByIdAsync(item.PurchaseOrderLineId.Value, cancellationToken);
                if (line == null)
                {
                    return new CreateMaterialContainersResponse(
                        ErrorCodes.PurchaseOrderLineNotFound,
                        new Dictionary<string, string> { { "PurchaseOrderLineId", item.PurchaseOrderLineId.Value.ToString() } });
                }
            }

            toAssign.Add((existing, item));
        }

        foreach (var (container, item) in toAssign)
        {
            container.Assign(item.MaterialCode, item.LotCode, item.Amount, item.Unit, item.PurchaseOrderLineId, updatedBy);
        }

        await _containerRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Assigned {Count} MaterialContainers", toAssign.Count);

        return new CreateMaterialContainersResponse { Containers = toAssign.Select(t => MapToDto(t.Container)).ToList() };
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
