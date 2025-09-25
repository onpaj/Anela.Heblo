using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;

public class GetCalendarViewHandler : IRequestHandler<GetCalendarViewRequest, GetCalendarViewResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly ILogger<GetCalendarViewHandler> _logger;

    public GetCalendarViewHandler(
        IManufactureOrderRepository repository,
        ILogger<GetCalendarViewHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetCalendarViewResponse> Handle(GetCalendarViewRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Get orders that have dates within the requested range (excluding cancelled orders)
            var orders = await _repository.GetOrdersForDateRangeAsync(
                DateOnly.FromDateTime(request.StartDate),
                DateOnly.FromDateTime(request.EndDate),
                cancellationToken);

            // Filter out cancelled orders
            orders = orders.Where(o => o.State != ManufactureOrderState.Cancelled).ToList();

            var events = new List<CalendarEventDto>();

            foreach (var order in orders)
            {
                // Add semi-product event if date is within range
                if (order.SemiProductPlannedDate >= DateOnly.FromDateTime(request.StartDate) &&
                    order.SemiProductPlannedDate <= DateOnly.FromDateTime(request.EndDate))
                {
                    events.Add(new CalendarEventDto
                    {
                        Id = order.Id,
                        OrderNumber = order.OrderNumber,
                        Title = $"{order.SemiProduct?.ProductName?.Replace(" - meziprodukt", "") ?? order.OrderNumber}",
                        Date = order.SemiProductPlannedDate.ToDateTime(TimeOnly.MinValue),
                        State = order.State,
                        ResponsiblePerson = order.ResponsiblePerson,
                        SemiProduct = order.SemiProduct != null ? new CalendarEventSemiProductDto
                        {
                            ProductCode = order.SemiProduct.ProductCode,
                            ProductName = order.SemiProduct.ProductName,
                            PlannedQuantity = order.SemiProduct.PlannedQuantity,
                            ActualQuantity = order.SemiProduct.ActualQuantity,
                            BatchMultiplier = order.SemiProduct.BatchMultiplier
                        } : null,
                        Products = order.Products?.Select(p => new CalendarEventProductDto
                        {
                            ProductCode = p.ProductCode,
                            ProductName = p.ProductName,
                            PlannedQuantity = p.PlannedQuantity,
                            ActualQuantity = p.ActualQuantity
                        }).ToList() ?? new List<CalendarEventProductDto>()
                    });
                }
            }

            // Sort events by date
            events = events.OrderBy(e => e.Date).ToList();

            return new GetCalendarViewResponse
            {
                Events = events
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting calendar view for date range {StartDate} to {EndDate}",
                request.StartDate, request.EndDate);
            return new GetCalendarViewResponse(ErrorCodes.InternalServerError);
        }
    }
}