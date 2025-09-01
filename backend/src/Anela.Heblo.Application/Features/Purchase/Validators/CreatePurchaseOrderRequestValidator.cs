using FluentValidation;
using Anela.Heblo.Application.Features.Purchase.Model;

namespace Anela.Heblo.Application.Features.Purchase.Validators;

public class CreatePurchaseOrderRequestValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierId)
            .GreaterThan(0L).WithMessage("Supplier is required");

        RuleFor(x => x.OrderDate)
            .NotEmpty().WithMessage("Order date is required")
            .Must(BeAValidDate).WithMessage("Order date must be a valid date")
            .Must(NotBeTooFarInFuture).WithMessage("Order date cannot be more than 30 days in the future");

        RuleFor(x => x.ExpectedDeliveryDate)
            .Must(BeAValidDate).When(x => !string.IsNullOrEmpty(x.ExpectedDeliveryDate))
            .WithMessage("Expected delivery date must be a valid date")
            .Must((request, expectedDate) => BeAfterOrEqualToOrderDate(request.OrderDate, expectedDate))
            .When(x => !string.IsNullOrEmpty(x.ExpectedDeliveryDate))
            .WithMessage("Expected delivery date must be on or after the order date");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters");

        RuleFor(x => x.OrderNumber)
            .MaximumLength(50).WithMessage("Order number cannot exceed 50 characters");

        RuleForEach(x => x.Lines)
            .SetValidator(new CreatePurchaseOrderLineRequestValidator())
            .When(x => x.Lines != null);

        RuleFor(x => x.Lines)
            .Must(lines => lines == null || lines.Count <= 100)
            .WithMessage("A purchase order cannot have more than 100 line items");
    }

    private bool BeAValidDate(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return false;

        return DateTime.TryParse(dateString, out _);
    }

    private bool NotBeTooFarInFuture(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString) || !DateTime.TryParse(dateString, out var date))
            return false;

        var maxFutureDate = DateTime.UtcNow.AddDays(30);
        return date.Date <= maxFutureDate.Date;
    }

    private bool BeAfterOrEqualToOrderDate(string? orderDateString, string? expectedDateString)
    {
        if (string.IsNullOrEmpty(orderDateString) || string.IsNullOrEmpty(expectedDateString))
            return true;

        if (!DateTime.TryParse(orderDateString, out var orderDate) ||
            !DateTime.TryParse(expectedDateString, out var expectedDate))
            return false;

        return expectedDate.Date >= orderDate.Date;
    }
}

public class CreatePurchaseOrderLineRequestValidator : AbstractValidator<CreatePurchaseOrderLineRequest>
{
    public CreatePurchaseOrderLineRequestValidator()
    {
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