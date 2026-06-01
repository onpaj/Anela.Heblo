using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Manufacture.Validators;

public class CalculateBatchByIngredientRequestValidator : AbstractValidator<CalculateBatchByIngredientRequest>
{
    public CalculateBatchByIngredientRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Product code is required")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.IngredientCode)
            .NotEmpty()
            .WithMessage("Ingredient code is required")
            .MaximumLength(50)
            .WithMessage("Ingredient code cannot exceed 50 characters");

        RuleFor(x => x.DesiredIngredientAmount)
            .GreaterThan(0)
            .WithMessage("Desired ingredient amount must be greater than 0")
            .LessThanOrEqualTo(999999.99)
            .WithMessage("Desired ingredient amount cannot exceed 999,999.99 grams");
    }
}