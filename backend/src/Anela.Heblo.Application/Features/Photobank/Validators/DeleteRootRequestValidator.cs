using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRoot;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class DeleteRootRequestValidator : AbstractValidator<DeleteRootRequest>
{
    public DeleteRootRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be a positive integer");
    }
}
