using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Validators;

public class GetProductStatisticsRequestValidator : AbstractValidator<GetProductStatisticsRequest>
{
    /// <summary>
    /// Chart legibility and payload cap. This is the only rule bounding the response size,
    /// so it lives here rather than in the frontend alone.
    /// </summary>
    public const int MaxProducts = 10;

    public GetProductStatisticsRequestValidator()
    {
        RuleFor(x => x.ProductCodes)
            .NotEmpty()
            .WithMessage("At least one product code is required")
            .Must(codes => codes == null || codes.Count <= MaxProducts)
            .WithMessage($"At most {MaxProducts} product codes can be requested at once");

        RuleForEach(x => x.ProductCodes)
            .NotEmpty()
            .WithMessage("Product code cannot be empty")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.DateFrom)
            .Must(BeAValidMonth)
            .WithMessage("DateFrom must be in yyyy-MM format");

        RuleFor(x => x.DateTo)
            .Must(BeAValidMonth)
            .WithMessage("DateTo must be in yyyy-MM format");

        RuleFor(x => x)
            .Must(HaveOrderedRange)
            .WithMessage("DateFrom must not be later than DateTo")
            .When(x => BeAValidMonth(x.DateFrom) && BeAValidMonth(x.DateTo));
    }

    private static bool BeAValidMonth(string? month) =>
        month != null && MonthRange.TryParse(month, out _, out _);

    private static bool HaveOrderedRange(GetProductStatisticsRequest request) =>
        string.CompareOrdinal(request.DateFrom, request.DateTo) <= 0;
}
