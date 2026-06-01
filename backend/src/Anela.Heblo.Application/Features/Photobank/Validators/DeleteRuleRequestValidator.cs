using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRule;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class DeleteRuleRequestValidator : AbstractValidator<DeleteRuleRequest>
{
    public DeleteRuleRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be a positive integer");
    }
}
