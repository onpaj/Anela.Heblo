using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmSinglePhaseProduction;

public class ConfirmSinglePhaseProductionHandler : IRequestHandler<ConfirmSinglePhaseProductionRequest, ConfirmSinglePhaseProductionResponse>
{
    private readonly IManufactureOrderRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ConfirmSinglePhaseProductionHandler(
        IManufactureOrderRepository repository,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<ConfirmSinglePhaseProductionResponse> Handle(
        ConfirmSinglePhaseProductionRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _repository.GetOrderByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
        {
            return ConfirmSinglePhaseProductionResponse.Failed("Manufacture order not found");
        }

        // Validation: Must be single-phase and in correct state
        if (order.ManufactureType != ManufactureType.SinglePhase)
        {
            return ConfirmSinglePhaseProductionResponse.Failed("Order is not single-phase");
        }

        if (order.State != ManufactureOrderState.Planned)
        {
            return ConfirmSinglePhaseProductionResponse.Failed("Order must be in Planned state");
        }

        var currentTime = _timeProvider.GetUtcNow().DateTime;

        // State transition: Planned → InProduction
        order.State = ManufactureOrderState.InProduction;
        order.StateChangedAt = currentTime;
        order.StateChangedByUser = request.UserId;

        // Direct production: Update product quantities directly
        foreach (var productUpdate in request.ProductActualQuantities)
        {
            var product = order.Products.FirstOrDefault(p => p.Id == productUpdate.Key);
            if (product == null)
            {
                return ConfirmSinglePhaseProductionResponse.Failed($"Product with ID {productUpdate.Key} not found in order");
            }

            product.ActualQuantity = productUpdate.Value;
            product.SetDefaultLot(currentTime);

            // For single-phase, we need to get expiration months from the product
            // For now, using a default expiration calculation
            product.ExpirationDate = ManufactureOrderExtensions.GetDefaultExpiration(currentTime, 12); // Default 12 months
        }

        // Final state transition: InProduction → Completed
        order.State = ManufactureOrderState.Completed;
        order.StateChangedAt = currentTime;

        await _repository.UpdateOrderAsync(order, cancellationToken);

        // TODO: ERP integration for single-phase orders
        // await _erpIntegrationService.CreateProductionDocumentAsync(order);

        return ConfirmSinglePhaseProductionResponse.Successful(order.Id, currentTime);
    }
}