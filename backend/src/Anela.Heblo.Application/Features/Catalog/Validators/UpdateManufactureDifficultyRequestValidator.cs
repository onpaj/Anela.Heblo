using Anela.Heblo.Application.Features.Catalog.Model;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Validators;

public class UpdateManufactureDifficultyRequestValidator : AbstractValidator<UpdateManufactureDifficultyRequest>
{
    public UpdateManufactureDifficultyRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("ID must be a positive integer");

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