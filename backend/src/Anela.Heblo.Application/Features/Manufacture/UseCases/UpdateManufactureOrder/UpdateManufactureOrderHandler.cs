using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderHandler : IRequestHandler<UpdateManufactureOrderRequest, UpdateManufactureOrderResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateManufactureOrderHandler> _logger;

    public UpdateManufactureOrderHandler(
        IManufactureOrderRepository repository,
        ICurrentUserService currentUserService,
        ILogger<UpdateManufactureOrderHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<UpdateManufactureOrderResponse> Handle(UpdateManufactureOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _repository.GetOrderByIdAsync(request.Id, cancellationToken);

            if (order == null)
            {
                return new UpdateManufactureOrderResponse(Application.Shared.ErrorCodes.ResourceNotFound,
                    new Dictionary<string, string> { { "id", request.Id.ToString() } });
            }

            // Update basic properties only if provided
            if (request.SemiProductPlannedDate.HasValue)
                order.SemiProductPlannedDate = request.SemiProductPlannedDate.Value;

            if (request.ProductPlannedDate.HasValue)
                order.ProductPlannedDate = request.ProductPlannedDate.Value;

            if (request.ResponsiblePerson != null)
                order.ResponsiblePerson = request.ResponsiblePerson;

            if (request.ErpOrderNumberSemiproduct != null)
                order.ErpOrderNumberSemiproduct = request.ErpOrderNumberSemiproduct;

            if (request.ErpOrderNumberProduct != null)
                order.ErpOrderNumberProduct = request.ErpOrderNumberProduct;

            // Update semi-product if provided
            if (request.SemiProduct != null)
            {
                if (request.SemiProduct.PlannedQuantity != null)
                    order.SemiProduct.PlannedQuantity = request.SemiProduct.PlannedQuantity.Value;

                if (request.SemiProduct.LotNumber != null)
                    order.SemiProduct.LotNumber = request.SemiProduct.LotNumber;

                if (request.SemiProduct.ExpirationDate != null)
                    order.SemiProduct.ExpirationDate = request.SemiProduct.ExpirationDate;

                if (request.SemiProduct.ActualQuantity != null)
                    order.SemiProduct.ActualQuantity = request.SemiProduct.ActualQuantity.Value;
            }

            // Update products only if provided
            if (request.Products.Any())
            {
                // Check if this is updating existing products (by Id) or replacing all products
                bool isUpdatingExistingProducts = request.Products.All(p => p.Id.HasValue);

                if (isUpdatingExistingProducts)
                {
                    // Update existing products - only update specified fields
                    foreach (var productRequest in request.Products)
                    {
                        var existingProduct = order.Products.FirstOrDefault(p => p.Id == productRequest.Id.Value);
                        if (existingProduct != null)
                        {
                            if (productRequest.PlannedQuantity.HasValue)
                                existingProduct.PlannedQuantity = (decimal)productRequest.PlannedQuantity.Value;

                            if (productRequest.ActualQuantity.HasValue)
                                existingProduct.ActualQuantity = productRequest.ActualQuantity.Value;

                            if (!string.IsNullOrEmpty(productRequest.ProductCode))
                                existingProduct.ProductCode = productRequest.ProductCode;

                            if (!string.IsNullOrEmpty(productRequest.ProductName))
                                existingProduct.ProductName = productRequest.ProductName;

                            existingProduct.ExpirationDate = order.SemiProduct!.ExpirationDate;
                            existingProduct.LotNumber = order.SemiProduct!.LotNumber;
                        }
                    }
                }
                else
                {
                    // Replace all products (original behavior for product creation/full update)
                    order.Products.Clear();
                    foreach (var productRequest in request.Products)
                    {
                        order.Products.Add(new ManufactureOrderProduct
                        {
                            ProductCode = productRequest.ProductCode!,
                            ProductName = productRequest.ProductName!,
                            PlannedQuantity = (decimal)productRequest.PlannedQuantity!.Value,
                            ActualQuantity = productRequest.ActualQuantity ?? (decimal)productRequest.PlannedQuantity!.Value,
                            SemiProductCode = order.SemiProduct!.ProductCode,
                            ExpirationDate = order.SemiProduct!.ExpirationDate,
                            LotNumber = order.SemiProduct!.LotNumber,
                        });
                    }
                }
            }

            // Update manual action required if provided
            if (request.ManualActionRequired.HasValue)
                order.ManualActionRequired = request.ManualActionRequired.Value;

            // Add note if provided
            if (!string.IsNullOrWhiteSpace(request.NewNote))
            {
                var currentUser = _currentUserService.GetCurrentUser();
                order.Notes.Add(new ManufactureOrderNote
                {
                    Text = request.NewNote.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUser = currentUser.Name
                });
            }

            var updatedOrder = await _repository.UpdateOrderAsync(order, cancellationToken);

            return new UpdateManufactureOrderResponse
            {
                Order = MapToDto(updatedOrder)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating manufacture order {OrderId}", request.Id);
            return new UpdateManufactureOrderResponse(Application.Shared.ErrorCodes.InternalServerError);
        }
    }

    private UpdateManufactureOrderDto MapToDto(ManufactureOrder order)
    {
        return new UpdateManufactureOrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CreatedDate = order.CreatedDate,
            CreatedByUser = order.CreatedByUser,
            ResponsiblePerson = order.ResponsiblePerson,
            SemiProductPlannedDate = order.SemiProductPlannedDate,
            ProductPlannedDate = order.ProductPlannedDate,
            State = order.State.ToString(),
            StateChangedAt = order.StateChangedAt,
            StateChangedByUser = order.StateChangedByUser,
            SemiProduct = new UpdateManufactureOrderSemiProductDto
            {
                Id = order.SemiProduct.Id,
                ProductCode = order.SemiProduct.ProductCode,
                ProductName = order.SemiProduct.ProductName,
                PlannedQuantity = order.SemiProduct.PlannedQuantity,
                ActualQuantity = order.SemiProduct.ActualQuantity,
                LotNumber = order.SemiProduct.LotNumber,
                ExpirationDate = order.SemiProduct.ExpirationDate
            },
            Products = order.Products.Select(p => new UpdateManufactureOrderProductDto
            {
                Id = p.Id,
                ProductCode = p.ProductCode,
                ProductName = p.ProductName,
                SemiProductCode = p.SemiProductCode,
                PlannedQuantity = p.PlannedQuantity,
                ActualQuantity = p.ActualQuantity
            }).ToList(),
            Notes = order.Notes.Select(n => new UpdateManufactureOrderNoteDto
            {
                Id = n.Id,
                Text = n.Text,
                CreatedAt = n.CreatedAt,
                CreatedByUser = n.CreatedByUser
            }).ToList()
        };
    }
}