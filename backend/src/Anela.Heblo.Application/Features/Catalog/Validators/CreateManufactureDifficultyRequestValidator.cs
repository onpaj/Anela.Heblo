using Anela.Heblo.Application.Features.Catalog.Model;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Validators;

public class CreateManufactureDifficultyRequestValidator : AbstractValidator<CreateManufactureDifficultyRequest>
{
    public CreateManufactureDifficultyRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Product code is required")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.DifficultyValue)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Difficulty value must be non-negative");

        RuleFor(x => x.ValidFrom)
            .LessThan(x => x.ValidTo)
            .WithMessage("ValidFrom must be earlier than ValidTo")
            .When(x => x.ValidFrom.HasValue && x.ValidTo.HasValue);

        RuleFor(x => x.ValidTo)
            .GreaterThan(x => x.ValidFrom)
            .WithMessage("ValidTo must be later than ValidFrom")
            .When(x => x.ValidFrom.HasValue && x.ValidTo.HasValue);
    }
}