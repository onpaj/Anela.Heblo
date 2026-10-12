using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdsSyncOptionsTests
{
    [Fact]
    public void Defaults_match_the_spec()
    {
        // Act
        var options = new AdsSyncOptions();

        // Assert
        options.FactLookbackDays.Should().Be(14);
        options.OutOfBandMatchWindowMinutes.Should().Be(120);
        options.DailySync.CronExpression.Should().Be("30 5 * * *");
        options.DailySync.TimeZone.Should().Be("Europe/Prague");
        options.ChangeSync.CronExpression.Should().Be("15 * * * *");
        options.ChangeSync.InitialLookbackDays.Should().Be(7);
    }

    [Fact]
    public void Managed_account_keys_are_trimmed_and_deduplicated()
    {
        // Arrange — the configuration binder merges arrays index-wise, so duplicates are realistic.
        var options = new AdsSyncOptions { ManagedAccounts = new[] { "GoogleAds:123", " GoogleAds:123 ", "", "Sklik:9" } };

        // Act
        var keys = options.ManagedAccountKeys();

        // Assert
        keys.Should().BeEquivalentTo(new[] { "GoogleAds:123", "Sklik:9" });
    }

    [Theory]
    [InlineData(-1, 500)]
    [InlineData(31, 500)]
    [InlineData(14, 0)]
    public void EnsureValid_rejects_out_of_range_values(int lookback, int batchSize)
    {
        // Arrange
        var options = new AdsSyncOptions { FactLookbackDays = lookback, BatchSize = batchSize };

        // Act
        var act = options.EnsureValid;

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Ads*");
    }

    [Theory]
    [InlineData(0, 45)]
    [InlineData(-5, 45)]
    [InlineData(45, 0)]
    [InlineData(45, -1)]
    public void EnsureValid_rejects_non_positive_run_timeouts(int dailyMinutes, int changeMinutes)
    {
        // Arrange
        var options = new AdsSyncOptions
        {
            DailySync = new AdsDailySyncOptions { RunTimeoutMinutes = dailyMinutes },
            ChangeSync = new AdsChangeSyncOptions { RunTimeoutMinutes = changeMinutes },
        };

        // Act
        var act = options.EnsureValid;

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*RunTimeoutMinutes*");
    }
}
