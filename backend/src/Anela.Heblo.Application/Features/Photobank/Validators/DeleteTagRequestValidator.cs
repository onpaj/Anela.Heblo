using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class DeleteTagRequestValidator : AbstractValidator<DeleteTagRequest>
{
    public DeleteTagRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than 0");
    }
}
