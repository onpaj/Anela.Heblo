using Anela.Heblo.Adapters.Flexi.Analytics;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class FlexiAnalyticsSyncOptionsTests
{
    [Fact]
    public void GetInitialBackfillDateTime_ReturnsUtcKind()
    {
        var options = new FlexiAnalyticsSyncOptions { InitialBackfillFrom = "2024-01-01" };
        var result = options.GetInitialBackfillDateTime();
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void GetInitialBackfillDateTime_DateComponentMatchesConfiguredDateAsUtcMidnight()
    {
        // Regression for the .Date bug introduced in #3243:
        // .Date on Kind=Utc returns Kind=Unspecified, then .ToUniversalTime() shifts
        // by the local offset (Prague UTC+2 CEST), producing 2019-12-31T23:00:00Z.
        // The correct result is 2020-01-01T00:00:00Z.
        var options = new FlexiAnalyticsSyncOptions { InitialBackfillFrom = "2020-01-01" };
        var result = options.GetInitialBackfillDateTime();
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), result);
    }
}
