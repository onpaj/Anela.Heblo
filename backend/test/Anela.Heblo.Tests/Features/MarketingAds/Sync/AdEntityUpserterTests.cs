using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdEntityUpserterTests
{
    private static async Task<(AdsSyncHarness Harness, long AccountId)> ArrangeAsync()
    {
        var harness = new AdsSyncHarness();
        var account = (await harness.Accounts.UpsertAsync(AdPlatform.GoogleAds, new[] { AdsTestData.Account() }, default)).Single();
        return (harness, account.Id);
    }

    private static readonly AdEntitySnapshot[] Tree =
    {
        AdsTestData.Ad("ad-1", "g-1"),          // child before parent on purpose
        AdsTestData.AdGroup("g-1", "c-1"),
        AdsTestData.Campaign("c-1"),
    };

    [Fact]
    public async Task Inserts_entities_and_resolves_parents_regardless_of_order()
    {
        var (harness, accountId) = await ArrangeAsync();

        var result = await harness.Entities.UpsertAsync(accountId, Tree, reportCreations: false, default);

        result.EntityIds.Should().HaveCount(3);
        result.Changes.Should().BeEmpty();
        await using var verify = harness.Verify();
        var rows = await verify.Entities.ToListAsync();
        var campaign = rows.Single(e => e.ExternalId == "c-1");
        var adGroup = rows.Single(e => e.ExternalId == "g-1");
        adGroup.ParentId.Should().Be(campaign.Id);
        rows.Single(e => e.ExternalId == "ad-1").ParentId.Should().Be(adGroup.Id);
        campaign.Level.Should().Be("Campaign");
    }

    [Fact]
    public async Task Identical_second_run_writes_nothing_and_reports_nothing()
    {
        var (harness, accountId) = await ArrangeAsync();
        await harness.Entities.UpsertAsync(accountId, Tree, false, default);

        var second = await harness.Entities.UpsertAsync(accountId, Tree, reportCreations: true, default);

        second.RowsWritten.Should().Be(0);
        second.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_change_updates_the_row_and_is_reported()
    {
        var (harness, accountId) = await ArrangeAsync();
        await harness.Entities.UpsertAsync(accountId, Tree, false, default);
        harness.Time.Advance(TimeSpan.FromHours(1));

        var result = await harness.Entities.UpsertAsync(accountId,
            new[] { AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused), Tree[1], Tree[2] }, false, default);

        var change = result.Changes.Should().ContainSingle().Subject;
        change.Key.Should().Be(new AdEntityKey(AdEntityLevel.Ad, "ad-1"));
        change.Change.ChangeType.Should().Be(AdSnapshotDiffer.StatusChange);
        await using var verify = harness.Verify();
        var row = await verify.Entities.SingleAsync(e => e.ExternalId == "ad-1");
        row.Status.Should().Be("Paused");
        row.UpdatedAt.Should().Be(harness.Time.GetUtcNow());
    }

    [Fact]
    public async Task Creations_are_reported_only_when_asked()
    {
        var (harness, accountId) = await ArrangeAsync();
        await harness.Entities.UpsertAsync(accountId, new[] { Tree[2] }, false, default);

        var result = await harness.Entities.UpsertAsync(accountId, Tree, reportCreations: true, default);

        result.Changes.Select(c => (c.Key.ExternalId, c.Change.ChangeType))
            .Should().BeEquivalentTo(new[] { ("ad-1", "Created"), ("g-1", "Created") });
        result.Changes.Should().OnlyContain(c => c.EntityId > 0);
    }

    [Fact]
    public async Task Stored_attributes_with_different_formatting_are_not_a_change()
    {
        // Arrange — write the row the way jsonb hands it back
        var (harness, accountId) = await ArrangeAsync();
        await using (var seed = harness.Verify())
        {
            seed.Entities.Add(new AdEntity
            {
                AccountId = accountId, Level = "Campaign", ExternalId = "c-9", Name = "Campaign c-9", Status = "Enabled",
                AttributesJson = "{\"bidding\": \"MaxConversions\", \"budget\": \"500\"}",
                FirstSeenAt = AdsTestData.Now, LastSeenAt = AdsTestData.Now, UpdatedAt = AdsTestData.Now,
            });
            await seed.SaveChangesAsync();
        }
        var snapshot = AdsTestData.Campaign("c-9", attributes: new Dictionary<string, string?> { ["budget"] = "500", ["bidding"] = "MaxConversions" });

        // Act
        var result = await harness.Entities.UpsertAsync(accountId, new[] { snapshot }, true, default);

        // Assert
        result.Changes.Should().BeEmpty();
        result.RowsWritten.Should().Be(0);
    }

    [Fact]
    public async Task LoadIds_returns_every_entity_of_the_account_only()
    {
        var (harness, accountId) = await ArrangeAsync();
        await harness.Entities.UpsertAsync(accountId, Tree, false, default);

        var ids = await harness.Entities.LoadIdsAsync(accountId, default);
        var other = await harness.Entities.LoadIdsAsync(accountId + 1000, default);

        ids.Keys.Should().Contain(new AdEntityKey(AdEntityLevel.AdGroup, "g-1"));
        ids.Should().HaveCount(3);
        other.Should().BeEmpty();
    }
}
