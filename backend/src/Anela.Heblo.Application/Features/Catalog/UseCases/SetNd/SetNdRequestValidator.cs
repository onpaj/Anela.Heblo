using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SetNd;

public class SetNdRequestValidator : AbstractValidator<SetNdRequest>
{
    private const int MaxProductCodeLength = 50;

    public SetNdRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("ProductCode is required")
            .MaximumLength(MaxProductCodeLength)
            .WithMessage($"ProductCode cannot exceed {MaxProductCodeLength} characters");
    }
}
