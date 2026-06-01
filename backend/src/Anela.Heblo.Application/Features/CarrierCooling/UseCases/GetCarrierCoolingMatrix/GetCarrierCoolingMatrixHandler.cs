using Anela.Heblo.Application.Features.CarrierCooling.Contracts;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;

public class GetCarrierCoolingMatrixHandler : IRequestHandler<GetCarrierCoolingMatrixRequest, GetCarrierCoolingMatrixResponse>
{
    private readonly ICarrierCoolingRepository _repository;
    private readonly IShippingMethodCatalog _catalog;

    public GetCarrierCoolingMatrixHandler(ICarrierCoolingRepository repository, IShippingMethodCatalog catalog)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<GetCarrierCoolingMatrixResponse> Handle(
        GetCarrierCoolingMatrixRequest request,
        CancellationToken cancellationToken)
    {
        var available = _catalog.GetAvailableDeliveryOptions();
        var stored = await _repository.GetAllAsync(cancellationToken);
        var storedLookup = stored.ToDictionary(s => (s.Carrier, s.DeliveryHandling), s => s.Cooling);

        var groups = available
            .GroupBy(x => x.Carrier)
            .Select(g => new CarrierGroupDto
            {
                Carrier = g.Key,
                Rows = g.Select(x => new CarrierCoolingRowDto
                {
                    DeliveryHandling = x.Handling,
                    Cooling = storedLookup.TryGetValue((x.Carrier, x.Handling), out var c) ? c : Cooling.None,
                }).ToList(),
            })
            .ToList();

        return new GetCarrierCoolingMatrixResponse { Groups = groups };
    }
}
