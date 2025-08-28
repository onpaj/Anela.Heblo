using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Application.Common.Extensions;
using Anela.Heblo.Xcc.Infrastructure;

namespace Anela.Heblo.Application.Features.Purchase;

public class CreatePurchaseOrderHandler : IRequestHandler<CreatePurchaseOrderRequest, CreatePurchaseOrderResponse>
{
    private readonly ILogger<CreatePurchaseOrderHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPurchaseOrderRepository _repository;
    private readonly IPurchaseOrderNumberGenerator _orderNumberGenerator;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICurrentUserService _currentUserService;

    public CreatePurchaseOrderHandler(
        ILogger<CreatePurchaseOrderHandler> logger,
        IUnitOfWork unitOfWork,
        IPurchaseOrderRepository repository,
        IPurchaseOrderNumberGenerator orderNumberGenerator,
        ICatalogRepository catalogRepository,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
        _repository = repository;
        _orderNumberGenerator = orderNumberGenerator;
        _catalogRepository = catalogRepository;
        _currentUserService = currentUserService;
    }

    public async Task<CreatePurchaseOrderResponse> Handle(CreatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating new purchase order for supplier {SupplierName}", request.SupplierName);

        // Using dispose pattern - SaveChangesAsync called automatically on dispose
        await using (_unitOfWork)
        {
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
                request.SupplierName,
                orderDate,
                expectedDeliveryDate,
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

            _logger.LogInformation("Purchase order {OrderNumber} created successfully with ID {Id}. Lines in DB: {LineCount}",
                orderNumber, purchaseOrder.Id, purchaseOrder.Lines.Count);

            return await MapToResponseAsync(purchaseOrder, cancellationToken);
        }
        // SaveChangesAsync is automatically called here when _unitOfWork is disposed
    }


    private async Task<CreatePurchaseOrderResponse> MapToResponseAsync(PurchaseOrder purchaseOrder, CancellationToken cancellationToken)
    {
        var lines = new List<PurchaseOrderLineDto>();

        foreach (var line in purchaseOrder.Lines)
        {
            lines.Add(new PurchaseOrderLineDto(
                line.Id,
                line.MaterialId,
                line.MaterialId, // Code is same as MaterialId
                line.MaterialName,
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
            0, // No longer using SupplierId
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