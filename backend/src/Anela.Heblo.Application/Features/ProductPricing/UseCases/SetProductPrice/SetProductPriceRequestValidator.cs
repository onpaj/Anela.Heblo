using FluentValidation;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceRequestValidator : AbstractValidator<SetProductPriceRequest>
{
    public SetProductPriceRequestValidator()
    {
        RuleFor(r => r.ProductCode).NotEmpty().MaximumLength(50);

        // Shoptet treats a literal 0 as a genuine free price, not "clear the price".
        RuleFor(r => r.PriceWithVat).GreaterThan(0m);
    }
}
