using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderById;

public class GetPurchaseOrderByIdHandler : IRequestHandler<GetPurchaseOrderByIdRequest, GetPurchaseOrderByIdResponse>
{
    private readonly ILogger<GetPurchaseOrderByIdHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IMaterialCatalogService _materialCatalog;

    public GetPurchaseOrderByIdHandler(
        ILogger<GetPurchaseOrderByIdHandler> logger,
        IPurchaseOrderRepository repository,
        ISupplierRepository supplierRepository,
        IMaterialCatalogService materialCatalog)
    {
        _logger = logger;
        _repository = repository;
        _supplierRepository = supplierRepository;
        _materialCatalog = materialCatalog;
    }

    public async Task<GetPurchaseOrderByIdResponse> Handle(GetPurchaseOrderByIdRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting purchase order details for ID {Id}", request.Id);

        var purchaseOrder = await _repository.GetByIdWithDetailsAsync(request.Id, cancellationToken);

        if (purchaseOrder == null)
        {
            _logger.LogWarning("Purchase order not found for ID {Id}", request.Id);
            return new GetPurchaseOrderByIdResponse(ErrorCodes.PurchaseOrderNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        _logger.LogInformation("Found purchase order {OrderNumber} with {LineCount} lines and {HistoryCount} history entries",
            purchaseOrder.OrderNumber, purchaseOrder.Lines.Count, purchaseOrder.History.Count);

        // Load supplier details to get the note
        var supplier = await _supplierRepository.GetByIdAsync(purchaseOrder.SupplierId, cancellationToken);
        var supplierNote = supplier?.Description;

        // Batch-load catalog items to get notes for each material (replaces per-id N+1 loop)
        var materialIds = purchaseOrder.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var materialLookup = materialIds.Count > 0
            ? await _materialCatalog.GetByIdsAsync(materialIds, cancellationToken)
            : (IReadOnlyDictionary<string, MaterialInfo>)new Dictionary<string, MaterialInfo>();

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
                CatalogNote = materialLookup.TryGetValue(l.MaterialId, out var material) ? material.Note : null
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