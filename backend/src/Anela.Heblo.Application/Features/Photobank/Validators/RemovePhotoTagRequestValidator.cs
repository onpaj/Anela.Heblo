using Anela.Heblo.Application.Features.Photobank.UseCases.RemovePhotoTag;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class RemovePhotoTagRequestValidator : AbstractValidator<RemovePhotoTagRequest>
{
    public RemovePhotoTagRequestValidator()
    {
        RuleFor(x => x.PhotoId)
            .GreaterThan(0)
            .WithMessage("PhotoId must be a positive integer");

        RuleFor(x => x.TagId)
            .GreaterThan(0)
            .WithMessage("TagId must be a positive integer");
    }
}
