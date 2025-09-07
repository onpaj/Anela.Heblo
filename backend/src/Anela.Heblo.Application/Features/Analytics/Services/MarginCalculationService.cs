using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IMarginCalculationService
{
    ServiceMarginCalculationResult CalculateProductMargins(AnalyticsProduct product, DateTime startDate, DateTime endDate);
    decimal CalculateMarginPercentage(decimal margin, decimal revenue);
}

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

public class ServiceMarginCalculationResult
{
    public bool IsSuccess { get; private set; }
    public MarginData? Data { get; private set; }
    public ErrorCodes? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    private ServiceMarginCalculationResult(bool isSuccess, MarginData? data, ErrorCodes? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Data = data;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static ServiceMarginCalculationResult Success(MarginData data)
    {
        return new ServiceMarginCalculationResult(true, data, null, null);
    }

    public static ServiceMarginCalculationResult Failure(ErrorCodes errorCode, string? errorMessage = null)
    {
        return new ServiceMarginCalculationResult(false, null, errorCode, errorMessage);
    }
}

public class MarginData
{
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public decimal Margin { get; set; }
    public decimal MarginPercentage { get; set; }
    public int UnitsSold { get; set; }
}