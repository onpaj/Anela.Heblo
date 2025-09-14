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

            // Update basic properties
            order.SemiProductPlannedDate = request.SemiProductPlannedDate;
            order.ProductPlannedDate = request.ProductPlannedDate;
            order.ResponsiblePerson = request.ResponsiblePerson;

            // Update semi-product if provided
            if (request.SemiProduct != null && order.SemiProduct != null)
            {
                order.SemiProduct.LotNumber = request.SemiProduct.LotNumber;
                order.SemiProduct.ExpirationDate = request.SemiProduct.ExpirationDate;
            }

            // Update products
            order.Products.Clear();
            foreach (var productRequest in request.Products)
            {
                order.Products.Add(new ManufactureOrderProduct
                {
                    ProductCode = productRequest.ProductCode,
                    ProductName = productRequest.ProductName,
                    PlannedQuantity = (decimal)productRequest.PlannedQuantity,
                    SemiProductCode = order.SemiProduct!.ProductCode
                });
            }

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
            SemiProduct = order.SemiProduct != null ? new UpdateManufactureOrderSemiProductDto
            {
                Id = order.SemiProduct.Id,
                ProductCode = order.SemiProduct.ProductCode,
                ProductName = order.SemiProduct.ProductName,
                PlannedQuantity = order.SemiProduct.PlannedQuantity,
                ActualQuantity = order.SemiProduct.ActualQuantity,
                LotNumber = order.SemiProduct.LotNumber,
                ExpirationDate = order.SemiProduct.ExpirationDate
            } : null,
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