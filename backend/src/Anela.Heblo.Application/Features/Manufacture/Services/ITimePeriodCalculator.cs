using Anela.Heblo.Application.Features.Manufacture.Model;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface ITimePeriodCalculator
{
    (DateTime fromDate, DateTime toDate) CalculateTimePeriod(
        TimePeriodFilter timePeriod,
        DateTime? customFromDate = null,
        DateTime? customToDate = null);
}