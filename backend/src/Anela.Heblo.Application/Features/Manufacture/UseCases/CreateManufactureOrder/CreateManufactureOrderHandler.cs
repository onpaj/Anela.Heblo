using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;

public class CreateManufactureOrderHandler : IRequestHandler<CreateManufactureOrderRequest, CreateManufactureOrderResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly IProductNameFormatter _productNameFormatter;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public CreateManufactureOrderHandler(
        IManufactureOrderRepository repository,
        IProductNameFormatter productNameFormatter,
        ICatalogRepository catalogRepository,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _productNameFormatter = productNameFormatter;
        _catalogRepository = catalogRepository;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
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
            PlannedDate = request.PlannedDate,
            ManufactureType = request.ManufactureType,
            State = ManufactureOrderState.Draft,
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = currentUser.Name
        };

        var semiproduct = await _catalogRepository.GetByIdAsync(request.ProductCode, cancellationToken);
        if (semiproduct == null)
        {
            return new CreateManufactureOrderResponse(ErrorCodes.ProductNotFound);
        }

        var expirationDate = ManufactureOrderExtensions.GetDefaultExpiration(_timeProvider.GetUtcNow().DateTime, semiproduct.Properties.ExpirationMonths);
        var lotNumber = ManufactureOrderExtensions.GetDefaultLot(_timeProvider.GetUtcNow().DateTime);

        // Create the semi-product entry only for multi-phase manufacturing
        if (request.ManufactureType == ManufactureType.MultiPhase)
        {
            var semiProduct = new ManufactureOrderSemiProduct
            {
                ProductCode = request.ProductCode,
                ProductName = _productNameFormatter.ShortProductName(semiproduct.ProductName),
                PlannedQuantity = (decimal)request.NewBatchSize,
                ActualQuantity = (decimal)request.NewBatchSize,
                BatchMultiplier = (decimal)request.ScaleFactor,
                ExpirationMonths = semiproduct.Properties.ExpirationMonths,
                ExpirationDate = expirationDate,
                LotNumber = lotNumber,
            };
            order.SemiProduct = semiProduct;
        }

        // Create final products from the request 
        foreach (var productRequest in request.Products)
        {
            var product = new ManufactureOrderProduct
            {
                ProductCode = productRequest.ProductCode,
                ProductName = productRequest.ProductName,
                SemiProductCode = request.ManufactureType == ManufactureType.MultiPhase ? request.ProductCode : null,
                PlannedQuantity = (decimal)productRequest.PlannedQuantity,
                ActualQuantity = (decimal)productRequest.PlannedQuantity,
                ExpirationDate = expirationDate,
                LotNumber = lotNumber,
            };
            order.Products.Add(product);
        }


        // Save the order
        var createdOrder = await _repository.AddOrderAsync(order, cancellationToken);

        return new CreateManufactureOrderResponse
        {
            Id = createdOrder.Id,
            OrderNumber = createdOrder.OrderNumber
        };
    }
}