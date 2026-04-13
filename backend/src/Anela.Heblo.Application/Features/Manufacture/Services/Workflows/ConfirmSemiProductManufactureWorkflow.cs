using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

public interface IConfirmSemiProductManufactureWorkflow
{
    Task<ConfirmSemiProductManufactureResult> ExecuteAsync(
        int orderId,
        decimal actualQuantity,
        string? changeReason,
        CancellationToken cancellationToken);
}

public class ConfirmSemiProductManufactureWorkflow : IConfirmSemiProductManufactureWorkflow
{
    private readonly IMediator _mediator;
    private readonly IManufactureNameBuilder _nameBuilder;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ConfirmSemiProductManufactureWorkflow> _logger;

    public ConfirmSemiProductManufactureWorkflow(
        IMediator mediator,
        IManufactureNameBuilder nameBuilder,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService,
        ILogger<ConfirmSemiProductManufactureWorkflow> logger)
    {
        _mediator = mediator;
        _nameBuilder = nameBuilder;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ConfirmSemiProductManufactureResult> ExecuteAsync(
        int orderId,
        decimal actualQuantity,
        string? changeReason,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting semi-product manufacture confirmation for order {OrderId} with quantity {ActualQuantity}",
                orderId, actualQuantity);

            // Step 1: Update the ActualQuantity on the semi-product
            var updateResult = await UpdateSemiProductQuantityAsync(orderId, actualQuantity, cancellationToken);
            if (!updateResult.Success)
            {
                _logger.LogError("Failed to update actual quantity for order {OrderId}: {ErrorCode}",
                    orderId, updateResult.ErrorCode);
                return new ConfirmSemiProductManufactureResult(false, string.Format(ManufactureMessages.QuantityUpdateErrorFormat, updateResult.ErrorCode));
            }

            // Step 2: Create manufacture via external client
            var submitManufactureResult = await SubmitToErpAsync(orderId, updateResult.Order!, cancellationToken);

            // Step 3: Change state to SemiProductManufactured
            var result = await UpdateStatusAsync(orderId, actualQuantity, changeReason, submitManufactureResult, cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("Failed to update status for order {OrderId}: {ErrorCode}",
                    orderId, result.ErrorCode);
                return new ConfirmSemiProductManufactureResult(false, string.Format(ManufactureMessages.StatusChangeErrorFormat, result.ErrorCode));
            }

            _logger.LogInformation("Successfully confirmed semi-product manufacture for order {OrderId}", orderId);
            return new ConfirmSemiProductManufactureResult(true,
                string.Format(ManufactureMessages.SemiProductManufacturedSuccessFormat, actualQuantity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming semi-product manufacture for order {OrderId}", orderId);
            return new ConfirmSemiProductManufactureResult(false, ManufactureMessages.UnexpectedSemiProductError);
        }
    }

    private async Task<UpdateManufactureOrderResponse> UpdateSemiProductQuantityAsync(
        int orderId,
        decimal actualQuantity,
        CancellationToken cancellationToken)
    {
        var updateRequest = new UpdateManufactureOrderRequest
        {
            Id = orderId,
            SemiProduct = new UpdateManufactureOrderSemiProductRequest
            {
                ActualQuantity = actualQuantity,
            }
        };

        return await _mediator.Send(updateRequest, cancellationToken);
    }

    private async Task<SubmitManufactureResponse> SubmitToErpAsync(
        int orderId,
        UpdateManufactureOrderDto order,
        CancellationToken cancellationToken)
    {
        var semiProduct = order.SemiProduct;
        var manufactureName = _nameBuilder.Build(order, ErpManufactureType.SemiProduct);
        var items = new List<SubmitManufactureRequestItem>
        {
            new()
            {
                ProductCode = semiProduct.ProductCode,
                Name = semiProduct.ProductName,
                Amount = semiProduct.ActualQuantity ?? semiProduct.PlannedQuantity,
            }
        };

        var submitManufactureRequest = new SubmitManufactureRequest
        {
            ManufactureOrderId = orderId,
            ManufactureOrderNumber = order.OrderNumber,
            ManufactureInternalNumber = manufactureName,
            ManufactureType = ErpManufactureType.SemiProduct,
            Date = _timeProvider.GetUtcNow().DateTime,
            CreatedBy = _currentUserService.GetCurrentUser().Name,
            Items = items,
            LotNumber = semiProduct.LotNumber,
            ExpirationDate = semiProduct.ExpirationDate,
            ResidueDistribution = null,
        };

        var submitManufactureResult = await _mediator.Send(submitManufactureRequest, cancellationToken);
        if (!submitManufactureResult.Success)
        {
            _logger.LogError("Failed to create manufacture for order {OrderId}: {ErrorCode}",
                orderId, submitManufactureResult.ErrorCode);
        }
        else
        {
            _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {OrderId}",
                submitManufactureResult.ManufactureId, orderId);
        }

        return submitManufactureResult;
    }

    private async Task<UpdateManufactureOrderStatusResponse> UpdateStatusAsync(
        int orderId,
        decimal actualQuantity,
        string? changeReason,
        SubmitManufactureResponse submitResult,
        CancellationToken cancellationToken)
    {
        var statusRequest = new UpdateManufactureOrderStatusRequest
        {
            Id = orderId,
            NewState = ManufactureOrderState.SemiProductManufactured,
            ChangeReason = changeReason ?? string.Format(ManufactureMessages.SemiProductDefaultChangeReasonFormat, actualQuantity),
            Note = submitResult.Success
                ? string.Format(ManufactureMessages.SemiProductErpNoteFormat, submitResult.ManufactureId)
                : submitResult.UserMessage ?? submitResult.FullError(),
            SemiProductOrderCode = submitResult.ManufactureId,
            ProductOrderCode = null,
            DiscardRedisueDocumentCode = null,
            ManualActionRequired = !submitResult.Success,
            WeightWithinTolerance = null,
            WeightDifference = null,
            // Restore Flexi sub-document codes so ManufactureOrder.FlexiDoc* columns
            // are populated. These were forwarded by the old service and must keep
            // being forwarded to preserve audit trails that link orders to Flexi docs.
            FlexiDocMaterialIssueForSemiProduct = submitResult.MaterialIssueForSemiProductDocCode,
            FlexiDocSemiProductReceipt = submitResult.SemiProductReceiptDocCode,
        };

        return await _mediator.Send(statusRequest, cancellationToken);
    }
}
