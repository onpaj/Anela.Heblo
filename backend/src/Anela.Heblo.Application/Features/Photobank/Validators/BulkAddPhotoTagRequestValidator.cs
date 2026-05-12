using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class BulkAddPhotoTagRequestValidator : AbstractValidator<BulkAddPhotoTagRequest>
{
    public BulkAddPhotoTagRequestValidator()
    {
        RuleFor(x => x.TagName)
            .NotEmpty()
            .WithMessage("TagName is required")
            .MaximumLength(100)
            .WithMessage("TagName cannot exceed 100 characters");

        RuleFor(x => x)
            .Must(x =>
                !string.IsNullOrWhiteSpace(x.Search)
                || (x.Tags != null && x.Tags.Exists(t => !string.IsNullOrWhiteSpace(t))))
            .WithMessage("At least one filter (Tags or Search) must be provided")
            .WithErrorCode(((int)Shared.ErrorCodes.BulkTagFiltersRequired).ToString());
    }
}
