using System.Globalization;
using FluentValidation;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;

public class GetConsumptionHistoryRequestValidator : AbstractValidator<GetConsumptionHistoryRequest>
{
    public GetConsumptionHistoryRequestValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage("PageNumber must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("PageSize must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("PageSize must not exceed 100.");

        RuleFor(x => x.DateFrom)
            .Must(BeValidDateOrEmpty).WithMessage("DateFrom must be in yyyy-MM-dd format.");

        RuleFor(x => x.DateTo)
            .Must(BeValidDateOrEmpty).WithMessage("DateTo must be in yyyy-MM-dd format.");
    }

    private static bool BeValidDateOrEmpty(string? value)
        => string.IsNullOrWhiteSpace(value)
           || DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
