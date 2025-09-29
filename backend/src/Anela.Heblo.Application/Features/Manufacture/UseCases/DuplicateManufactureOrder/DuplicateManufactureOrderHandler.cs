using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DuplicateManufactureOrder;

public class DuplicateManufactureOrderHandler : IRequestHandler<DuplicateManufactureOrderRequest, DuplicateManufactureOrderResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public DuplicateManufactureOrderHandler(
        IManufactureOrderRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
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
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Create the duplicate order
        var duplicateOrder = new ManufactureOrder
        {
            OrderNumber = orderNumber,
            CreatedDate = DateTime.UtcNow,
            CreatedByUser = currentUser.Name,
            ResponsiblePerson = sourceOrder.ResponsiblePerson,
            SemiProductPlannedDate = today,
            ProductPlannedDate = today,
            State = ManufactureOrderState.Draft,
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = currentUser.Name
        };

        // Duplicate the semi-product with updated lot number and expiration date
        if (sourceOrder.SemiProduct != null)
        {
            // Calculate lot number in wwyyyyMM format
            var year = today.Year;
            var month = today.Month.ToString("D2");
            var week = GetWeekNumber(today.ToDateTime(TimeOnly.MinValue)).ToString("D2");
            var lotNumber = $"{week}{year}{month}";

            // Calculate expiration date (last day of month after adding expiration months)
            var expirationMonths = sourceOrder.SemiProduct.ExpirationMonths;
            var expirationDate = today.AddMonths(expirationMonths);
            var lastDayOfExpirationMonth = DateOnly.FromDateTime(new DateTime(expirationDate.Year, expirationDate.Month, 1).AddMonths(1).AddDays(-1));

            var semiProduct = new ManufactureOrderSemiProduct
            {
                ProductCode = sourceOrder.SemiProduct.ProductCode,
                ProductName = sourceOrder.SemiProduct.ProductName,
                PlannedQuantity = sourceOrder.SemiProduct.PlannedQuantity,
                ActualQuantity = sourceOrder.SemiProduct.PlannedQuantity, // Reset actual to planned
                BatchMultiplier = sourceOrder.SemiProduct.BatchMultiplier,
                ExpirationMonths = sourceOrder.SemiProduct.ExpirationMonths,
                LotNumber = lotNumber,
                ExpirationDate = lastDayOfExpirationMonth
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
            };
            duplicateOrder.Products.Add(product);
        }

        // Create initial audit log entry
        var auditLog = new ManufactureOrderAuditLog
        {
            Timestamp = DateTime.UtcNow,
            User = currentUser.Name,
            Action = ManufactureOrderAuditAction.OrderCreated,
            Details = $"Order duplicated from order #{sourceOrder.OrderNumber}. Dates updated to current date.",
            NewValue = ManufactureOrderState.Draft.ToString()
        };
        duplicateOrder.AuditLog.Add(auditLog);

        // Save the duplicate order
        var createdOrder = await _repository.AddOrderAsync(duplicateOrder, cancellationToken);

        return new DuplicateManufactureOrderResponse
        {
            Id = createdOrder.Id,
            OrderNumber = createdOrder.OrderNumber
        };
    }

    // Helper function to get ISO week number
    private static int GetWeekNumber(DateTime date)
    {
        var d = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
        var dayNum = (int)d.DayOfWeek;
        if (dayNum == 0) dayNum = 7; // Sunday should be 7, not 0
        d = d.AddDays(4 - dayNum);
        var yearStart = new DateTime(d.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (int)Math.Ceiling(((d - yearStart).TotalDays + 1) / 7);
    }
}