using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Manufacture.Validators;

public class GetManufacturingStockAnalysisRequestValidator : AbstractValidator<GetManufacturingStockAnalysisRequest>
{
    public GetManufacturingStockAnalysisRequestValidator()
    {
        RuleFor(x => x.PageSize)
            .GreaterThanOrEqualTo(ManufactureConstants.MIN_PAGE_SIZE)
            .WithMessage($"PageSize must be at least {ManufactureConstants.MIN_PAGE_SIZE}")
            .LessThanOrEqualTo(ManufactureConstants.MAX_PAGE_SIZE)
            .WithMessage($"PageSize cannot exceed {ManufactureConstants.MAX_PAGE_SIZE}");

        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(ManufactureConstants.MIN_PAGE_NUMBER)
            .WithMessage($"PageNumber must be at least {ManufactureConstants.MIN_PAGE_NUMBER}");

        // Custom period validation
        When(x => x.TimePeriod == TimePeriodFilter.CustomPeriod, () =>
        {
            RuleFor(x => x.CustomFromDate)
                .NotNull()
                .WithMessage("CustomFromDate is required when using CustomPeriod");

            RuleFor(x => x.CustomToDate)
                .NotNull()
                .WithMessage("CustomToDate is required when using CustomPeriod");

            RuleFor(x => x)
                .Must(x => x.CustomFromDate.HasValue && x.CustomToDate.HasValue &&
                          x.CustomFromDate.Value <= x.CustomToDate.Value)
                .WithMessage("CustomFromDate must be before or equal to CustomToDate")
                .When(x => x.CustomFromDate.HasValue && x.CustomToDate.HasValue);
        });

        // SearchTerm validation (optional constraint)
        When(x => !string.IsNullOrWhiteSpace(x.SearchTerm), () =>
        {
            RuleFor(x => x.SearchTerm)
                .MaximumLength(100)
                .WithMessage("SearchTerm cannot exceed 100 characters");
        });

        // ProductFamily validation (optional constraint)
        When(x => !string.IsNullOrWhiteSpace(x.ProductFamily), () =>
        {
            RuleFor(x => x.ProductFamily)
                .MaximumLength(50)
                .WithMessage("ProductFamily cannot exceed 50 characters");
        });
    }
}