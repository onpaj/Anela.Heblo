using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;
using Anela.Heblo.Application.Shared;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Analytics.Validators;

public class GetProductMarginSummaryRequestValidator : AbstractValidator<GetProductMarginSummaryRequest>
{
    public GetProductMarginSummaryRequestValidator()
    {
        RuleFor(x => x.TimeWindow)
            .Must(v => TimeWindowParser.SupportedTimeWindows.Contains(v))
            .WithErrorCode(((int)ErrorCodes.InvalidTimeWindow).ToString())
            .WithState(x => (object)new Dictionary<string, string>
            {
                { "timeWindow", x.TimeWindow }
            })
            .WithMessage(x => string.Format(
                AnalyticsConstants.ValidationMessages.INVALID_TIME_WINDOW,
                x.TimeWindow,
                string.Join(", ", TimeWindowParser.SupportedTimeWindows)));
    }
}
