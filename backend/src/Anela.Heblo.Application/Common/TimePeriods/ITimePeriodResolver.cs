namespace Anela.Heblo.Application.Common.TimePeriods;

public interface ITimePeriodResolver
{
    IReadOnlyList<DateRange> Resolve(TimePeriod period, DateTime? customFrom = null, DateTime? customTo = null);
}
