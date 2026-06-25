namespace Anela.Heblo.Application.Features.Analytics.Services;

public class TimeWindowParser
{
    private readonly TimeProvider _timeProvider;

    public TimeWindowParser(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)
    {
        var today = _timeProvider.GetLocalNow().Date;

        return timeWindow switch
        {
            "current-year" => (new DateTime(today.Year, 1, 1), today),
            "current-and-previous-year" => (new DateTime(today.Year - 1, 1, 1), today),
            "last-6-months" => (today.AddMonths(-6), today),
            "last-12-months" => (today.AddMonths(-12), today),
            "last-24-months" => (today.AddMonths(-24), today),
            _ => throw new ArgumentException($"Unknown time window value: '{timeWindow}'", nameof(timeWindow))
        };
    }
}
