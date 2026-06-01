using Anela.Heblo.Application.Features.Catalog.UseCases.GetManufactureDifficultySettings;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Validators;

public class GetManufactureDifficultyHistoryRequestValidator : AbstractValidator<GetManufactureDifficultySettingsRequest>
{
    public GetManufactureDifficultyHistoryRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Product code is required")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");
    }
}