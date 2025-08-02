using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase;

public class CreatePurchaseOrderHandler : IRequestHandler<CreatePurchaseOrderRequest, CreatePurchaseOrderResponse>
{
    private readonly ILogger<CreatePurchaseOrderHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;
    private readonly IPurchaseOrderNumberGenerator _orderNumberGenerator;

    public CreatePurchaseOrderHandler(
        ILogger<CreatePurchaseOrderHandler> logger,
        IPurchaseOrderRepository repository,
        IPurchaseOrderNumberGenerator orderNumberGenerator)
    {
        _logger = logger;
        _repository = repository;
        _orderNumberGenerator = orderNumberGenerator;
    }

    public async Task<CreatePurchaseOrderResponse> Handle(CreatePurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating new purchase order for supplier {SupplierId}", request.SupplierId);

        var orderNumber = await _orderNumberGenerator.GenerateOrderNumberAsync(request.OrderDate, cancellationToken);

        var purchaseOrder = new PurchaseOrder(
            orderNumber,
            request.SupplierId,
            request.OrderDate,
            request.ExpectedDeliveryDate,
            request.Notes,
            "System"); // TODO: Get actual user from context

        foreach (var lineRequest in request.Lines)
        {
            purchaseOrder.AddLine(
                lineRequest.MaterialId,
                lineRequest.Quantity,
                lineRequest.UnitPrice,
                lineRequest.Notes);
        }

        await _repository.AddAsync(purchaseOrder, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Purchase order {OrderNumber} created successfully with ID {Id}",
            orderNumber, purchaseOrder.Id);

        return MapToResponse(purchaseOrder);
    }

    private static CreatePurchaseOrderResponse MapToResponse(PurchaseOrder purchaseOrder)
    {
        return new CreatePurchaseOrderResponse(
            purchaseOrder.Id,
            purchaseOrder.OrderNumber,
            purchaseOrder.SupplierId,
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
            purchaseOrder.CreatedAt,
            purchaseOrder.CreatedBy
        );
    }
}