using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingRequest : IRequest<SetCarrierCoolingResponse>
{
    public Carriers Carrier { get; set; }
    public DeliveryHandling DeliveryHandling { get; set; }
    public Cooling Cooling { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
