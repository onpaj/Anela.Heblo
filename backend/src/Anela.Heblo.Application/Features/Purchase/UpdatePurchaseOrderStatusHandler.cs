using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase;

public class UpdatePurchaseOrderStatusHandler : IRequestHandler<UpdatePurchaseOrderStatusRequest, UpdatePurchaseOrderStatusResponse?>
{
    private readonly ILogger<UpdatePurchaseOrderStatusHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;

    public UpdatePurchaseOrderStatusHandler(
        ILogger<UpdatePurchaseOrderStatusHandler> logger,
        IPurchaseOrderRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<UpdatePurchaseOrderStatusResponse?> Handle(UpdatePurchaseOrderStatusRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating purchase order status for ID {Id} to {Status}", request.Id, request.Status);

        var purchaseOrder = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (purchaseOrder == null)
        {
            _logger.LogWarning("Purchase order not found for ID {Id}", request.Id);
            return null;
        }

        if (!Enum.TryParse<PurchaseOrderStatus>(request.Status, out var newStatus))
        {
            _logger.LogWarning("Invalid status {Status} for purchase order {OrderNumber}",
                request.Status, purchaseOrder.OrderNumber);
            throw new ArgumentException($"Invalid status: {request.Status}");
        }

        try
        {
            purchaseOrder.ChangeStatus(newStatus, "System"); // TODO: Get actual user from context

            await _repository.UpdateAsync(purchaseOrder, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Purchase order {OrderNumber} status updated to {Status}",
                purchaseOrder.OrderNumber, newStatus);

            return new UpdatePurchaseOrderStatusResponse(
                purchaseOrder.Id,
                purchaseOrder.OrderNumber,
                purchaseOrder.Status.ToString(),
                purchaseOrder.UpdatedAt,
                purchaseOrder.UpdatedBy
            );
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Cannot update status for purchase order {OrderNumber}: {Message}",
                purchaseOrder.OrderNumber, ex.Message);
            throw;
        }
    }
}