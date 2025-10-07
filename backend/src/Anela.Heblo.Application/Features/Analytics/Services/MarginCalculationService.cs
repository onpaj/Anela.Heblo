using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public class MarginCalculationService : IMarginCalculationService
{
    public ServiceMarginCalculationResult CalculateProductMargins(AnalyticsProduct product, DateTime startDate, DateTime endDate)
    {
        try
        {
            var salesInPeriod = product.SalesHistory
                .Where(s => s.Date >= startDate && s.Date <= endDate)
                .ToList();

            var unitsSold = (int)salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
            if (unitsSold <= 0)
            {
                return ServiceMarginCalculationResult.Failure(ErrorCodes.InsufficientData);
            }

            var revenue = (decimal)unitsSold * product.SellingPrice;
            var cost = (decimal)unitsSold * (product.SellingPrice - product.MarginAmount);
            var margin = revenue - cost;
            var marginPercentage = revenue > 0 ? (margin / revenue) * 100 : 0;

            return ServiceMarginCalculationResult.Success(new MarginData
            {
                Revenue = revenue,
                Cost = cost,
                Margin = margin,
                MarginPercentage = marginPercentage,
                UnitsSold = unitsSold
            });
        }
        catch (Exception ex)
        {
            return ServiceMarginCalculationResult.Failure(ErrorCodes.MarginCalculationFailed, ex.Message);
        }
    }

    public decimal CalculateMarginPercentage(decimal margin, decimal revenue)
    {
        return revenue > 0 ? (margin / revenue) * 100 : 0;
    }
}