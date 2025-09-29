using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ManufactureOrderApplicationService : IManufactureOrderApplicationService
{
    private readonly IMediator _mediator;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ManufactureOrderApplicationService> _logger;

    public ManufactureOrderApplicationService(
        IMediator mediator,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService,
        ILogger<ManufactureOrderApplicationService> logger)
    {
        _mediator = mediator;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
        _logger = logger;
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
            if (!updateResult.Success)
            {
                _logger.LogError("Failed to update actual quantity for order {OrderId}: {ErrorCode}",
                    orderId, updateResult.ErrorCode);
                return new ConfirmSemiProductManufactureResult(false, $"Chyba při aktualizaci množství: {updateResult.ErrorCode}");
            }

            // Step 2: Create manufacture via external client
            var semiProduct = updateResult.Order!.SemiProduct;
            var submitManufactureRequest = new SubmitManufactureRequest
            {
                ManufactureOrderNumber = updateResult.Order.OrderNumber,
                ManufactureType = ManufactureType.SemiProduct,
                Date = _timeProvider.GetUtcNow().DateTime,
                CreatedBy = _currentUserService.GetCurrentUser().Name,
                Items = new List<SubmitManufactureRequestItem>()
                {
                    new ()
                    {
                        ProductCode = semiProduct.ProductCode,
                        Name = semiProduct.ProductName,
                        Amount = semiProduct.ActualQuantity ?? semiProduct.PlannedQuantity,
                    }
                },
                LotNumber = semiProduct.LotNumber,
                ExpirationDate = semiProduct.ExpirationDate,
            };

            var submitManufactureResult = await _mediator.Send(submitManufactureRequest, cancellationToken);
            if (!submitManufactureResult.Success)
            {
                _logger.LogError("Failed to create manufacture for order {OrderId}: {ErrorCode}",
                    orderId, submitManufactureResult.ErrorCode);
                return new ConfirmSemiProductManufactureResult(false, $"Chyba při vytvoření výroby: {submitManufactureResult.ErrorCode}");
            }

            _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {OrderId}",
                submitManufactureResult.ManufactureId, orderId);

            // Step 3: Change state to SemiProductManufactured
            var statusRequest = new UpdateManufactureOrderStatusRequest
            {
                Id = orderId,
                NewState = ManufactureOrderState.SemiProductManufactured,
                ChangeReason = changeReason ?? $"Potvrzeno skutečné množství polotovaru: {actualQuantity}",
                Note = submitManufactureResult.Success ? $"Vytvořena vydaná objednávka meziproduktu {submitManufactureResult.ManufactureId}" : $"Nepodařilo se vytvořit vydanou objednávku meziproduktu: {submitManufactureResult.FullError()}",
                SemiProductOrderCode = submitManufactureResult.ManufactureId,
                ManualActionRequired = !submitManufactureResult.Success
            };

            var statusResult = await _mediator.Send(statusRequest, cancellationToken);
            if (!statusResult.Success)
            {
                _logger.LogError("Failed to update status for order {OrderId}: {ErrorCode}",
                    orderId, statusResult.ErrorCode);
                return new ConfirmSemiProductManufactureResult(false, $"Chyba při změně stavu: {statusResult.ErrorCode}");
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
        string? changeReason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting product completion confirmation for order {OrderId} with {ProductCount} products",
                orderId, productActualQuantities.Count);

            // Step 1: Update the ActualQuantity on each product
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
            if (!updateResult.Success)
            {
                _logger.LogError("Failed to update actual quantities for order {OrderId}: {ErrorCode}",
                    orderId, updateResult.ErrorCode);
                return new ConfirmProductCompletionResult(false, $"Chyba při aktualizaci množství produktů: {updateResult.ErrorCode}");
            }

            // Step 2: Create manufacture via external client
            var submitManufactureRequest = new SubmitManufactureRequest
            {
                ManufactureOrderNumber = updateResult.Order!.OrderNumber,
                ManufactureType = ManufactureType.Product,
                Date = _timeProvider.GetUtcNow().DateTime,
                CreatedBy = _currentUserService.GetCurrentUser().Name,
                Items = updateResult.Order!.Products.Select(p => new SubmitManufactureRequestItem()
                {
                    ProductCode = p.ProductCode,
                    Name = p.ProductName,
                    Amount = p.ActualQuantity ?? p.PlannedQuantity,
                }).ToList(),
                LotNumber = updateResult.Order.SemiProduct.LotNumber,
                ExpirationDate = updateResult.Order.SemiProduct.ExpirationDate,
            };

            var submitManufactureResult = await _mediator.Send(submitManufactureRequest, cancellationToken);
            if (!submitManufactureResult.Success)
            {
                _logger.LogError("Failed to create manufacture for order {OrderId}: {ErrorCode}",
                    orderId, submitManufactureResult.ErrorCode);
                //return new ConfirmProductCompletionResult(false, $"Chyba při vytvoření výroby: {submitManufactureResult.ErrorCode}");
            }
            else
            {
                _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {OrderId}",
                    submitManufactureResult.ManufactureId, orderId);
            }

            // Step 3: Change state to Completed
            var statusRequest = new UpdateManufactureOrderStatusRequest
            {
                Id = orderId,
                NewState = ManufactureOrderState.Completed,
                ChangeReason = changeReason ?? $"Potvrzeno dokončení výroby produktů",
                Note = submitManufactureResult.Success ? $"Vytvořena vydaná objednávka produktů {submitManufactureResult.ManufactureId}" : $"Nepodařilo se vytvořit vydanou objednávku produktů {submitManufactureResult.FullError()}",
                ProductOrderCode = submitManufactureResult.ManufactureId,
                ManualActionRequired = !submitManufactureResult.Success
            };

            var statusResult = await _mediator.Send(statusRequest, cancellationToken);
            if (!statusResult.Success)
            {
                _logger.LogError("Failed to update status for order {OrderId}: {ErrorCode}",
                    orderId, statusResult.ErrorCode);
                return new ConfirmProductCompletionResult(false, $"Chyba při změně stavu: {statusResult.ErrorCode}");
            }

            _logger.LogInformation("Successfully confirmed product completion for order {OrderId}", orderId);
            return new ConfirmProductCompletionResult(true,
                $"Výroba produktů byla úspěšně dokončena");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming product completion for order {OrderId}", orderId);
            return new ConfirmProductCompletionResult(false, "Došlo k neočekávané chybě při dokončení výroby produktů");
        }
    }
}