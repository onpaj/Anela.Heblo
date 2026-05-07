using Anela.Heblo.Application.Features.Photobank.UseCases.AddRule;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class AddRuleRequestValidator : AbstractValidator<AddRuleRequest>
{
    public AddRuleRequestValidator()
    {
        RuleFor(x => x.PathPattern)
            .NotEmpty()
            .WithMessage("PathPattern is required")
            .MaximumLength(500)
            .WithMessage("PathPattern cannot exceed 500 characters")
            .Must(PhotobankValidationHelpers.BeValidRegex)
            .WithMessage("Invalid regular expression pattern.");

        RuleFor(x => x.TagName)
            .NotEmpty()
            .WithMessage("TagName is required")
            .MaximumLength(100)
            .WithMessage("TagName cannot exceed 100 characters");
    }
}
