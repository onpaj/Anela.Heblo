using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;

public class UpdateManufactureOrderStatusHandler : IRequestHandler<UpdateManufactureOrderStatusRequest, UpdateManufactureOrderStatusResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly ILogger<UpdateManufactureOrderStatusHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UpdateManufactureOrderStatusHandler(
        IManufactureOrderRepository repository,
        ILogger<UpdateManufactureOrderStatusHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _repository = repository;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<UpdateManufactureOrderStatusResponse> Handle(UpdateManufactureOrderStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _repository.GetOrderByIdAsync(request.Id, cancellationToken);
            
            if (order == null)
            {
                return new UpdateManufactureOrderStatusResponse(Application.Shared.ErrorCodes.ResourceNotFound,
                    new Dictionary<string, string> { { "id", request.Id.ToString() } });
            }

            var oldState = order.State;
            
            // Validate state transition (basic validation - can be extended)
            if (!IsValidStateTransition(oldState, request.NewState))
            {
                return new UpdateManufactureOrderStatusResponse(Application.Shared.ErrorCodes.InvalidOperation,
                    new Dictionary<string, string> 
                    { 
                        { "oldState", oldState.ToString() },
                        { "newState", request.NewState.ToString() }
                    });
            }

            // Update state
            order.State = request.NewState;
            order.StateChangedAt = DateTime.UtcNow;
            order.StateChangedByUser = GetCurrentUserName();

            // Add audit log entry if change reason provided
            if (!string.IsNullOrEmpty(request.ChangeReason))
            {
                order.AuditLog.Add(new ManufactureOrderAuditLog
                {
                    Action = ManufactureOrderAuditAction.StateChanged,
                    Details = request.ChangeReason,
                    OldValue = oldState.ToString(),
                    NewValue = request.NewState.ToString(),
                    Timestamp = DateTime.UtcNow,
                    User = order.StateChangedByUser,
                    ManufactureOrderId = order.Id
                });
            }

            await _repository.UpdateOrderAsync(order, cancellationToken);

            return new UpdateManufactureOrderStatusResponse
            {
                OldState = oldState.ToString(),
                NewState = request.NewState.ToString(),
                StateChangedAt = order.StateChangedAt,
                StateChangedByUser = order.StateChangedByUser
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating manufacture order status for order {OrderId}", request.Id);
            return new UpdateManufactureOrderStatusResponse(ErrorCodes.InternalServerError);
        }
    }

    private bool IsValidStateTransition(ManufactureOrderState fromState, ManufactureOrderState toState)
    {
        // Allow backward and forward state transitions - extended business rules
        return fromState switch
        {
            ManufactureOrderState.Draft => toState is ManufactureOrderState.SemiProductPlanned or ManufactureOrderState.Cancelled,
            ManufactureOrderState.SemiProductPlanned => toState is ManufactureOrderState.Draft or ManufactureOrderState.SemiProductManufacture or ManufactureOrderState.Cancelled,
            ManufactureOrderState.SemiProductManufacture => toState is ManufactureOrderState.SemiProductPlanned or ManufactureOrderState.ProductsPlanned or ManufactureOrderState.Cancelled,
            ManufactureOrderState.ProductsPlanned => toState is ManufactureOrderState.SemiProductManufacture or ManufactureOrderState.ProductsManufacture or ManufactureOrderState.Cancelled,
            ManufactureOrderState.ProductsManufacture => toState is ManufactureOrderState.ProductsPlanned or ManufactureOrderState.Completed or ManufactureOrderState.Cancelled,
            ManufactureOrderState.Completed => toState is ManufactureOrderState.ProductsManufacture or ManufactureOrderState.Cancelled, // Allow going back from completed
            ManufactureOrderState.Cancelled => false, // Cannot change from cancelled state
            _ => false
        };
    }

    private string GetCurrentUserName()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.Identity?.Name ?? "System";
    }
}