using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;

public class CreateManufactureOrderHandler : IRequestHandler<CreateManufactureOrderRequest, CreateManufactureOrderResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public CreateManufactureOrderHandler(
        IManufactureOrderRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<CreateManufactureOrderResponse> Handle(
        CreateManufactureOrderRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();

        // Generate unique order number
        var orderNumber = await _repository.GenerateOrderNumberAsync(cancellationToken);

        // Create the manufacture order
        var order = new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = DateTime.UtcNow,
            CreatedByUser = currentUser.Name,
            ResponsiblePerson = request.ResponsiblePerson,
            SemiProductPlannedDate = request.SemiProductPlannedDate,
            ProductPlannedDate = request.ProductPlannedDate,
            State = ManufactureOrderState.Draft,
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = currentUser.Name
        };

        // Create the semi-product entry (the main product being manufactured)
        var semiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            PlannedQuantity = (decimal)request.NewBatchSize,
            ActualQuantity = (decimal)request.NewBatchSize
        };
        order.SemiProducts.Add(semiProduct);

        // For now, we don't create final products as they would be created in a later phase
        // The issue description suggests this comes after semi-product manufacturing

        // Create initial audit log entry
        var auditLog = new ManufactureOrderAuditLog
        {
            Timestamp = DateTime.UtcNow,
            User = currentUser.Name,
            Action = ManufactureOrderAuditAction.OrderCreated,
            Details = $"Order created from batch calculation. Target batch size: {request.NewBatchSize}g (scale factor: {request.ScaleFactor:F3})",
            NewValue = ManufactureOrderState.Draft.ToString()
        };
        order.AuditLog.Add(auditLog);

        // Save the order
        var createdOrder = await _repository.AddOrderAsync(order, cancellationToken);

        return new CreateManufactureOrderResponse
        {
            Id = createdOrder.Id,
            OrderNumber = createdOrder.OrderNumber
        };
    }
}