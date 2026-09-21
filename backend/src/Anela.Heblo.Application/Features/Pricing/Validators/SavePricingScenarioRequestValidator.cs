using Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Pricing.Validators;

public class SavePricingScenarioRequestValidator : AbstractValidator<SavePricingScenarioRequest>
{
    public SavePricingScenarioRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);

        RuleForEach(x => x.Overrides).SetValidator(new PricingOverrideDtoValidator());
        RuleFor(x => x.Overrides)
            .Must(PricingOverrideDtoValidator.HasDistinctProductCodes)
            .WithMessage(PricingOverrideDtoValidator.DuplicateProductCodesMessage);
    }
}
