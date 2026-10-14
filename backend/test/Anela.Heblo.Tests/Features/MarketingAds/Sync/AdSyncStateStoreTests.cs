using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdSyncStateStoreTests
{
    [Fact]
    public async Task Failure_after_success_keeps_watermark_and_last_success()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var watermark = AdsTestData.Now.AddHours(-1);
        await harness.State.RecordSuccessAsync(AdPlatform.Sklik, "acc", AdSyncStreams.ChangeEvents, AdsTestData.Now, watermark, default);

        // Act
        await harness.State.RecordFailureAsync(AdPlatform.Sklik, "acc", AdSyncStreams.ChangeEvents, "boom", default);

        // Assert — through a second context
        await using var verify = harness.Verify();
        var row = await verify.SyncStates.SingleAsync();
        row.Status.Should().Be(AdsDbValues.StatusFailed);
        row.LastError.Should().Be("boom");
        row.Watermark.Should().Be(watermark);
        row.LastSuccessAt.Should().Be(AdsTestData.Now);
    }

    [Fact]
    public async Task Success_without_watermark_keeps_the_previous_one_and_clears_the_error()
    {
        var harness = new AdsSyncHarness();
        var watermark = AdsTestData.Now.AddDays(-1);
        await harness.State.RecordSuccessAsync(AdPlatform.Sklik, "acc", AdSyncStreams.DailyFacts, AdsTestData.Now, watermark, default);
        await harness.State.RecordFailureAsync(AdPlatform.Sklik, "acc", AdSyncStreams.DailyFacts, "boom", default);

        await harness.State.RecordSuccessAsync(AdPlatform.Sklik, "acc", AdSyncStreams.DailyFacts, AdsTestData.Now, null, default);

        var state = await harness.State.GetAsync(AdPlatform.Sklik, "acc", AdSyncStreams.DailyFacts, default);
        state!.Watermark.Should().Be(watermark);
        state.LastError.Should().BeNull();
        state.Status.Should().Be(AdsDbValues.StatusSucceeded);
    }

    [Fact]
    public async Task Long_errors_are_truncated()
    {
        var harness = new AdsSyncHarness();

        await harness.State.RecordFailureAsync(AdPlatform.MetaAds, "acc", AdSyncStreams.Entities, new string('x', 5000), default);

        await using var verify = harness.Verify();
        (await verify.SyncStates.SingleAsync()).LastError!.Length.Should().Be(AdSyncStateStore.MaxErrorLength);
    }

    [Fact]
    public async Task Missing_state_is_null()
    {
        (await new AdsSyncHarness().State.GetAsync(AdPlatform.MetaAds, "acc", AdSyncStreams.Entities, default)).Should().BeNull();
    }
}
