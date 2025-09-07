using FluentValidation;

namespace Anela.Heblo.Application.Features.Analytics.Validators;

public class GetProductMarginAnalysisRequestValidator : AbstractValidator<GetProductMarginAnalysisRequest>
{
    public GetProductMarginAnalysisRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty()
            .WithMessage(AnalyticsConstants.ValidationMessages.PRODUCT_ID_REQUIRED);

        RuleFor(x => x.StartDate)
            .LessThanOrEqualTo(x => x.EndDate)
            .WithMessage(AnalyticsConstants.ValidationMessages.INVALID_DATE_RANGE);

        // Optional: Add reasonable period limit for performance
        RuleFor(x => x)
            .Must(x => (x.EndDate - x.StartDate).TotalDays <= AnalyticsConstants.MAX_REPORT_PERIOD_DAYS)
            .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.PERIOD_TOO_LONG, AnalyticsConstants.MAX_REPORT_PERIOD_DAYS));

        RuleFor(x => x)
            .Must(x => (x.EndDate - x.StartDate).TotalDays >= AnalyticsConstants.MIN_REPORT_PERIOD_DAYS)
            .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.PERIOD_TOO_SHORT, AnalyticsConstants.MIN_REPORT_PERIOD_DAYS));

        // IncludeBreakdown is optional boolean, no validation needed
    }
}