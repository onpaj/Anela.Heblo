using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Validators;

public class GetCatalogDetailRequestValidator : AbstractValidator<GetCatalogDetailRequest>
{
    public GetCatalogDetailRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Product code is required")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.MonthsBack)
            .GreaterThanOrEqualTo(0)
            .WithMessage("MonthsBack cannot be negative")
            .LessThanOrEqualTo(CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)
            .WithMessage($"MonthsBack cannot exceed {CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD}");
    }
}