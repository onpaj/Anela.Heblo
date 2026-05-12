using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class BulkAddPhotoTagByIdsRequestValidator : AbstractValidator<BulkAddPhotoTagByIdsRequest>
{
    public BulkAddPhotoTagByIdsRequestValidator()
    {
        RuleFor(x => x.TagName)
            .NotEmpty()
            .WithMessage("TagName is required")
            .MaximumLength(100)
            .WithMessage("TagName cannot exceed 100 characters");

        RuleFor(x => x.PhotoIds)
            .NotNull()
            .WithMessage("PhotoIds is required")
            .NotEmpty()
            .WithMessage("PhotoIds must contain at least one ID");
    }
}
