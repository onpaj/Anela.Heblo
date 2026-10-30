using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SetTopProduct;

public class SetTopProductRequestValidator : AbstractValidator<SetTopProductRequest>
{
    private const int MaxProductCodeLength = 50;

    public SetTopProductRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("ProductCode is required")
            .MaximumLength(MaxProductCodeLength)
            .WithMessage($"ProductCode cannot exceed {MaxProductCodeLength} characters");
    }
}
