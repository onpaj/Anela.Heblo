using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateProductWeight;

public class RecalculateProductWeightRequestValidator : AbstractValidator<RecalculateProductWeightRequest>
{
    public RecalculateProductWeightRequestValidator()
    {
        When(x => !string.IsNullOrEmpty(x.ProductCode), () =>
        {
            RuleFor(x => x.ProductCode)
                .NotEmpty()
                .WithMessage("ProductCode cannot be empty when specified")
                .MaximumLength(50)
                .WithMessage("ProductCode cannot exceed 50 characters");
        });
    }
}