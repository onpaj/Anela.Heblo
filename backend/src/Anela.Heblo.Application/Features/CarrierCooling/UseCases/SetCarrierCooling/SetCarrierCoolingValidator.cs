using Anela.Heblo.Domain.Features.Logistics;
using FluentValidation;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingValidator : AbstractValidator<SetCarrierCoolingRequest>
{
    public SetCarrierCoolingValidator(IShippingMethodCatalog catalog)
    {
        RuleFor(x => x.Carrier).IsInEnum();
        RuleFor(x => x.DeliveryHandling).IsInEnum();
        RuleFor(x => x.Cooling).IsInEnum();
        RuleFor(x => x.CoolingText).MaximumLength(50);

        RuleFor(x => x)
            .Must(x => catalog.GetAvailableDeliveryOptions()
                .Any(o => o.Carrier == x.Carrier && o.Handling == x.DeliveryHandling))
            .WithMessage("Combination of Carrier and DeliveryHandling is not available.");
    }
}
