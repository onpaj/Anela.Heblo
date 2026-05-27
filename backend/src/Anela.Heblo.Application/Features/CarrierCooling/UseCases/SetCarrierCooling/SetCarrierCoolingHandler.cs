using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingHandler : IRequestHandler<SetCarrierCoolingRequest, SetCarrierCoolingResponse>
{
    private readonly ICarrierCoolingRepository _repository;
    private readonly IShippingMethodCatalog _catalog;

    public SetCarrierCoolingHandler(ICarrierCoolingRepository repository, IShippingMethodCatalog catalog)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<SetCarrierCoolingResponse> Handle(
        SetCarrierCoolingRequest request,
        CancellationToken cancellationToken)
    {
        var isValidCombo = _catalog.GetAvailableDeliveryOptions()
            .Any(o => o.Carrier == request.Carrier && o.Handling == request.DeliveryHandling);

        if (!isValidCombo)
        {
            return new SetCarrierCoolingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string>
                {
                    { "message", $"Combination of Carrier '{request.Carrier}' and DeliveryHandling '{request.DeliveryHandling}' is not available." }
                }
            };
        }

        var setting = new CarrierCoolingSetting(
            request.Carrier,
            request.DeliveryHandling,
            request.Cooling,
            request.ModifiedBy);

        await _repository.UpsertAsync(setting, cancellationToken);

        return new SetCarrierCoolingResponse();
    }
}
