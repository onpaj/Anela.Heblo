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
}
