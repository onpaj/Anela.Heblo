using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;

public class UpdateManufactureOrderStatusHandler : IRequestHandler<UpdateManufactureOrderStatusRequest, UpdateManufactureOrderStatusResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateManufactureOrderStatusHandler> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly IManufactureInventoryWriteDownService _inventoryWriteDownService;
    private readonly IManufactureConditionsCaptureService _conditionsCaptureService;

    public UpdateManufactureOrderStatusHandler(
        IManufactureOrderRepository repository,
        TimeProvider timeProvider,
        ILogger<UpdateManufactureOrderStatusHandler> logger,
        ICurrentUserService currentUserService,
        IManufactureInventoryWriteDownService inventoryWriteDownService,
        IManufactureConditionsCaptureService conditionsCaptureService)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
        _currentUserService = currentUserService;
        _inventoryWriteDownService = inventoryWriteDownService;
        _conditionsCaptureService = conditionsCaptureService;
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
            if (!order.CanTransitionTo(request.NewState))
            {
                return new UpdateManufactureOrderStatusResponse(Application.Shared.ErrorCodes.InvalidOperation,
                    new Dictionary<string, string>
                    {
                        { "oldState", oldState.ToString() },
                        { "newState", request.NewState.ToString() }
                    });
            }

            var currentUserName = _currentUserService.GetCurrentUser().GetDisplayName();

            // Update state
            order.ChangeState(request.NewState, _timeProvider.GetUtcNow().DateTime, currentUserName);

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

            if (request.WeightWithinTolerance.HasValue)
                order.WeightWithinTolerance = request.WeightWithinTolerance.Value;

            if (request.WeightDifference.HasValue)
                order.WeightDifference = request.WeightDifference.Value;

            if (request.FlexiDocMaterialIssueForSemiProduct != null)
            {
                order.DocMaterialIssueForSemiProduct = request.FlexiDocMaterialIssueForSemiProduct;
                order.DocMaterialIssueForSemiProductDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.FlexiDocSemiProductReceipt != null)
            {
                order.DocSemiProductReceipt = request.FlexiDocSemiProductReceipt;
                order.DocSemiProductReceiptDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.FlexiDocSemiProductIssueForProduct != null)
            {
                order.DocSemiProductIssueForProduct = request.FlexiDocSemiProductIssueForProduct;
                order.DocSemiProductIssueForProductDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.FlexiDocMaterialIssueForProduct != null)
            {
                order.DocMaterialIssueForProduct = request.FlexiDocMaterialIssueForProduct;
                order.DocMaterialIssueForProductDate = _timeProvider.GetUtcNow().DateTime;
            }

            if (request.FlexiDocProductReceipt != null)
            {
                order.DocProductReceipt = request.FlexiDocProductReceipt;
                order.DocProductReceiptDate = _timeProvider.GetUtcNow().DateTime;
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

            if (request.NewState is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Completed
                && order.ConditionsReadings.All(r => r.Stage != request.NewState))
            {
                var reading = await _conditionsCaptureService.CaptureAsync(order, request.NewState, cancellationToken);
                order.ConditionsReadings.Add(reading);
            }

            if (request.NewState == ManufactureOrderState.Completed)
            {
                await _inventoryWriteDownService.WriteDownAsync(order, currentUserName, cancellationToken);
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
}
