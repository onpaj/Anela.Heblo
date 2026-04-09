using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ManufactureOrderApplicationService : IManufactureOrderApplicationService
{
    private readonly IMediator _mediator;
    private readonly IResidueDistributionCalculator _residueCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ManufactureOrderApplicationService> _logger;
    private readonly IManufactureNameBuilder _nameBuilder;
    private readonly IConfirmSemiProductManufactureWorkflow _semiProductWorkflow;

    public ManufactureOrderApplicationService(
        IMediator mediator,
        IResidueDistributionCalculator residueCalculator,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService,
        ILogger<ManufactureOrderApplicationService> logger,
        IManufactureNameBuilder nameBuilder,
        IConfirmSemiProductManufactureWorkflow semiProductWorkflow)
    {
        _mediator = mediator;
        _residueCalculator = residueCalculator;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
        _logger = logger;
        _nameBuilder = nameBuilder ?? throw new ArgumentNullException(nameof(nameBuilder));
        _semiProductWorkflow = semiProductWorkflow;
    }

    public Task<ConfirmSemiProductManufactureResult> ConfirmSemiProductManufactureAsync(
        int orderId,
        decimal actualQuantity,
        string? changeReason = null,
        CancellationToken cancellationToken = default)
    {
        return _semiProductWorkflow.ExecuteAsync(orderId, actualQuantity, changeReason, cancellationToken);
    }




    public async Task<ConfirmProductCompletionResult> ConfirmProductCompletionAsync(
        int orderId,
        Dictionary<int, decimal> productActualQuantities,
        bool overrideConfirmed = false,
        string? changeReason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting product completion confirmation for order {OrderId} with {ProductCount} products",
                orderId, productActualQuantities.Count);

            // Step 1: Update the ActualQuantity on each product
            var updateResult = await UpdateProductsQuantity(orderId, productActualQuantities, cancellationToken);
            if (!updateResult.Success)
            {
                _logger.LogError("Failed to update actual quantities for order {OrderId}: {ErrorCode}",
                    orderId, updateResult.ErrorCode);
                return new ConfirmProductCompletionResult(string.Format(ManufactureMessages.ProductQuantityUpdateErrorFormat, updateResult.ErrorCode));
            }

            // Step 2: Calculate residue distribution
            var distribution = await _residueCalculator.CalculateAsync(updateResult.Order!, cancellationToken);

            // Step 3: If outside threshold and not yet confirmed by user, request confirmation
            if (!distribution.IsWithinAllowedThreshold && !overrideConfirmed)
            {
                _logger.LogInformation("Order {OrderId} requires user confirmation: residue {DiffPct:F2}% exceeds allowed {AllowedPct:F2}%",
                    orderId, distribution.DifferencePercentage, distribution.AllowedResiduePercentage);
                return ConfirmProductCompletionResult.NeedsConfirmation(distribution);
            }

            // Step 4: Submit to ERP with distribution data
            var submitManufactureResult = await CreateManufactureOrderInErp(orderId, updateResult.Order!, ErpManufactureType.Product, distribution, cancellationToken);

            // Step 5: Update BoM ingredient amounts per product if ERP submission succeeded
            if (submitManufactureResult.Success)
            {
                foreach (var product in distribution.Products)
                {
                    var bomResponse = await _mediator.Send(
                        new UpdateBoMIngredientAmountRequest
                        {
                            ProductCode = product.ProductCode,
                            IngredientCode = updateResult.Order!.SemiProduct.ProductCode,
                            NewAmount = (double)product.AdjustedGramsPerUnit
                        },
                        cancellationToken);

                    if (!bomResponse.Success)
                    {
                        _logger.LogWarning(
                            "Failed to update BoM ingredient amount for product {ProductCode} in order {OrderId}: {UserMessage}",
                            product.ProductCode, orderId, bomResponse.UserMessage);
                    }
                }
            }

            // Step 6: Change state to Completed
            string? orderNote = null;
            if (!submitManufactureResult.Success)
                orderNote = submitManufactureResult.UserMessage ?? submitManufactureResult.FullError();

            string? weightToleranceNote = null;
            if (overrideConfirmed && !distribution.IsWithinAllowedThreshold)
                weightToleranceNote = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    ManufactureMessages.WeightToleranceOverrideFormat,
                    distribution.DifferencePercentage,
                    distribution.AllowedResiduePercentage);

            var combinedNote = string.Join("\n", new[] { orderNote, weightToleranceNote }.Where(n => n != null));
            if (string.IsNullOrEmpty(combinedNote))
                combinedNote = string.Format(ManufactureMessages.ProductCompletionDefaultNoteFormat, submitManufactureResult.ManufactureId);

            var result = await UpdateOrderStatus(
                new UpdateOrderStatusCommand(
                    OrderId: orderId,
                    TargetState: ManufactureOrderState.Completed,
                    ChangeReason: changeReason ?? ManufactureMessages.ProductCompletionDefaultChangeReason,
                    Note: combinedNote,
                    Documents: new ManufactureDocumentCodes(
                        SemiProduct: null,
                        Product: submitManufactureResult.ManufactureId,
                        Discard: null),
                    ManualActionRequired: !submitManufactureResult.Success,
                    WeightTolerance: new WeightToleranceInfo(
                        WithinTolerance: distribution.IsWithinAllowedThreshold,
                        Difference: distribution.Difference)),
                cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("Failed to update status for order {OrderId}: {ErrorCode}",
                    orderId, result.ErrorCode);
                return new ConfirmProductCompletionResult(string.Format(ManufactureMessages.StatusChangeErrorFormat, result.ErrorCode));
            }

            _logger.LogInformation("Successfully confirmed product completion for order {OrderId}", orderId);
            return new ConfirmProductCompletionResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming product completion for order {OrderId}", orderId);
            return new ConfirmProductCompletionResult(string.Format(ManufactureMessages.UnexpectedProductCompletionErrorFormat, ex.Message));
        }
    }



    private async Task<UpdateManufactureOrderResponse> UpdateProductsQuantity(int orderId, Dictionary<int, decimal> productActualQuantities,
        CancellationToken cancellationToken)
    {
        var productRequests = productActualQuantities.Select(kvp => new UpdateManufactureOrderProductRequest
        {
            Id = kvp.Key,
            ActualQuantity = kvp.Value,
        }).ToList();

        var updateRequest = new UpdateManufactureOrderRequest
        {
            Id = orderId,
            Products = productRequests
        };

        var updateResult = await _mediator.Send(updateRequest, cancellationToken);
        return updateResult;
    }

    private async Task<UpdateManufactureOrderStatusResponse> UpdateOrderStatus(
        UpdateOrderStatusCommand command,
        CancellationToken cancellationToken)
    {
        var statusRequest = new UpdateManufactureOrderStatusRequest
        {
            Id = command.OrderId,
            NewState = command.TargetState,
            ChangeReason = command.ChangeReason,
            Note = command.Note,
            SemiProductOrderCode = command.Documents.SemiProduct,
            ProductOrderCode = command.Documents.Product,
            DiscardRedisueDocumentCode = command.Documents.Discard,
            ManualActionRequired = command.ManualActionRequired,
            WeightWithinTolerance = command.WeightTolerance?.WithinTolerance,
            WeightDifference = command.WeightTolerance?.Difference,
        };

        return await _mediator.Send(statusRequest, cancellationToken);
    }

    private async Task<SubmitManufactureResponse> CreateManufactureOrderInErp(
        int orderId,
        UpdateManufactureOrderDto order,
        ErpManufactureType type,
        ResidueDistribution? distribution,
        CancellationToken cancellationToken)
    {
        string manufactureName;
        List<SubmitManufactureRequestItem> items;
        var semiProduct = order!.SemiProduct;

        if (type == ErpManufactureType.Product)
        {
            manufactureName = _nameBuilder.Build(order, type);
            items = order!.Products.Select(p => new SubmitManufactureRequestItem()
            {
                ProductCode = p.ProductCode,
                Name = p.ProductName,
                Amount = p.ActualQuantity ?? p.PlannedQuantity,
            }).ToList();
        }
        else
        {
            manufactureName = _nameBuilder.Build(order, type);
            items = new List<SubmitManufactureRequestItem>()
            {
                new()
                {
                    ProductCode = semiProduct.ProductCode,
                    Name = semiProduct.ProductName,
                    Amount = semiProduct.ActualQuantity ?? semiProduct.PlannedQuantity,
                }
            };
        }

        var submitManufactureRequest = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = order.OrderNumber,
            ManufactureInternalNumber = manufactureName,
            ManufactureType = type,
            Date = _timeProvider.GetUtcNow().DateTime,
            CreatedBy = _currentUserService.GetCurrentUser().Name,
            Items = items,
            LotNumber = semiProduct.LotNumber,
            ExpirationDate = semiProduct.ExpirationDate,
            ResidueDistribution = distribution,
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

}

