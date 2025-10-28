using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Manufacture.Validators;

public class CalculateBatchPlanRequestValidator : AbstractValidator<CalculateBatchPlanRequest>
{
    public CalculateBatchPlanRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Semiproduct code is required.");

        RuleFor(x => x.ControlMode)
            .IsInEnum()
            .WithMessage("Control mode must be a valid value.");

        RuleFor(x => x.MmqMultiplier)
            .NotNull()
            .When(x => x.ControlMode == BatchPlanControlMode.MmqMultiplier)
            .WithMessage("MMQ Multiplier is required when using MMQ Multiplier mode.");

        RuleFor(x => x.MmqMultiplier)
            .GreaterThan(0)
            .When(x => x.ControlMode == BatchPlanControlMode.MmqMultiplier && x.MmqMultiplier.HasValue)
            .WithMessage("MMQ Multiplier must be greater than 0.");

        RuleFor(x => x.TotalWeightToUse)
            .NotNull()
            .When(x => x.ControlMode == BatchPlanControlMode.TotalWeight)
            .WithMessage("Total weight is required when using Total Weight mode.");

        RuleFor(x => x.TotalWeightToUse)
            .GreaterThan(0)
            .When(x => x.ControlMode == BatchPlanControlMode.TotalWeight && x.TotalWeightToUse.HasValue)
            .WithMessage("Total weight must be greater than 0.");

        RuleFor(x => x.TargetDaysCoverage)
            .NotNull()
            .When(x => x.ControlMode == BatchPlanControlMode.TargetDaysCoverage)
            .WithMessage("Target days coverage is required when using Target Coverage mode.");

        RuleFor(x => x.TargetDaysCoverage)
            .GreaterThan(0)
            .When(x => x.ControlMode == BatchPlanControlMode.TargetDaysCoverage && x.TargetDaysCoverage.HasValue)
            .WithMessage("Target days coverage must be greater than 0.");

        RuleFor(x => x.FromDate)
            .LessThanOrEqualTo(x => x.ToDate)
            .When(x => x.FromDate.HasValue && x.ToDate.HasValue)
            .WithMessage("From date must be before or equal to to date.");

        RuleForEach(x => x.ProductConstraints)
            .SetValidator(new ProductSizeConstraintValidator());
    }
}

public class ProductSizeConstraintValidator : AbstractValidator<ProductSizeConstraint>
{
    public ProductSizeConstraintValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Product code is required.");

        RuleFor(x => x.FixedQuantity)
            .GreaterThanOrEqualTo(0)
            .When(x => x.IsFixed)
            .WithMessage("Fixed quantity must be greater than or equal to 0 when product is fixed.");

        RuleFor(x => x.FixedQuantity)
            .NotNull()
            .When(x => x.IsFixed)
            .WithMessage("Fixed quantity is required when product is fixed.");
    }
}