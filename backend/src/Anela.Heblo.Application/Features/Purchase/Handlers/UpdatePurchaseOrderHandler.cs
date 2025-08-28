using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Infrastructure;

namespace Anela.Heblo.Application.Features.Purchase;

public class UpdatePurchaseOrderHandler : IRequestHandler<UpdatePurchaseOrderRequest, UpdatePurchaseOrderResponse?>
{
    private readonly ILogger<UpdatePurchaseOrderHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPurchaseOrderRepository _repository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICurrentUserService _currentUserService;

    public UpdatePurchaseOrderHandler(
        ILogger<UpdatePurchaseOrderHandler> logger,
        IUnitOfWork unitOfWork,
        IPurchaseOrderRepository repository,
        ICatalogRepository catalogRepository,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
        _repository = repository;
        _catalogRepository = catalogRepository;
        _currentUserService = currentUserService;
    }

    public async Task<UpdatePurchaseOrderResponse?> Handle(UpdatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating purchase order {Id}", request.Id);

        var purchaseOrder = await _repository.GetByIdWithDetailsAsync(request.Id, cancellationToken);

        if (purchaseOrder == null)
        {
            _logger.LogWarning("Purchase order not found for ID {Id}", request.Id);
            return null;
        }

        // Using dispose pattern - SaveChangesAsync called automatically on dispose
        await using (_unitOfWork)
        {
            try
            {
                var currentUser = _currentUserService.GetCurrentUser();
                var updatedBy = currentUser.Name ?? "System";

                // Update order number if provided
                if (!string.IsNullOrEmpty(request.OrderNumber) && request.OrderNumber != purchaseOrder.OrderNumber)
                {
                    purchaseOrder.UpdateOrderNumber(request.OrderNumber, updatedBy);
                }

                purchaseOrder.Update(request.SupplierName, request.ExpectedDeliveryDate, request.Notes, updatedBy);

                var existingLineIds = purchaseOrder.Lines.Select(l => l.Id).ToHashSet();
                var requestLineIds = request.Lines.Where(l => l.Id.HasValue).Select(l => l.Id!.Value).ToHashSet();

                var linesToRemove = existingLineIds.Except(requestLineIds).ToList();
                foreach (var lineId in linesToRemove)
                {
                    purchaseOrder.RemoveLine(lineId);
                }

                foreach (var lineRequest in request.Lines)
                {
                    if (lineRequest.Id.HasValue)
                    {
                        // Get material name from catalog if available
                        var material = await _catalogRepository.GetByIdAsync(lineRequest.MaterialId, cancellationToken);
                        var materialName = material?.ProductName ?? lineRequest.Name ?? "Unknown Material";

                        purchaseOrder.UpdateLine(
                            lineRequest.Id.Value,
                            materialName,
                            lineRequest.Quantity,
                            lineRequest.UnitPrice,
                            lineRequest.Notes);
                    }
                    else
                    {
                        // Get material name from catalog if available
                        var material = await _catalogRepository.GetByIdAsync(lineRequest.MaterialId, cancellationToken);
                        var materialName = material?.ProductName ?? lineRequest.Name ?? "Unknown Material";

                        purchaseOrder.AddLine(
                            lineRequest.MaterialId,
                            materialName,
                            lineRequest.Quantity,
                            lineRequest.UnitPrice,
                            lineRequest.Notes);
                    }
                }

                _logger.LogInformation("Purchase order {OrderNumber} updated successfully", purchaseOrder.OrderNumber);

                return await MapToResponseAsync(purchaseOrder, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Cannot update purchase order {OrderNumber}: {Message}",
                    purchaseOrder.OrderNumber, ex.Message);
                throw;
            }
        }
        // SaveChangesAsync is automatically called here when _unitOfWork is disposed
    }


    private async Task<UpdatePurchaseOrderResponse> MapToResponseAsync(PurchaseOrder purchaseOrder, CancellationToken cancellationToken)
    {
        var lines = new List<PurchaseOrderLineDto>();

        foreach (var line in purchaseOrder.Lines)
        {
            // Try to get material name from catalog
            var material = await _catalogRepository.GetByIdAsync(line.MaterialId, cancellationToken);
            var materialName = material?.ProductName ?? "Unknown Material";

            lines.Add(new PurchaseOrderLineDto(
                line.Id,
                line.MaterialId,
                line.MaterialId, // Code is same as MaterialId
                line.MaterialName,
                line.Quantity,
                line.UnitPrice,
                line.LineTotal,
                line.Notes));
        }

        return new UpdatePurchaseOrderResponse(
            purchaseOrder.Id,
            purchaseOrder.OrderNumber,
            0, // No longer using SupplierId
            purchaseOrder.SupplierName,
            purchaseOrder.OrderDate,
            purchaseOrder.ExpectedDeliveryDate,
            purchaseOrder.Status.ToString(),
            purchaseOrder.Notes,
            purchaseOrder.TotalAmount,
            lines,
            purchaseOrder.UpdatedAt,
            purchaseOrder.UpdatedBy
        );
    }
}