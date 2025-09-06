using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderStatus;

public class UpdatePurchaseOrderStatusHandler : IRequestHandler<UpdatePurchaseOrderStatusRequest, UpdatePurchaseOrderStatusResponse>
{
    private readonly ILogger<UpdatePurchaseOrderStatusHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public UpdatePurchaseOrderStatusHandler(
        ILogger<UpdatePurchaseOrderStatusHandler> logger,
        IPurchaseOrderRepository repository,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<UpdatePurchaseOrderStatusResponse> Handle(UpdatePurchaseOrderStatusRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating purchase order status for ID {Id} to {Status}", request.Id, request.Status);

        var purchaseOrder = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (purchaseOrder == null)
        {
            _logger.LogWarning("Purchase order not found for ID {Id}", request.Id);
            return new UpdatePurchaseOrderStatusResponse(ErrorCodes.PurchaseOrderNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        if (!Enum.TryParse<PurchaseOrderStatus>(request.Status, out var newStatus))
        {
            _logger.LogWarning("Invalid status {Status} for purchase order {OrderNumber}",
                request.Status, purchaseOrder.OrderNumber);
            return new UpdatePurchaseOrderStatusResponse(ErrorCodes.InvalidPurchaseOrderStatus, new Dictionary<string, string> { { "Status", request.Status }, { "OrderNumber", purchaseOrder.OrderNumber } });
        }

        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var updatedBy = currentUser.Name ?? "System";

            purchaseOrder.ChangeStatus(newStatus, updatedBy);

            await _repository.UpdateAsync(purchaseOrder, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Purchase order {OrderNumber} status updated to {Status}",
                purchaseOrder.OrderNumber, newStatus);

            return new UpdatePurchaseOrderStatusResponse
            {
                Id = purchaseOrder.Id,
                OrderNumber = purchaseOrder.OrderNumber,
                Status = purchaseOrder.Status.ToString(),
                UpdatedAt = purchaseOrder.UpdatedAt,
                UpdatedBy = purchaseOrder.UpdatedBy
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Cannot update status for purchase order {OrderNumber}: {Message}",
                purchaseOrder.OrderNumber, ex.Message);
            return new UpdatePurchaseOrderStatusResponse(ErrorCodes.StatusTransitionNotAllowed, new Dictionary<string, string> { { "OrderNumber", purchaseOrder.OrderNumber }, { "Message", ex.Message } });
        }
    }
}