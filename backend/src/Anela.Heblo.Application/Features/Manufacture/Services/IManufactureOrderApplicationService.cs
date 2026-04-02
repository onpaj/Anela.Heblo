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
        bool overrideConfirmed = false,
        string? changeReason = null,
        CancellationToken cancellationToken = default);
}