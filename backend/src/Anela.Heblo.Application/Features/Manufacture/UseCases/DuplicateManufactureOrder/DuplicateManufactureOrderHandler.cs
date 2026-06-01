using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DuplicateManufactureOrder;

public class DuplicateManufactureOrderHandler : IRequestHandler<DuplicateManufactureOrderRequest, DuplicateManufactureOrderResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public DuplicateManufactureOrderHandler(
        IManufactureOrderRepository repository,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<DuplicateManufactureOrderResponse> Handle(
        DuplicateManufactureOrderRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();

        // Get the source order
        var sourceOrder = await _repository.GetOrderByIdAsync(request.SourceOrderId, cancellationToken);
        if (sourceOrder == null)
        {
            return new DuplicateManufactureOrderResponse(ErrorCodes.OrderNotFound);
        }

        // Generate unique order number for the duplicate
        var orderNumber = await _repository.GenerateOrderNumberAsync(cancellationToken);

        // Get the current date for lot number and expiration calculations
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);

        // Create the duplicate order
        var duplicateOrder = new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = DateTime.UtcNow,
            CreatedByUser = currentUser.Name,
            ResponsiblePerson = sourceOrder.ResponsiblePerson,
            PlannedDate = today,
            State = ManufactureOrderState.Draft,
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = currentUser.Name
        };

        var expirationDate = ManufactureOrderExtensions.GetDefaultExpiration(_timeProvider.GetUtcNow().DateTime, sourceOrder.SemiProduct.ExpirationMonths);
        var lotNumber = ManufactureOrderExtensions.GetDefaultLot(_timeProvider.GetUtcNow().DateTime);

        // Duplicate the semi-product with updated lot number and expiration date
        if (sourceOrder.SemiProduct != null)
        {
            var semiProduct = new ManufactureOrderSemiProduct
            {
                ProductCode = sourceOrder.SemiProduct.ProductCode,
                ProductName = sourceOrder.SemiProduct.ProductName,
                PlannedQuantity = sourceOrder.SemiProduct.PlannedQuantity,
                ActualQuantity = sourceOrder.SemiProduct.PlannedQuantity, // Reset actual to planned
                BatchMultiplier = sourceOrder.SemiProduct.BatchMultiplier,
                ExpirationMonths = sourceOrder.SemiProduct.ExpirationMonths,
                ExpirationDate = expirationDate,
                LotNumber = lotNumber,
            };
            duplicateOrder.SemiProduct = semiProduct;
        }

        // Duplicate the products with same quantities
        foreach (var sourceProduct in sourceOrder.Products)
        {

            var product = new ManufactureOrderProduct
            {
                ProductCode = sourceProduct.ProductCode,
                ProductName = sourceProduct.ProductName,
                SemiProductCode = sourceProduct.SemiProductCode,
                PlannedQuantity = sourceProduct.PlannedQuantity,
                ActualQuantity = sourceProduct.PlannedQuantity, // Reset actual to planned
                ExpirationDate = expirationDate,
                LotNumber = lotNumber,
            };
            duplicateOrder.Products.Add(product);
        }


        // Save the duplicate order
        var createdOrder = await _repository.AddOrderAsync(duplicateOrder, cancellationToken);

        return new DuplicateManufactureOrderResponse
        {
            Id = createdOrder.Id,
            OrderNumber = createdOrder.OrderNumber
        };
    }


}