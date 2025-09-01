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

        return new GetPurchaseOrderByIdResponse
        {
            Id = purchaseOrder.Id,
            OrderNumber = purchaseOrder.OrderNumber,
            SupplierId = 0, // No longer using SupplierId
            SupplierName = purchaseOrder.SupplierName,
            OrderDate = purchaseOrder.OrderDate,
            ExpectedDeliveryDate = purchaseOrder.ExpectedDeliveryDate,
            ContactVia = purchaseOrder.ContactVia,
            Status = purchaseOrder.Status.ToString(),
            InvoiceAcquired = purchaseOrder.InvoiceAcquired,
            Notes = purchaseOrder.Notes,
            TotalAmount = purchaseOrder.TotalAmount,
            IsEditable = purchaseOrder.IsEditable,
            Lines = purchaseOrder.Lines.Select(l => new PurchaseOrderLineDto
            {
                Id = l.Id,
                MaterialId = l.MaterialId,
                Code = l.MaterialId, // Code is same as MaterialId
                MaterialName = l.MaterialName,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                LineTotal = l.LineTotal,
                Notes = l.Notes
            }).ToList(),
            History = purchaseOrder.History.Select(h => new PurchaseOrderHistoryDto
            {
                Id = h.Id,
                Action = h.Action,
                OldValue = h.OldValue,
                NewValue = h.NewValue,
                ChangedAt = h.ChangedAt,
                ChangedBy = h.ChangedBy
            }).OrderByDescending(h => h.ChangedAt).ToList(),
            CreatedAt = purchaseOrder.CreatedAt,
            CreatedBy = purchaseOrder.CreatedBy,
            UpdatedAt = purchaseOrder.UpdatedAt,
            UpdatedBy = purchaseOrder.UpdatedBy
        };
    }
}