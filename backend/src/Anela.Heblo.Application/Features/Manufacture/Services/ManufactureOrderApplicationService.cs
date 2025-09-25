using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IManufactureOrderApplicationService
{
    Task<ConfirmSemiProductManufactureResult> ConfirmSemiProductManufactureAsync(
        int orderId, 
        decimal actualQuantity, 
        string? changeReason = null, 
        CancellationToken cancellationToken = default);

    Task<ConfirmProductCompletionResult> ConfirmProductCompletionAsync(
        int orderId,
        Dictionary<int, decimal> productActualQuantities,
        string? changeReason = null,
        CancellationToken cancellationToken = default);
}

public class ManufactureOrderApplicationService : IManufactureOrderApplicationService
{
    private readonly IMediator _mediator;
    private readonly ILogger<ManufactureOrderApplicationService> _logger;

    public ManufactureOrderApplicationService(
        IMediator mediator,
        ILogger<ManufactureOrderApplicationService> logger)
    {
        _mediator = mediator;
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

            // Step 2: Change state to SemiProductManufactured
            var statusRequest = new UpdateManufactureOrderStatusRequest
            {
                Id = orderId,
                NewState = ManufactureOrderState.SemiProductManufactured,
                ChangeReason = changeReason ?? $"Potvrzeno skutečné množství polotovaru: {actualQuantity}"
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

            // Step 2: Change state to Completed
            var statusRequest = new UpdateManufactureOrderStatusRequest
            {
                Id = orderId,
                NewState = ManufactureOrderState.Completed,
                ChangeReason = changeReason ?? $"Potvrzeno dokončení výroby produktů"
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

public class ConfirmSemiProductManufactureResult
{
    public bool Success { get; }
    public string Message { get; }

    public ConfirmSemiProductManufactureResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}

// DTOs for API Controller
public class ConfirmSemiProductManufactureRequest
{
    [Required]
    public int Id { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "ActualQuantity must be greater than 0")]
    public decimal ActualQuantity { get; set; }

    public string? ChangeReason { get; set; }
}

public class ConfirmSemiProductManufactureResponse : BaseResponse
{
    public string? Message { get; set; }

    public ConfirmSemiProductManufactureResponse() : base() { }

    public ConfirmSemiProductManufactureResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}

// Result class for product completion
public class ConfirmProductCompletionResult
{
    public bool Success { get; }
    public string Message { get; }

    public ConfirmProductCompletionResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}

// DTOs for product completion API Controller
public class ConfirmProductCompletionRequest
{
    [Required]
    public int Id { get; set; }

    [Required]
    public List<ProductActualQuantityRequest> Products { get; set; } = new();

    public string? ChangeReason { get; set; }
}

public class ProductActualQuantityRequest
{
    [Required]
    public int Id { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "ActualQuantity must be greater than 0")]
    public decimal ActualQuantity { get; set; }
}

public class ConfirmProductCompletionResponse : BaseResponse
{
    public string? Message { get; set; }

    public ConfirmProductCompletionResponse() : base() { }

    public ConfirmProductCompletionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}