using FluentValidation;
using Anela.Heblo.Application.Features.Purchase.Model;

namespace Anela.Heblo.Application.Features.Purchase.Validators;

public class UpdatePurchaseOrderRequestValidator : AbstractValidator<UpdatePurchaseOrderRequest>
{
    public UpdatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Invalid purchase order ID");

        RuleFor(x => x.SupplierName)
            .NotEmpty().WithMessage("Supplier name is required")
            .MaximumLength(200).WithMessage("Supplier name cannot exceed 200 characters");

        RuleFor(x => x.ExpectedDeliveryDate)
            .Must(BeAReasonableDate).When(x => x.ExpectedDeliveryDate.HasValue)
            .WithMessage("Expected delivery date must be reasonable (not more than 2 years in the future)");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters");

        RuleFor(x => x.OrderNumber)
            .MaximumLength(50).WithMessage("Order number cannot exceed 50 characters");

        RuleFor(x => x.Lines)
            .NotNull().WithMessage("Order lines are required")
            .NotEmpty().WithMessage("At least one order line is required")
            .Must(lines => lines.Count <= 100).WithMessage("A purchase order cannot have more than 100 line items");

        RuleForEach(x => x.Lines)
            .SetValidator(new UpdatePurchaseOrderLineRequestValidator());
    }

    private bool BeAReasonableDate(DateTime? date)
    {
        if (!date.HasValue)
            return true;

        var maxFutureDate = DateTime.UtcNow.AddYears(2);
        var minPastDate = DateTime.UtcNow.AddYears(-10);

        return date.Value >= minPastDate && date.Value <= maxFutureDate;
    }
}

public class UpdatePurchaseOrderLineRequestValidator : AbstractValidator<UpdatePurchaseOrderLineRequest>
{
    public UpdatePurchaseOrderLineRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).When(x => x.Id.HasValue)
            .WithMessage("Invalid line ID");

        RuleFor(x => x.MaterialId)
            .NotEmpty().WithMessage("Material ID is required")
            .MaximumLength(50).WithMessage("Material ID cannot exceed 50 characters");

        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("Name cannot exceed 200 characters");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0")
            .LessThanOrEqualTo(999999.99m).WithMessage("Quantity cannot exceed 999999.99");

        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Unit price cannot be negative")
            .LessThanOrEqualTo(999999.99m).WithMessage("Unit price cannot exceed 999999.99");

        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Notes cannot exceed 500 characters");
    }
}