using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ManufactureOrderApplicationService : IManufactureOrderApplicationService
{
    private readonly IConfirmSemiProductManufactureWorkflow _semiProductWorkflow;
    private readonly IConfirmProductCompletionWorkflow _productCompletionWorkflow;

    public ManufactureOrderApplicationService(
        IConfirmSemiProductManufactureWorkflow semiProductWorkflow,
        IConfirmProductCompletionWorkflow productCompletionWorkflow)
    {
        _semiProductWorkflow = semiProductWorkflow;
        _productCompletionWorkflow = productCompletionWorkflow;
    }

    public Task<ConfirmSemiProductManufactureResult> ConfirmSemiProductManufactureAsync(
        int orderId,
        decimal actualQuantity,
        string? changeReason = null,
        CancellationToken cancellationToken = default)
    {
        return _semiProductWorkflow.ExecuteAsync(orderId, actualQuantity, changeReason, cancellationToken);
    }

    public Task<ConfirmProductCompletionResult> ConfirmProductCompletionAsync(
        int orderId,
        Dictionary<int, decimal> productActualQuantities,
        bool overrideConfirmed = false,
        string? changeReason = null,
        CancellationToken cancellationToken = default)
    {
        return _productCompletionWorkflow.ExecuteAsync(
            orderId, productActualQuantities, overrideConfirmed, changeReason, cancellationToken);
    }
}
