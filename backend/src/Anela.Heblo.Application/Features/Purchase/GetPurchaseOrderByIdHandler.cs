using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase;

public class GetPurchaseOrderByIdHandler : IRequestHandler<GetPurchaseOrderByIdRequest, GetPurchaseOrderByIdResponse?>
{
    private readonly ILogger<GetPurchaseOrderByIdHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;

    public GetPurchaseOrderByIdHandler(
        ILogger<GetPurchaseOrderByIdHandler> logger,
        IPurchaseOrderRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<GetPurchaseOrderByIdResponse?> Handle(GetPurchaseOrderByIdRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting purchase order details for ID {Id}", request.Id);

        var purchaseOrder = await _repository.GetByIdWithDetailsAsync(request.Id, cancellationToken);

        if (purchaseOrder == null)
        {
            _logger.LogWarning("Purchase order not found for ID {Id}", request.Id);
            return null;
        }

        _logger.LogInformation("Found purchase order {OrderNumber} with {LineCount} lines and {HistoryCount} history entries",
            purchaseOrder.OrderNumber, purchaseOrder.Lines.Count, purchaseOrder.History.Count);

        return new GetPurchaseOrderByIdResponse(
            purchaseOrder.Id,
            purchaseOrder.OrderNumber,
            Guid.Empty, // No longer using SupplierId
            purchaseOrder.SupplierName,
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
            purchaseOrder.History.Select(h => new PurchaseOrderHistoryDto(
                h.Id,
                h.Action,
                h.OldValue,
                h.NewValue,
                h.ChangedAt,
                h.ChangedBy
            )).OrderByDescending(h => h.ChangedAt).ToList(),
            purchaseOrder.CreatedAt,
            purchaseOrder.CreatedBy,
            purchaseOrder.UpdatedAt,
            purchaseOrder.UpdatedBy
        );
    }
}