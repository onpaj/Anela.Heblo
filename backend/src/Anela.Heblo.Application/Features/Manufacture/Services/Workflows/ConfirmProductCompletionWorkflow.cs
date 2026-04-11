using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

public interface IConfirmProductCompletionWorkflow
{
    Task<ConfirmProductCompletionResult> ExecuteAsync(
        int orderId,
        Dictionary<int, decimal> productActualQuantities,
        bool overrideConfirmed,
        string? changeReason,
        CancellationToken cancellationToken);
}

public class ConfirmProductCompletionWorkflow : IConfirmProductCompletionWorkflow
{
    private readonly IMediator _mediator;
    private readonly IResidueDistributionCalculator _residueCalculator;
    private readonly IManufactureNameBuilder _nameBuilder;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ConfirmProductCompletionWorkflow> _logger;

    public ConfirmProductCompletionWorkflow(
        IMediator mediator,
        IResidueDistributionCalculator residueCalculator,
        IManufactureNameBuilder nameBuilder,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService,
        ILogger<ConfirmProductCompletionWorkflow> logger)
    {
        _mediator = mediator;
        _residueCalculator = residueCalculator;
        _nameBuilder = nameBuilder;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ConfirmProductCompletionResult> ExecuteAsync(
        int orderId,
        Dictionary<int, decimal> productActualQuantities,
        bool overrideConfirmed,
        string? changeReason,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting product completion confirmation for order {OrderId} with {ProductCount} products",
                orderId, productActualQuantities.Count);

            // Step 1: Update the ActualQuantity on each product
            var updateResult = await UpdateProductsQuantityAsync(orderId, productActualQuantities, cancellationToken);
            if (!updateResult.Success)
            {
                _logger.LogError("Failed to update actual quantities for order {OrderId}: {ErrorCode}",
                    orderId, updateResult.ErrorCode);
                return new ConfirmProductCompletionResult(
                    string.Format(ManufactureMessages.ProductQuantityUpdateErrorFormat, updateResult.ErrorCode));
            }

            // Step 2: Calculate residue distribution
            var distribution = await _residueCalculator.CalculateAsync(updateResult.Order!, cancellationToken);

            // Step 3: If outside threshold and not yet confirmed by user, request confirmation
            if (!distribution.IsWithinAllowedThreshold && !overrideConfirmed)
            {
                _logger.LogInformation(
                    "Order {OrderId} requires user confirmation: residue {DiffPct:F2}% exceeds allowed {AllowedPct:F2}%",
                    orderId, distribution.DifferencePercentage, distribution.AllowedResiduePercentage);
                return ConfirmProductCompletionResult.NeedsConfirmation(distribution);
            }

            // Step 4: Submit to ERP with distribution data
            var submitResult = await SubmitToErpAsync(orderId, updateResult.Order!, distribution, cancellationToken);

            // Step 5: Update BoM ingredient amounts per product if ERP submission succeeded
            var bomFailures = new List<string>();
            if (submitResult.Success)
            {
                bomFailures = await UpdateBoMIngredientsAsync(submitResult, updateResult.Order!, distribution, orderId, cancellationToken);
            }

            // Step 6: Transition to Completed state
            var result = await TransitionToCompletedAsync(
                orderId, submitResult, distribution, overrideConfirmed, changeReason, bomFailures, cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("Failed to update status for order {OrderId}: {ErrorCode}",
                    orderId, result.ErrorCode);
                return new ConfirmProductCompletionResult(
                    string.Format(ManufactureMessages.StatusChangeErrorFormat, result.ErrorCode));
            }

            _logger.LogInformation("Successfully confirmed product completion for order {OrderId}", orderId);
            return new ConfirmProductCompletionResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming product completion for order {OrderId}", orderId);
            return new ConfirmProductCompletionResult(
                string.Format(ManufactureMessages.UnexpectedProductCompletionErrorFormat, ex.Message));
        }
    }

    private async Task<UpdateManufactureOrderResponse> UpdateProductsQuantityAsync(
        int orderId,
        Dictionary<int, decimal> productActualQuantities,
        CancellationToken cancellationToken)
    {
        var productRequests = productActualQuantities
            .Select(kvp => new UpdateManufactureOrderProductRequest
            {
                Id = kvp.Key,
                ActualQuantity = kvp.Value,
            })
            .ToList();

        var updateRequest = new UpdateManufactureOrderRequest
        {
            Id = orderId,
            Products = productRequests,
        };

        return await _mediator.Send(updateRequest, cancellationToken);
    }

    private async Task<SubmitManufactureResponse> SubmitToErpAsync(
        int orderId,
        UpdateManufactureOrderDto order,
        ResidueDistribution distribution,
        CancellationToken cancellationToken)
    {
        var semiProduct = order.SemiProduct;
        var manufactureName = _nameBuilder.Build(order, ErpManufactureType.Product);
        var items = order.Products
            .Select(p => new SubmitManufactureRequestItem
            {
                ProductCode = p.ProductCode,
                Name = p.ProductName,
                Amount = p.ActualQuantity ?? p.PlannedQuantity,
            })
            .ToList();

        var submitRequest = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = order.OrderNumber,
            ManufactureInternalNumber = manufactureName,
            ManufactureType = ErpManufactureType.Product,
            Date = _timeProvider.GetUtcNow().DateTime,
            CreatedBy = _currentUserService.GetCurrentUser().Name,
            Items = items,
            LotNumber = semiProduct.LotNumber,
            ExpirationDate = semiProduct.ExpirationDate,
            ResidueDistribution = distribution,
        };

        var submitResult = await _mediator.Send(submitRequest, cancellationToken);
        if (!submitResult.Success)
        {
            _logger.LogError("Failed to create manufacture for order {OrderId}: {ErrorCode}",
                orderId, submitResult.ErrorCode);
        }
        else
        {
            _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {OrderId}",
                submitResult.ManufactureId, orderId);
        }

        return submitResult;
    }

    private async Task<List<string>> UpdateBoMIngredientsAsync(
        SubmitManufactureResponse submitResult,
        UpdateManufactureOrderDto order,
        ResidueDistribution distribution,
        int orderId,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        foreach (var product in distribution.Products)
        {
            var response = await _mediator.Send(
                new UpdateBoMIngredientAmountRequest
                {
                    ProductCode = product.ProductCode,
                    IngredientCode = order.SemiProduct.ProductCode,
                    NewAmount = (double)product.AdjustedGramsPerUnit,
                },
                cancellationToken);

            if (!response.Success)
            {
                var errorDetail = response.UserMessage ?? response.FullError();
                failures.Add($"{product.ProductCode}: {errorDetail}");
                _logger.LogWarning(
                    "Failed to update BoM ingredient amount for product {ProductCode} in order {OrderId}: {UserMessage}",
                    product.ProductCode, orderId, errorDetail);
            }
        }

        return failures;
    }

    private async Task<UpdateManufactureOrderStatusResponse> TransitionToCompletedAsync(
        int orderId,
        SubmitManufactureResponse submitResult,
        ResidueDistribution distribution,
        bool overrideConfirmed,
        string? changeReason,
        List<string> bomFailures,
        CancellationToken cancellationToken)
    {
        string? erpFailureNote = !submitResult.Success
            ? submitResult.UserMessage ?? submitResult.FullError()
            : null;

        string? weightToleranceNote = overrideConfirmed && !distribution.IsWithinAllowedThreshold
            ? string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                ManufactureMessages.WeightToleranceOverrideFormat,
                distribution.DifferencePercentage,
                distribution.AllowedResiduePercentage)
            : null;

        string? bomFailureNote = bomFailures.Count > 0
            ? "BoM update failures: " + string.Join("; ", bomFailures)
            : null;

        var combined = string.Join(
            "\n",
            new[] { erpFailureNote, weightToleranceNote, bomFailureNote }.Where(n => n != null));

        if (string.IsNullOrEmpty(combined))
        {
            combined = string.Format(
                ManufactureMessages.ProductCompletionDefaultNoteFormat,
                submitResult.ManufactureId);
        }

        var manualActionRequired = !submitResult.Success || bomFailures.Count > 0;

        var statusRequest = new UpdateManufactureOrderStatusRequest
        {
            Id = orderId,
            NewState = ManufactureOrderState.Completed,
            ChangeReason = changeReason ?? ManufactureMessages.ProductCompletionDefaultChangeReason,
            Note = combined,
            SemiProductOrderCode = null,
            ProductOrderCode = submitResult.ManufactureId,
            DiscardRedisueDocumentCode = null,
            ManualActionRequired = manualActionRequired,
            WeightWithinTolerance = distribution.IsWithinAllowedThreshold,
            WeightDifference = distribution.Difference,
            // Restore Flexi sub-document codes so ManufactureOrder.FlexiDoc* columns
            // are populated. These were forwarded by the old service and must keep
            // being forwarded to preserve audit trails that link orders to Flexi docs.
            FlexiDocSemiProductIssueForProduct = submitResult.SemiProductIssueForProductDocCode,
            FlexiDocMaterialIssueForProduct = submitResult.MaterialIssueForProductDocCode,
            FlexiDocProductReceipt = submitResult.ProductReceiptDocCode,
        };

        return await _mediator.Send(statusRequest, cancellationToken);
    }
}
