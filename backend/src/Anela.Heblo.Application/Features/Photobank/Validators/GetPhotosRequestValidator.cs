using Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

public class GetPhotosRequestValidator : AbstractValidator<GetPhotosRequest>
{
    private const int MaxPageSize = 200;

    public GetPhotosRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThan(0)
            .WithMessage("Page must be a positive integer");

        RuleFor(x => x.PageSize)
            .GreaterThan(0)
            .WithMessage("PageSize must be a positive integer")
            .LessThanOrEqualTo(MaxPageSize)
            .WithMessage($"PageSize cannot exceed {MaxPageSize}");
    }
}
