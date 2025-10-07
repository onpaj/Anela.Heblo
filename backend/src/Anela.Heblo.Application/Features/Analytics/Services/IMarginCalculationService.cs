using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IMarginCalculationService
{
    ServiceMarginCalculationResult CalculateProductMargins(AnalyticsProduct product, DateTime startDate, DateTime endDate);
    decimal CalculateMarginPercentage(decimal margin, decimal revenue);
}