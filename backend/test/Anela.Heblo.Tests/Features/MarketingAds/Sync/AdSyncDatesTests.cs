using Anela.Heblo.Application.Features.MarketingAds.Sync;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdSyncDatesTests
{
    [Fact]
    public void Yesterday_is_computed_in_the_account_time_zone()
    {
        // Arrange — 22:30 UTC on 7 Oct is 00:30 on 8 Oct in Prague but 18:30 on 7 Oct in New York.
        var now = new DateTimeOffset(2026, 10, 7, 22, 30, 0, TimeSpan.Zero);

        // Act + Assert
        AdSyncDates.Yesterday(now, "Europe/Prague").Should().Be(new DateOnly(2026, 10, 7));
        AdSyncDates.Yesterday(now, "America/New_York").Should().Be(new DateOnly(2026, 10, 6));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void Unknown_time_zone_falls_back_to_Prague(string? zone)
    {
        var now = new DateTimeOffset(2026, 10, 7, 22, 30, 0, TimeSpan.Zero);
        AdSyncDates.Yesterday(now, zone).Should().Be(new DateOnly(2026, 10, 7));
    }

    [Fact]
    public void Fact_window_is_yesterday_plus_lookback_oldest_first()
    {
        // Act
        var window = AdSyncDates.FactWindow(new DateOnly(2026, 10, 6), 14);

        // Assert
        window.Should().HaveCount(15);
        window[0].Should().Be(new DateOnly(2026, 9, 22));
        window[^1].Should().Be(new DateOnly(2026, 10, 6));
    }

    [Fact]
    public void Zero_lookback_is_just_yesterday()
    {
        AdSyncDates.FactWindow(new DateOnly(2026, 10, 6), 0).Should().Equal(new DateOnly(2026, 10, 6));
    }
}
