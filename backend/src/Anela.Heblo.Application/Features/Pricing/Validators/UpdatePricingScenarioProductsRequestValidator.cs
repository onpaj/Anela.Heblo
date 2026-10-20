using Anela.Heblo.Application.Features.Pricing.UseCases.UpdatePricingScenarioProducts;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Pricing.Validators;

public class UpdatePricingScenarioProductsRequestValidator : AbstractValidator<UpdatePricingScenarioProductsRequest>
{
    public UpdatePricingScenarioProductsRequestValidator()
    {
        RuleFor(x => x.ScenarioId).NotEmpty();

        RuleFor(x => x)
            .Must(x => x.Edits.Count > 0 || x.RemoveProductCodes.Count > 0 || x.Name is not null || x.Description is not null)
            .WithMessage("The request must contain at least one edit, removal, name or description change.");

        // Same bounds as SavePricingScenarioRequestValidator; only a value that IS set is checked.
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Description).MaximumLength(2000);

        RuleForEach(x => x.Edits).ChildRules(edit =>
        {
            edit.RuleFor(e => e.ProductCode).NotEmpty();
            edit.RuleFor(e => e.Field).IsInEnum();
        });
        RuleForEach(x => x.RemoveProductCodes).NotEmpty();
    }
}
