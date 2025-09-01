using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Application.Common.Extensions;

namespace Anela.Heblo.Application.Features.Purchase;

public class CreatePurchaseOrderHandler : IRequestHandler<CreatePurchaseOrderRequest, CreatePurchaseOrderResponse>
{
    private readonly ILogger<CreatePurchaseOrderHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly IPurchaseOrderNumberGenerator _orderNumberGenerator;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISupplierRepository _supplierRepository;

    public CreatePurchaseOrderHandler(
        ILogger<CreatePurchaseOrderHandler> logger,
        IPurchaseOrderRepository repository,
        IPurchaseOrderNumberGenerator orderNumberGenerator,
        ICatalogRepository catalogRepository,
        ICurrentUserService currentUserService,
        ISupplierRepository supplierRepository)
    {
        _logger = logger;
        _repository = repository;
        _orderNumberGenerator = orderNumberGenerator;
        _catalogRepository = catalogRepository;
        _currentUserService = currentUserService;
        _supplierRepository = supplierRepository;
    }

    public async Task<CreatePurchaseOrderResponse> Handle(CreatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        // Get supplier by ID
        var supplier = await _supplierRepository.GetByIdAsync(request.SupplierId, cancellationToken);
        if (supplier == null)
        {
            throw new ArgumentException($"Supplier with ID {request.SupplierId} not found");
        }

        _logger.LogInformation("Creating new purchase order for supplier {SupplierName}", supplier.Name);

        // Parse dates from string format and ensure UTC for PostgreSQL compatibility
        var orderDate = request.OrderDate.ToUtcDateTime();
        var expectedDeliveryDate = request.ExpectedDeliveryDate.ToUtcDateTimeOrNull();

        var orderNumber = !string.IsNullOrEmpty(request.OrderNumber)
            ? request.OrderNumber
            : await _orderNumberGenerator.GenerateOrderNumberAsync(orderDate, cancellationToken);

        var currentUser = _currentUserService.GetCurrentUser();
        var createdBy = currentUser.Name ?? "System";

        var purchaseOrder = new PurchaseOrder(
            orderNumber,
            supplier.Id,
            supplier.Name,
            orderDate,
            expectedDeliveryDate,
            request.ContactVia,
            request.Notes,
            createdBy);

        // Add lines if provided
        if (request.Lines != null && request.Lines.Any())
        {
            _logger.LogInformation("Adding {LineCount} lines to purchase order {OrderNumber}",
                request.Lines.Count, orderNumber);

            foreach (var lineRequest in request.Lines)
            {
                // Look up material by ProductCode in catalog to get ProductName
                var material = await _catalogRepository.GetByIdAsync(lineRequest.MaterialId, cancellationToken);
                var materialName = material?.ProductName ?? lineRequest.Name ?? "Unknown Material";

                if (material == null)
                {
                    _logger.LogWarning("Material with code {MaterialId} not found in catalog, using provided name: {Name}",
                        lineRequest.MaterialId, materialName);
                }

                purchaseOrder.AddLine(
                    lineRequest.MaterialId,
                    materialName,
                    lineRequest.Quantity,
                    lineRequest.UnitPrice,
                    lineRequest.Notes);
            }
        }

        _logger.LogInformation("Purchase order {OrderNumber} has {LineCount} lines before saving",
            orderNumber, purchaseOrder.Lines.Count);

        await _repository.AddAsync(purchaseOrder, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Purchase order {OrderNumber} created successfully with ID {Id}. Lines in DB: {LineCount}",
            orderNumber, purchaseOrder.Id, purchaseOrder.Lines.Count);

        return await MapToResponseAsync(purchaseOrder, request.SupplierId, cancellationToken);
    }


    private async Task<CreatePurchaseOrderResponse> MapToResponseAsync(PurchaseOrder purchaseOrder, long supplierId, CancellationToken cancellationToken)
    {
        var lines = new List<PurchaseOrderLineDto>();

        foreach (var line in purchaseOrder.Lines)
        {
            lines.Add(new PurchaseOrderLineDto
            {
                Id = line.Id,
                MaterialId = line.MaterialId,
                Code = line.MaterialId, // Code is same as MaterialId
                MaterialName = line.MaterialName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal,
                Notes = line.Notes
            });
        }

        var history = purchaseOrder.History.Select(h => new PurchaseOrderHistoryDto
        {
            Id = h.Id,
            Action = h.Action,
            OldValue = h.OldValue,
            NewValue = h.NewValue,
            ChangedAt = h.ChangedAt,
            ChangedBy = h.ChangedBy
        }).ToList();

        return new CreatePurchaseOrderResponse
        {
            Id = purchaseOrder.Id,
            OrderNumber = purchaseOrder.OrderNumber,
            SupplierId = supplierId,
            SupplierName = purchaseOrder.SupplierName,
            OrderDate = purchaseOrder.OrderDate,
            ExpectedDeliveryDate = purchaseOrder.ExpectedDeliveryDate,
            ContactVia = purchaseOrder.ContactVia,
            Status = purchaseOrder.Status.ToString(),
            Notes = purchaseOrder.Notes,
            TotalAmount = purchaseOrder.TotalAmount,
            Lines = lines,
            History = history,
            CreatedAt = purchaseOrder.CreatedAt,
            CreatedBy = purchaseOrder.CreatedBy,
            UpdatedAt = purchaseOrder.UpdatedAt,
            UpdatedBy = purchaseOrder.UpdatedBy
        };
    }
}