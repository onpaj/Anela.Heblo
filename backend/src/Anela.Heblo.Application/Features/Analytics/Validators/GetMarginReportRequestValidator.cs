using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;
using Anela.Heblo.Application.Shared;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Analytics.Validators;

public class GetMarginReportRequestValidator : AbstractValidator<GetMarginReportRequest>
{
    public GetMarginReportRequestValidator()
    {
        RuleFor(x => x.StartDate)
            .LessThanOrEqualTo(x => x.EndDate)
            .WithErrorCode(((int)ErrorCodes.InvalidDateRange).ToString())
            .WithState(x => (object)new Dictionary<string, string>
            {
                { "startDate", x.StartDate.ToString("yyyy-MM-dd") },
                { "endDate", x.EndDate.ToString("yyyy-MM-dd") }
            })
            .WithMessage(AnalyticsConstants.ValidationMessages.INVALID_DATE_RANGE);

        RuleFor(x => x)
            .Must(x => (x.EndDate - x.StartDate).TotalDays <= AnalyticsConstants.MAX_REPORT_PERIOD_DAYS)
            .WithErrorCode(((int)ErrorCodes.InvalidReportPeriod).ToString())
            .WithState(x => (object)new Dictionary<string, string>
            {
                { "period", $"{(int)(x.EndDate - x.StartDate).TotalDays} days (max {AnalyticsConstants.MAX_REPORT_PERIOD_DAYS})" }
            })
            .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.PERIOD_TOO_LONG, AnalyticsConstants.MAX_REPORT_PERIOD_DAYS));

        RuleFor(x => x)
            .Must(x => (x.EndDate - x.StartDate).TotalDays >= AnalyticsConstants.MIN_REPORT_PERIOD_DAYS)
            .WithErrorCode(((int)ErrorCodes.InvalidReportPeriod).ToString())
            .WithState(x => (object)new Dictionary<string, string>
            {
                { "period", $"{(int)(x.EndDate - x.StartDate).TotalDays} days (min {AnalyticsConstants.MIN_REPORT_PERIOD_DAYS})" }
            })
            .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.PERIOD_TOO_SHORT, AnalyticsConstants.MIN_REPORT_PERIOD_DAYS));

        RuleFor(x => x.MaxProducts)
            .GreaterThan(0)
            .WithMessage(AnalyticsConstants.ValidationMessages.MAX_PRODUCTS_MINIMUM)
            .LessThanOrEqualTo(AnalyticsConstants.ABSOLUTE_MAX_PRODUCTS)
            .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.MAX_PRODUCTS_EXCEEDED, AnalyticsConstants.ABSOLUTE_MAX_PRODUCTS));

        // ProductFilter and CategoryFilter are optional, so no validation needed
    }
}