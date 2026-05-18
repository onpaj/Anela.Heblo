using Anela.Heblo.Domain.Features.Logistics;
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingHandler : IRequestHandler<SetCarrierCoolingRequest, SetCarrierCoolingResponse>
{
    private readonly ICarrierCoolingRepository _repository;

    public SetCarrierCoolingHandler(ICarrierCoolingRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<SetCarrierCoolingResponse> Handle(
        SetCarrierCoolingRequest request,
        CancellationToken cancellationToken)
    {
        var setting = new CarrierCoolingSetting(
            request.Carrier,
            request.DeliveryHandling,
            request.Cooling,
            request.ModifiedBy);

        await _repository.UpsertAsync(setting, cancellationToken);

        return new SetCarrierCoolingResponse();
    }
}
