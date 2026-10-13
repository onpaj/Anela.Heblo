using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal static class ReadSourceHarness
{
    public static readonly DateOnly FixtureDate = new(2026, 10, 6);

    public static (GoogleAdsReadSource Source, FakeTimeProvider Time) Create(
        IGoogleAdsApiClient api, Action<GoogleAdsSettings>? configure = null, ILogger<GoogleAdsReadSource>? logger = null)
    {
        var time = new FakeTimeProvider(TestSettings.Now);
        var source = new GoogleAdsReadSource(
            api,
            new TestOptionsMonitor<GoogleAdsSettings>(TestSettings.Create(configure)),
            time,
            logger ?? NullLogger<GoogleAdsReadSource>.Instance);
        return (source, time);
    }
}
