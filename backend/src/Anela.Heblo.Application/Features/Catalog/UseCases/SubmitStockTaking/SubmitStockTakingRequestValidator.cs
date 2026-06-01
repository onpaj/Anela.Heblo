using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;

public class SubmitStockTakingRequestValidator : AbstractValidator<SubmitStockTakingRequest>
{
    public SubmitStockTakingRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Product code is required")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.TargetAmount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Target amount must be greater than or equal to 0")
            .LessThan(100000)
            .WithMessage("Target amount must be less than 1,000");
    }
}