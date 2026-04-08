using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ManufactureOrderApplicationService : IManufactureOrderApplicationService
{
    private readonly IMediator _mediator;
    private readonly IManufactureClient _manufactureClient;
    private readonly IResidueDistributionCalculator _residueCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ManufactureOrderApplicationService> _logger;
    private readonly IProductNameFormatter _productNameFormatter;

    public ManufactureOrderApplicationService(
        IMediator mediator,
        IManufactureClient manufactureClient,
        IResidueDistributionCalculator residueCalculator,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService,
        ILogger<ManufactureOrderApplicationService> logger,
        IProductNameFormatter productNameFormatter)
    {
        _mediator = mediator;
        _manufactureClient = manufactureClient;
        _residueCalculator = residueCalculator;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
        _logger = logger;
        _productNameFormatter = productNameFormatter;
    }

    public async Task<ConfirmSemiProductManufactureResult> ConfirmSemiProductManufactureAsync(
        int orderId,
        decimal actualQuantity,
        string? changeReason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting semi-product manufacture confirmation for order {OrderId} with quantity {ActualQuantity}",
                orderId, actualQuantity);

            // Step 1: Update the ActualQuantity on the semi-product
            var updateResult = await UpdateSemiProductQuantity(orderId, actualQuantity, cancellationToken);
            if (!updateResult.Success)
            {
                _logger.LogError("Failed to update actual quantity for order {OrderId}: {ErrorCode}",
                    orderId, updateResult.ErrorCode);
                return new ConfirmSemiProductManufactureResult(false, $"Chyba při aktualizaci množství: {updateResult.ErrorCode}");
            }

            // Step 2: Create manufacture via external client
            var submitManufactureResult = await CreateManufactureOrderInErp(orderId, updateResult.Order!, ErpManufactureType.SemiProduct, null, cancellationToken);

            // Step 3: Change state to SemiProductManufactured
            var result = await UpdateOrderStatus(
                orderId,
                ManufactureOrderState.SemiProductManufactured,
                changeReason ?? $"Potvrzeno skutečné množství polotovaru: {actualQuantity}",
                submitManufactureResult.Success ? $"Vytvořena vydaná objednávka meziproduktu {submitManufactureResult.ManufactureId}" : submitManufactureResult.UserMessage ?? submitManufactureResult.FullError(),
                submitManufactureResult.ManufactureId,
                productDocumentCode: null,
                discardDocumentCode: null,
                !submitManufactureResult.Success,
                weightWithinTolerance: null,
                weightDifference: null,
                cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("Failed to update status for order {OrderId}: {ErrorCode}",
                    orderId, result.ErrorCode);
                return new ConfirmSemiProductManufactureResult(false, $"Chyba při změně stavu: {result.ErrorCode}");
            }

            _logger.LogInformation("Successfully confirmed semi-product manufacture for order {OrderId}", orderId);
            return new ConfirmSemiProductManufactureResult(true,
                $"Polotovar byl úspěšně vyroben se skutečným množstvím {actualQuantity}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming semi-product manufacture for order {OrderId}", orderId);
            return new ConfirmSemiProductManufactureResult(false, "Došlo k neočekávané chybě při potvrzení výroby polotovaru");
        }
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
                return new ConfirmProductCompletionResult($"Chyba při aktualizaci množství produktů: {updateResult.ErrorCode}");
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
                    try
                    {
                        await _manufactureClient.UpdateBoMIngredientAmountAsync(
                            product.ProductCode,
                            updateResult.Order!.SemiProduct.ProductCode,
                            (double)product.AdjustedGramsPerUnit,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update BoM ingredient amount for product {ProductCode} in order {OrderId}",
                            product.ProductCode, orderId);
                    }
                }
            }

            // Step 6: Change state to Completed
            string? orderNote = null;
            if (!submitManufactureResult.Success)
                orderNote = submitManufactureResult.UserMessage ?? submitManufactureResult.FullError();

            var result = await UpdateOrderStatus(
                orderId,
                ManufactureOrderState.Completed,
                changeReason ?? $"Potvrzeno dokončení výroby produktů",
                orderNote ?? $"Potvrzeno dokončení výroby produktů - {submitManufactureResult.ManufactureId}",
                semiproductDocumentCode: null,
                productDocumentCode: submitManufactureResult.ManufactureId,
                discardDocumentCode: null,
                manualActionRequired: !submitManufactureResult.Success,
                weightWithinTolerance: distribution.IsWithinAllowedThreshold,
                weightDifference: distribution.Difference,
                cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("Failed to update status for order {OrderId}: {ErrorCode}",
                    orderId, result.ErrorCode);
                return new ConfirmProductCompletionResult($"Chyba při změně stavu: {result.ErrorCode}");
            }

            _logger.LogInformation("Successfully confirmed product completion for order {OrderId}", orderId);
            return new ConfirmProductCompletionResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming product completion for order {OrderId}", orderId);
            return new ConfirmProductCompletionResult($"Došlo k neočekávané chybě při dokončení výroby produktů: {ex.Message}");
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
        int orderId,
        ManufactureOrderState targetState,
        string changeReason,
        string note,
        string? semiproductDocumentCode,
        string? productDocumentCode,
        string? discardDocumentCode,
        bool manualActionRequired,
        bool? weightWithinTolerance,
        decimal? weightDifference,
        CancellationToken cancellationToken)
    {
        var statusRequest = new UpdateManufactureOrderStatusRequest
        {
            Id = orderId,
            NewState = targetState,
            ChangeReason = changeReason,
            Note = note,
            SemiProductOrderCode = semiproductDocumentCode,
            ProductOrderCode = productDocumentCode,
            DiscardRedisueDocumentCode = discardDocumentCode,
            ManualActionRequired = manualActionRequired,
            WeightWithinTolerance = weightWithinTolerance,
            WeightDifference = weightDifference,
        };

        var statusResult = await _mediator.Send(statusRequest, cancellationToken);
        return statusResult;
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
            manufactureName = CreateManufactureName(order, type);
            items = order!.Products.Select(p => new SubmitManufactureRequestItem()
            {
                ProductCode = p.ProductCode,
                Name = p.ProductName,
                Amount = p.ActualQuantity ?? p.PlannedQuantity,
            }).ToList();
        }
        else
        {
            manufactureName = CreateManufactureName(order, type);
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

        _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {OrderId}",
            submitManufactureResult.ManufactureId, orderId);
        return submitManufactureResult;
    }

    private string CreateManufactureName(UpdateManufactureOrderDto order, ErpManufactureType type)
    {
        string manufactureName;
        if (type == ErpManufactureType.Product)
        {
            if (order.Products.All(p => p.ProductCode == order.SemiProduct.ProductCode)) // Singlephase manufacture
            {
                manufactureName = $"{order.SemiProduct.ProductCode}";
            }
            else
            {
                manufactureName = $"{order.SemiProduct.ProductCode.Substring(0, 6)} {_productNameFormatter.ShortProductName(order.SemiProduct.ProductName)}";
            }
        }
        else
            manufactureName = $"{order.SemiProduct.ProductCode.Substring(0, 6)}M {_productNameFormatter.ShortProductName(order.SemiProduct.ProductName)}";

        return manufactureName.Length > 40 ? manufactureName.Substring(0, 40) : manufactureName;
    }


    private async Task<UpdateManufactureOrderResponse> UpdateSemiProductQuantity(int orderId, decimal actualQuantity, CancellationToken cancellationToken)
    {
        // Step 1: Update the ActualQuantity on the semi-product
        var updateRequest = new UpdateManufactureOrderRequest
        {
            Id = orderId,
            SemiProduct = new UpdateManufactureOrderSemiProductRequest
            {
                // Only update ActualQuantity, other fields will be preserved
                ActualQuantity = actualQuantity,
            }
        };

        var updateResult = await _mediator.Send(updateRequest, cancellationToken);
        return updateResult;
    }
}
