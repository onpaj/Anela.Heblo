using Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class CreateTagRequestValidator : AbstractValidator<CreateTagRequest>
{
    public CreateTagRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required")
            .MaximumLength(100)
            .WithMessage("Name cannot exceed 100 characters");
    }
}
