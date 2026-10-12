using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdAccountUpserterTests
{
    [Fact]
    public async Task New_accounts_are_managed_when_no_list_is_configured()
    {
        var harness = new AdsSyncHarness();

        var rows = await harness.Accounts.UpsertAsync(AdPlatform.GoogleAds, new[] { AdsTestData.Account("1"), AdsTestData.Account("1") }, default);

        rows.Should().ContainSingle().Which.IsManaged.Should().BeTrue();
        await using var verify = harness.Verify();
        (await verify.Accounts.SingleAsync()).Platform.Should().Be("GoogleAds");
    }

    [Fact]
    public async Task Configured_list_decides_management_of_new_accounts()
    {
        var harness = new AdsSyncHarness(new AdsSyncOptions { ManagedAccounts = new[] { "GoogleAds:1" } });

        var rows = await harness.Accounts.UpsertAsync(AdPlatform.GoogleAds, new[] { AdsTestData.Account("1"), AdsTestData.Account("2") }, default);

        rows.Single(a => a.ExternalId == "1").IsManaged.Should().BeTrue();
        rows.Single(a => a.ExternalId == "2").IsManaged.Should().BeFalse();
    }

    [Fact]
    public async Task Existing_account_keeps_its_management_flag_and_gets_new_name()
    {
        // Arrange — an admin unmanaged the account in the database
        var harness = new AdsSyncHarness();
        await harness.Accounts.UpsertAsync(AdPlatform.Sklik, new[] { AdsTestData.Account("9") }, default);
        await using (var edit = harness.Verify())
        {
            (await edit.Accounts.SingleAsync()).IsManaged = false;
            await edit.SaveChangesAsync();
        }

        // Act
        await harness.Accounts.UpsertAsync(AdPlatform.Sklik, new[] { AdsTestData.Account("9") with { Name = "Renamed" } }, default);

        // Assert
        await using var verify = harness.Verify();
        var row = await verify.Accounts.SingleAsync();
        row.IsManaged.Should().BeFalse();
        row.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task SyncManaged_isolates_a_failing_source_and_returns_only_managed_accounts()
    {
        var harness = new AdsSyncHarness(new AdsSyncOptions { ManagedAccounts = new[] { "MetaAds:1" } });
        var ok = new ScriptedAdReadSource(AdPlatform.MetaAds) { Accounts = { AdsTestData.Account("1"), AdsTestData.Account("2") } };
        var broken = new ScriptedAdReadSource(AdPlatform.GoogleAds) { FailAccounts = true };

        var good = await harness.Accounts.SyncManagedAsync(ok, default);
        var bad = await harness.Accounts.SyncManagedAsync(broken, default);

        good.Error.Should().BeNull();
        good.Accounts.Select(a => a.ExternalId).Should().Equal("1");
        bad.Accounts.Should().BeEmpty();
        bad.Error.Should().Contain("GoogleAds").And.Contain("accounts unavailable");
    }
}
