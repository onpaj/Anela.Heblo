using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdStreamRunnerTests
{
    [Fact]
    public async Task A_stream_failing_at_save_time_does_not_poison_the_next_stream_on_the_shared_context()
    {
        // Arrange - a stored account, then a work delegate that tracks a duplicate-key account and fails on SaveChanges
        var harness = new AdsSyncHarness();
        var (accountId, _) = await harness.SeedTreeAsync();
        var runner = harness.Runner;

        // Act
        var failed = await runner.RunAsync(AdPlatform.GoogleAds, AdsTestData.AccountId, AdSyncStreams.DailyFacts, async token =>
        {
            harness.Db.Accounts.Add(new AdAccount
            {
                Id = accountId,
                Platform = "GoogleAds",
                ExternalId = "duplicate",
                Name = "dup",
                Currency = "CZK",
            });
            await harness.Db.SaveChangesAsync(token);
            return new AdStreamWork(1, null);
        }, default);
        var next = await runner.RunAsync(AdPlatform.GoogleAds, AdsTestData.AccountId, AdSyncStreams.SearchTerms, async token =>
        {
            var counts = await harness.SearchTerms.ReplaceDayAsync(accountId, AdsTestData.Yesterday,
                new[] { AdsTestData.Term("g-1", AdsTestData.Yesterday, "kosmetika", 10m) },
                (await harness.Entities.LoadIdsAsync(accountId, token)), token);
            return new AdStreamWork(counts.Written, null);
        }, default);

        // Assert
        failed.Succeeded.Should().BeFalse();
        next.Succeeded.Should().BeTrue();
        await using var verify = harness.Verify();
        (await verify.SearchTermsDaily.CountAsync()).Should().Be(1);
        var states = await verify.SyncStates.ToDictionaryAsync(s => s.Stream);
        states[AdSyncStreams.DailyFacts].Status.Should().Be(AdsDbValues.StatusFailed);
        states[AdSyncStreams.SearchTerms].Status.Should().Be(AdsDbValues.StatusSucceeded);
    }

    [Fact]
    public async Task Cancellation_is_rethrown_not_recorded_as_a_stream_failure()
    {
        var harness = new AdsSyncHarness();
        using var cts = new CancellationTokenSource();

        var act = () => harness.Runner.RunAsync(AdPlatform.GoogleAds, AdsTestData.AccountId, AdSyncStreams.Entities, token =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new AdStreamWork(0, null));
        }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
