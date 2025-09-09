using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Manufacture.Validators;

public class CalculateBatchBySizeRequestValidator : AbstractValidator<CalculatedBatchSizeRequest>
{
    public CalculateBatchBySizeRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Product code is required")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.DesiredBatchSize)
            .GreaterThan(0)
            .WithMessage("Desired batch size must be greater than 0")
            .LessThanOrEqualTo(999999.99)
            .WithMessage("Desired batch size cannot exceed 999,999.99 grams");
    }
}