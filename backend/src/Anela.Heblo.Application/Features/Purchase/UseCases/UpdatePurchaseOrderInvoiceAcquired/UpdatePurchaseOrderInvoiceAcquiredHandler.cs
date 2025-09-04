using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderInvoiceAcquired;

public class UpdatePurchaseOrderInvoiceAcquiredHandler : IRequestHandler<UpdatePurchaseOrderInvoiceAcquiredRequest, UpdatePurchaseOrderInvoiceAcquiredResponse?>
{
    private readonly ILogger<UpdatePurchaseOrderInvoiceAcquiredHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public UpdatePurchaseOrderInvoiceAcquiredHandler(
        ILogger<UpdatePurchaseOrderInvoiceAcquiredHandler> logger,
        IPurchaseOrderRepository repository,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<UpdatePurchaseOrderInvoiceAcquiredResponse?> Handle(UpdatePurchaseOrderInvoiceAcquiredRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating purchase order invoice acquired for ID {Id} to {InvoiceAcquired}", request.Id, request.InvoiceAcquired);

        var purchaseOrder = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (purchaseOrder == null)
        {
            _logger.LogWarning("Purchase order not found for ID {Id}", request.Id);
            return null;
        }

        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var updatedBy = currentUser.Name ?? "System";

            purchaseOrder.SetInvoiceAcquired(request.InvoiceAcquired, updatedBy);

            await _repository.UpdateAsync(purchaseOrder, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Purchase order {OrderNumber} invoice acquired updated to {InvoiceAcquired}",
                purchaseOrder.OrderNumber, request.InvoiceAcquired);

            return new UpdatePurchaseOrderInvoiceAcquiredResponse
            {
                Id = purchaseOrder.Id,
                InvoiceAcquired = purchaseOrder.InvoiceAcquired
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating invoice acquired for purchase order {OrderNumber}",
                purchaseOrder.OrderNumber);
            throw;
        }
    }
}