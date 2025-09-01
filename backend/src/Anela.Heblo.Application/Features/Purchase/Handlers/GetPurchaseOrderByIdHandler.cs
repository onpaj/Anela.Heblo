using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Purchase;

public class GetPurchaseOrderByIdHandler : IRequestHandler<GetPurchaseOrderByIdRequest, GetPurchaseOrderByIdResponse?>
{
    private readonly ILogger<GetPurchaseOrderByIdHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly ICatalogRepository _catalogRepository;

    public GetPurchaseOrderByIdHandler(
        ILogger<GetPurchaseOrderByIdHandler> logger,
        IPurchaseOrderRepository repository,
        ISupplierRepository supplierRepository,
        ICatalogRepository catalogRepository)
    {
        _logger = logger;
        _repository = repository;
        _supplierRepository = supplierRepository;
        _catalogRepository = catalogRepository;
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

        // Load supplier details to get the note
        var supplier = await _supplierRepository.GetByIdAsync(purchaseOrder.SupplierId, cancellationToken);
        var supplierNote = supplier?.Description;

        // Load catalog items to get notes for each material
        var materialIds = purchaseOrder.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var catalogItems = new Dictionary<string, CatalogAggregate>();

        foreach (var materialId in materialIds)
        {
            var catalogItem = await _catalogRepository.GetByIdAsync(materialId, cancellationToken);
            if (catalogItem != null)
            {
                catalogItems[materialId] = catalogItem;
            }
        }

        return new GetPurchaseOrderByIdResponse
        {
            Id = purchaseOrder.Id,
            OrderNumber = purchaseOrder.OrderNumber,
            SupplierId = purchaseOrder.SupplierId,
            SupplierName = purchaseOrder.SupplierName,
            SupplierNote = supplierNote,
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
                Notes = l.Notes,
                CatalogNote = catalogItems.TryGetValue(l.MaterialId, out var catalogItem) ? catalogItem.Note : null
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