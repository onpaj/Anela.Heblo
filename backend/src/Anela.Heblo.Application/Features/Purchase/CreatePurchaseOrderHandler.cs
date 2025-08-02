using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Purchase;

public class CreatePurchaseOrderHandler : IRequestHandler<CreatePurchaseOrderRequest, CreatePurchaseOrderResponse>
{
    private readonly ILogger<CreatePurchaseOrderHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly IPurchaseOrderNumberGenerator _orderNumberGenerator;
    private readonly ICatalogRepository _catalogRepository;

    public CreatePurchaseOrderHandler(
        ILogger<CreatePurchaseOrderHandler> logger,
        IPurchaseOrderRepository repository,
        IPurchaseOrderNumberGenerator orderNumberGenerator,
        ICatalogRepository catalogRepository)
    {
        _logger = logger;
        _repository = repository;
        _orderNumberGenerator = orderNumberGenerator;
        _catalogRepository = catalogRepository;
    }

    public async Task<CreatePurchaseOrderResponse> Handle(CreatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating new purchase order for supplier {SupplierName}", request.SupplierName);

        // Parse dates from string format and ensure UTC for PostgreSQL compatibility
        var orderDate = DateTime.SpecifyKind(DateTime.Parse(request.OrderDate).Date, DateTimeKind.Utc);
        var expectedDeliveryDate = !string.IsNullOrEmpty(request.ExpectedDeliveryDate) 
            ? DateTime.SpecifyKind(DateTime.Parse(request.ExpectedDeliveryDate).Date, DateTimeKind.Utc)
            : (DateTime?)null;

        var orderNumber = await _orderNumberGenerator.GenerateOrderNumberAsync(orderDate, cancellationToken);

        var purchaseOrder = new PurchaseOrder(
            orderNumber,
            request.SupplierName,
            orderDate,
            expectedDeliveryDate,
            request.Notes,
            "System"); // TODO: Get actual user from context

        // Add lines if provided
        if (request.Lines != null && request.Lines.Any())
        {
            _logger.LogInformation("Adding {LineCount} lines to purchase order {OrderNumber}", 
                request.Lines.Count, orderNumber);
                
            foreach (var lineRequest in request.Lines)
            {
                // Look up material by ProductCode in catalog
                var material = await _catalogRepository.GetByIdAsync(lineRequest.MaterialId, cancellationToken);
                if (material == null)
                {
                    _logger.LogWarning("Material with code {MaterialId} not found in catalog, using placeholder", lineRequest.MaterialId);
                }

                // Use a deterministic GUID based on ProductCode for consistency
                var materialId = Guid.TryParse(lineRequest.MaterialId, out var parsedId) 
                    ? parsedId 
                    : GenerateGuidFromString(lineRequest.MaterialId);

                purchaseOrder.AddLine(
                    materialId,
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

        return await MapToResponseAsync(purchaseOrder, cancellationToken);
    }

    private static Guid GenerateGuidFromString(string input)
    {
        // Create a deterministic GUID from string for consistency
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    private async Task<CreatePurchaseOrderResponse> MapToResponseAsync(PurchaseOrder purchaseOrder, CancellationToken cancellationToken)
    {
        var lines = new List<PurchaseOrderLineDto>();
        
        foreach (var line in purchaseOrder.Lines)
        {
            // Try to get material name from catalog
            var material = await _catalogRepository.GetByIdAsync(line.MaterialId.ToString(), cancellationToken);
            var materialName = material?.ProductName ?? "Unknown Material";

            lines.Add(new PurchaseOrderLineDto(
                line.Id,
                line.MaterialId,
                materialName,
                line.Quantity,
                line.UnitPrice,
                line.LineTotal,
                line.Notes));
        }

        var history = purchaseOrder.History.Select(h => new PurchaseOrderHistoryDto(
            h.Id,
            h.Action,
            h.OldValue,
            h.NewValue,
            h.ChangedAt,
            h.ChangedBy)).ToList();

        return new CreatePurchaseOrderResponse(
            purchaseOrder.Id,
            purchaseOrder.OrderNumber,
            Guid.Empty, // No longer using SupplierId
            purchaseOrder.SupplierName,
            purchaseOrder.OrderDate,
            purchaseOrder.ExpectedDeliveryDate,
            purchaseOrder.Status.ToString(),
            purchaseOrder.Notes,
            purchaseOrder.TotalAmount,
            lines,
            history,
            purchaseOrder.CreatedAt,
            purchaseOrder.CreatedBy,
            purchaseOrder.UpdatedAt,
            purchaseOrder.UpdatedBy
        );
    }
}