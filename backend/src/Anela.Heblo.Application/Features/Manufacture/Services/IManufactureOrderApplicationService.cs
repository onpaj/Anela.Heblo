using Anela.Heblo.Application.Features.Manufacture.Contracts;

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

    /// <summary>
    /// Confirm single-phase production with direct material-to-product conversion
    /// </summary>
    Task<ConfirmSinglePhaseProductionResult> ConfirmSinglePhaseProductionAsync(
        int orderId,
        Dictionary<int, decimal> productActualQuantities,
        string? changeReason = null,
        CancellationToken cancellationToken = default);
}