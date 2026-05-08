using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.RetagPhotos;

public class RetagPhotosRequestValidator : AbstractValidator<RetagPhotosRequest>
{
    private const int MaxPhotoIds = 5_000;

    public RetagPhotosRequestValidator()
    {
        RuleFor(x => x.PhotoIds)
            .NotNull()
            .Must(ids => ids.Length > 0)
            .WithMessage("At least one photo ID is required")
            .Must(ids => ids.Length <= MaxPhotoIds)
            .WithMessage($"Cannot retag more than {MaxPhotoIds} photos at once");
    }
}
