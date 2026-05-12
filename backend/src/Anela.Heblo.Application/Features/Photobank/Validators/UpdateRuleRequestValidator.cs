using Anela.Heblo.Application.Features.Photobank.UseCases.UpdateRule;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class UpdateRuleRequestValidator : AbstractValidator<UpdateRuleRequest>
{
    public UpdateRuleRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be a positive integer");

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

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0);
    }
}
