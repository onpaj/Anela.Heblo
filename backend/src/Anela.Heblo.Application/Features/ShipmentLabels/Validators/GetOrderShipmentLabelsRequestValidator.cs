using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using FluentValidation;

namespace Anela.Heblo.Application.Features.ShipmentLabels.Validators;

public class GetOrderShipmentLabelsRequestValidator : AbstractValidator<GetOrderShipmentLabelsRequest>
{
    public GetOrderShipmentLabelsRequestValidator()
    {
        RuleFor(x => x.OrderCode)
            .NotEmpty()
            .WithMessage("OrderCode is required.");
    }
}
