using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.RetagPhotos;

public class RetagPhotosRequestValidator : AbstractValidator<RetagPhotosRequest>
{
    private const int MaxPhotoIds = 5_000;

    public RetagPhotosRequestValidator()
    {
        RuleFor(x => x.PhotoIds)
            .NotNull()
                .WithMessage("PhotoIds is required")
            .NotEmpty()
                .WithMessage("At least one photo ID is required")
            .Must(ids => ids.Length <= MaxPhotoIds)
                .WithMessage($"Cannot retag more than {MaxPhotoIds} photos at once")
                .When(x => x.PhotoIds != null && x.PhotoIds.Length > 0);
    }
}
