using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;

public class UpdateManufactureOrderStatusHandler : IRequestHandler<UpdateManufactureOrderStatusRequest, UpdateManufactureOrderStatusResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateManufactureOrderStatusHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UpdateManufactureOrderStatusHandler(
        IManufactureOrderRepository repository,
        TimeProvider timeProvider,
        ILogger<UpdateManufactureOrderStatusHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _repository = repository;
        _timeProvider = timeProvider;
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
            order.StateChangedAt = _timeProvider.GetUtcNow().DateTime;
            order.StateChangedByUser = GetCurrentUserName();

            if (request.ManualActionRequired.HasValue)
                order.ManualActionRequired = request.ManualActionRequired.Value;
            if (request.SemiProductOrderCode != null)
            {
                order.ErpOrderNumberSemiproduct = request.SemiProductOrderCode;
                order.ErpOrderNumberSemiproductDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.ProductOrderCode != null)
            {
                order.ErpOrderNumberProduct = request.ProductOrderCode;
                order.ErpOrderNumberProductDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.ProductOrderCode != null)
            {
                order.ErpDiscardResidueDocumentNumber = request.DiscardRedisueDocumentCode;
                order.ErpDiscardResidueDocumentNumberDate = _timeProvider.GetUtcNow().DateTime;
            }


            if (request.Note != null)
            {
                order.Notes.Add(new ManufactureOrderNote()
                {
                    Text = request.Note,
                    CreatedAt = order.StateChangedAt,
                    CreatedByUser = order.StateChangedByUser
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
        // Allow backward and forward state transitions - simplified business rules
        return fromState switch
        {
            ManufactureOrderState.Draft => toState is ManufactureOrderState.Planned or ManufactureOrderState.Cancelled,
            ManufactureOrderState.Planned => toState is ManufactureOrderState.Draft or ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Cancelled or ManufactureOrderState.Completed,
            ManufactureOrderState.SemiProductManufactured => toState is ManufactureOrderState.Planned or ManufactureOrderState.Completed or ManufactureOrderState.Cancelled,
            ManufactureOrderState.Completed => toState is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Cancelled, // Allow going back from completed
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