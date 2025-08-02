using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase;

public class UpdatePurchaseOrderHandler : IRequestHandler<UpdatePurchaseOrderRequest, UpdatePurchaseOrderResponse?>
{
    private readonly ILogger<UpdatePurchaseOrderHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;

    public UpdatePurchaseOrderHandler(
        ILogger<UpdatePurchaseOrderHandler> logger,
        IPurchaseOrderRepository repository)
    {
        _logger = logger;
        _repository = repository;
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
            purchaseOrder.Update(request.ExpectedDeliveryDate, request.Notes, "System"); // TODO: Get actual user

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
                        lineRequest.Quantity,
                        lineRequest.UnitPrice,
                        lineRequest.Notes);
                }
                else
                {
                    purchaseOrder.AddLine(
                        lineRequest.MaterialId,
                        lineRequest.Quantity,
                        lineRequest.UnitPrice,
                        lineRequest.Notes);
                }
            }

            await _repository.UpdateAsync(purchaseOrder, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Purchase order {OrderNumber} updated successfully", purchaseOrder.OrderNumber);

            return new UpdatePurchaseOrderResponse(
                purchaseOrder.Id,
                purchaseOrder.OrderNumber,
                purchaseOrder.SupplierId,
                purchaseOrder.OrderDate,
                purchaseOrder.ExpectedDeliveryDate,
                purchaseOrder.Status.ToString(),
                purchaseOrder.Notes,
                purchaseOrder.TotalAmount,
                purchaseOrder.Lines.Select(l => new PurchaseOrderLineDto(
                    l.Id,
                    l.MaterialId,
                    "Unknown Material", // TODO: Join with material catalog
                    l.Quantity,
                    l.UnitPrice,
                    l.LineTotal,
                    l.Notes
                )).ToList(),
                purchaseOrder.UpdatedAt,
                purchaseOrder.UpdatedBy
            );
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Cannot update purchase order {OrderNumber}: {Message}",
                purchaseOrder.OrderNumber, ex.Message);
            throw;
        }
    }
}