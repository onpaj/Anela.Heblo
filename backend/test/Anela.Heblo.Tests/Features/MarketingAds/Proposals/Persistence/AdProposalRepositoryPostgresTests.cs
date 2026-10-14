using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence.MarketingAds;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Persistence;

/// <summary>The in-flight count uses correlated subqueries (current version, recorded executions); pin its SQL on Postgres.</summary>
[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdProposalRepositoryPostgresTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public AdProposalRepositoryPostgresTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() =>
        _connectionString = await MarketingAdsProposalsPostgres.CreateMigratedAsync(_fixture, "marketing_ads_c3_repo");

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(true, 6)]
    [InlineData(false, 3)]
    public async Task CountUnrecordedActionsOfOtherProposals_TranslatesAndCountsOnPostgres(bool includeApproved, int expected)
    {
        int selfId;
        await using (var seed = MarketingAdsProposalsPostgres.NewContext(_connectionString))
        {
            selfId = await AdProposalRepositoryTests.SeedInFlightPausesAsync(new AdProposalRepository(seed));
        }
        var statuses = includeApproved
            ? new[] { AdProposalStatus.Approved, AdProposalStatus.Executing }
            : new[] { AdProposalStatus.Executing };

        await using var verify = MarketingAdsProposalsPostgres.NewContext(_connectionString);
        var count = await new AdProposalRepository(verify)
            .CountUnrecordedActionsOfOtherProposalsAsync("GoogleAds", "acc-1", "PauseAd", selfId, statuses, CancellationToken.None);

        count.Should().Be(expected);
    }
}
