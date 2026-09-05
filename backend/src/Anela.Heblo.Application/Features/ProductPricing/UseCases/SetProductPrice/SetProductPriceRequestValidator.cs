using FluentValidation;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceRequestValidator : AbstractValidator<SetProductPriceRequest>
{
    private const decimal MaxPriceWithVat = 1_000_000m;

    public SetProductPriceRequestValidator()
    {
        RuleFor(r => r.ProductCode).NotEmpty();
        RuleFor(r => r.PriceWithVat).GreaterThan(0).LessThanOrEqualTo(MaxPriceWithVat);
    }
}
