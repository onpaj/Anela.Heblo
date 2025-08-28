using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Infrastructure;

namespace Anela.Heblo.Application.Features.Purchase;

public class UpdatePurchaseOrderStatusHandler : IRequestHandler<UpdatePurchaseOrderStatusRequest, UpdatePurchaseOrderStatusResponse?>
{
    private readonly ILogger<UpdatePurchaseOrderStatusHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPurchaseOrderRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public UpdatePurchaseOrderStatusHandler(
        ILogger<UpdatePurchaseOrderStatusHandler> logger,
        IUnitOfWork unitOfWork,
        IPurchaseOrderRepository repository,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
        _repository = repository;
        _currentUserService = currentUserService;
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

        // Using dispose pattern - SaveChangesAsync called automatically on dispose
        await using (_unitOfWork)
        {
            try
            {
                var currentUser = _currentUserService.GetCurrentUser();
                var updatedBy = currentUser.Name ?? "System";

                purchaseOrder.ChangeStatus(newStatus, updatedBy);

                await _repository.UpdateAsync(purchaseOrder, cancellationToken);

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
        // SaveChangesAsync is automatically called here when _unitOfWork is disposed
    }
}