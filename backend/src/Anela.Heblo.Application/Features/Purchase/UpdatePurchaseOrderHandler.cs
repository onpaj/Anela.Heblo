using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Purchase;

public class UpdatePurchaseOrderHandler : IRequestHandler<UpdatePurchaseOrderRequest, UpdatePurchaseOrderResponse?>
{
    private readonly ILogger<UpdatePurchaseOrderHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly ICatalogRepository _catalogRepository;

    public UpdatePurchaseOrderHandler(
        ILogger<UpdatePurchaseOrderHandler> logger,
        IPurchaseOrderRepository repository,
        ICatalogRepository catalogRepository)
    {
        _logger = logger;
        _repository = repository;
        _catalogRepository = catalogRepository;
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

        try
        {
            purchaseOrder.Update(request.SupplierName, request.ExpectedDeliveryDate, request.Notes, "System"); // TODO: Get actual user

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
                    purchaseOrder.UpdateLine(
                        lineRequest.Id.Value,
                        lineRequest.Code,
                        lineRequest.Name,
                        lineRequest.Quantity,
                        lineRequest.UnitPrice,
                        lineRequest.Notes);
                }
                else
                {
                    purchaseOrder.AddLine(
                        lineRequest.MaterialId,
                        lineRequest.Code,
                        lineRequest.Name,
                        lineRequest.Quantity,
                        lineRequest.UnitPrice,
                        lineRequest.Notes);
                }
            }

            // Entity is already tracked from GetByIdWithDetailsAsync, EF will auto-detect changes
            await _repository.SaveChangesAsync(cancellationToken);

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
                line.Code,
                line.Name,
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