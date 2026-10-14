using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Tests.Common;
using Anela.Heblo.Tests.Features.MarketingAds.Integration;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.ReadModel;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdsDbReadModelIntegrationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Seeded = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgresSharedContainerFixture _fixture;
    private AdsDbContext _db = null!;

    public AdsDbReadModelIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_read_model", withShoptet: false);
        _db = new AdsDbContext(AdsPostgres.Options(connectionString));

        var account = await AddAccountAsync(AdPlatform.GoogleAds, "acc-1", isManaged: true);
        await AddAccountAsync(AdPlatform.GoogleAds, "acc-unmanaged", isManaged: false);
        var group = await AddEntityAsync(account, AdEntityLevel.AdGroup, "grp-1", null, "Sestava 1", AdEntityStatus.Enabled);
        await AddEntityAsync(account, AdEntityLevel.Ad, "ad-1", group, "Reklama 1", AdEntityStatus.Enabled);
        await AddEntityAsync(account, AdEntityLevel.Ad, "ad-2", group, "Reklama 2", AdEntityStatus.Enabled);
        await AddEntityAsync(account, AdEntityLevel.Ad, "ad-3", group, "Reklama 3", AdEntityStatus.Paused);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<long> AddAccountAsync(AdPlatform platform, string externalId, bool isManaged)
    {
        var account = new AdAccount
        {
            Platform = platform.ToString(), ExternalId = externalId, Name = externalId, Currency = "CZK",
            TimeZone = "Europe/Prague", IsManaged = isManaged, CreatedAt = Seeded, UpdatedAt = Seeded,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account.Id;
    }

    private async Task<long> AddEntityAsync(long accountId, AdEntityLevel level, string externalId, long? parentId, string name, AdEntityStatus status)
    {
        var entity = new AdEntity
        {
            AccountId = accountId, Level = level.ToString(), ExternalId = externalId, ParentId = parentId, Name = name,
            Status = status.ToString(), FirstSeenAt = Seeded, LastSeenAt = Seeded, UpdatedAt = Seeded,
        };
        _db.Entities.Add(entity);
        await _db.SaveChangesAsync();
        return entity.Id;
    }

    [Theory]
    [InlineData(AdPlatform.GoogleAds, "acc-1", true)]
    [InlineData(AdPlatform.GoogleAds, "acc-unmanaged", false)]
    [InlineData(AdPlatform.GoogleAds, "acc-missing", null)]
    [InlineData(AdPlatform.MetaAds, "acc-1", null)]
    public async Task IsAccountManaged_ReadsIsManagedPerPlatform(AdPlatform platform, string account, bool? expected)
    {
        (await new AdsDbReadModel(_db).IsAccountManagedAsync(platform, account, CancellationToken.None)).Should().Be(expected);
    }

    [Fact]
    public async Task GetAdGroupOccupancy_CountsOnlyEnabledSiblings()
    {
        var result = await new AdsDbReadModel(_db).GetAdGroupOccupancyAsync(AdPlatform.GoogleAds, "acc-1", new[] { "ad-1", "ad-unknown" }, CancellationToken.None);

        result.Should().ContainKey("ad-1").WhoseValue.Should().Be(new AdGroupOccupancy("grp-1", 2));
        result.Should().NotContainKey("ad-unknown");
    }

    [Fact]
    public async Task GetEntityNames_ReturnsNamesOfKnownIds()
    {
        var names = await new AdsDbReadModel(_db).GetEntityNamesAsync(AdPlatform.GoogleAds, "acc-1", new[] { "grp-1", "ad-2", "nope" }, CancellationToken.None);

        names.Should().BeEquivalentTo(new Dictionary<string, string> { ["grp-1"] = "Sestava 1", ["ad-2"] = "Reklama 2" });
    }
}
