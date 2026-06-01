using Anela.Heblo.Application.Features.Photobank.UseCases.AddPhotoTag;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class AddPhotoTagRequestValidator : AbstractValidator<AddPhotoTagRequest>
{
    public AddPhotoTagRequestValidator()
    {
        RuleFor(x => x.PhotoId)
            .GreaterThan(0)
            .WithMessage("PhotoId must be a positive integer");

        RuleFor(x => x.TagName)
            .NotEmpty()
            .WithMessage("TagName is required")
            .MaximumLength(100)
            .WithMessage("TagName cannot exceed 100 characters");
    }
}
