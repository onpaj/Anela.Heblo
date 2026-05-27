using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using FluentValidation;

namespace Anela.Heblo.Application.Features.ShipmentLabels.Validators;

public class CreateOrderShipmentRequestValidator : AbstractValidator<CreateOrderShipmentRequest>
{
    public CreateOrderShipmentRequestValidator()
    {
        RuleFor(x => x.OrderCode)
            .NotEmpty()
            .WithMessage("OrderCode is required.");
    }
}
