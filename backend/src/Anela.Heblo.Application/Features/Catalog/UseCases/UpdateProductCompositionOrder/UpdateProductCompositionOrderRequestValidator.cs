using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;

public class UpdateProductCompositionOrderRequestValidator
    : AbstractValidator<UpdateProductCompositionOrderRequest>
{
    public UpdateProductCompositionOrderRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Product code is required")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.Order)
            .NotNull()
            .WithMessage("Order list is required");

        RuleForEach(x => x.Order)
            .ChildRules(order =>
            {
                order.RuleFor(x => x.IngredientProductCode)
                    .NotEmpty()
                    .WithMessage("Ingredient product code is required")
                    .MaximumLength(50)
                    .WithMessage("Ingredient product code cannot exceed 50 characters");

                order.RuleFor(x => x.SortOrder)
                    .GreaterThan(0)
                    .WithMessage("Sort order must be greater than 0");
            });
    }
}
