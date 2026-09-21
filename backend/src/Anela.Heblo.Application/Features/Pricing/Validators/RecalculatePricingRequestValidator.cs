using Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Pricing.Validators;

public class RecalculatePricingRequestValidator : AbstractValidator<RecalculatePricingRequest>
{
    public RecalculatePricingRequestValidator()
    {
        RuleForEach(x => x.Overrides)
            .ChildRules(o => o.RuleFor(x => x.ProductCode).NotEmpty());

        When(x => x.Edit is not null, () =>
        {
            RuleFor(x => x.Edit!.ProductCode).NotEmpty();
            RuleFor(x => x.Edit!.Field).IsInEnum();
        });
    }
}
