using FluentValidation;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.ResolvePriceSyncConflict;

public class ResolvePriceSyncConflictRequestValidator : AbstractValidator<ResolvePriceSyncConflictRequest>
{
    public ResolvePriceSyncConflictRequestValidator()
    {
        RuleFor(r => r.ProductCode).NotEmpty();
        RuleFor(r => r.Target).IsInEnum();
        RuleFor(r => r.Resolution).IsInEnum();
    }
}
