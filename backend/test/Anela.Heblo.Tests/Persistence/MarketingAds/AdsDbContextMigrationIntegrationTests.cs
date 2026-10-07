using System.Text.Json;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Tests.Persistence.MarketingAds;

/// <summary>
/// Runs the real InitialAdsSchema migration against Postgres: schema placement, the history-table
/// pin, jsonb, numeric precision, unique natural keys and timestamptz handling are all invisible to
/// the InMemory provider.
/// </summary>
[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdsDbContextMigrationIntegrationTests : IAsyncLifetime
{
    private static readonly DateOnly FactDate = new(2026, 10, 6);
    private static readonly DateTimeOffset SyncedAt = new(2026, 10, 7, 3, 30, 0, TimeSpan.Zero);

    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public AdsDbContextMigrationIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("ads");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AdsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AdsDbContext>()
            .UseNpgsql(_connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(AdsDbContext.MigrationsHistoryTableName, AdsDbContext.SchemaName))
            .Options);

    [Fact]
    public async Task Migrate_creates_the_six_tables_and_the_history_table_inside_the_ads_schema()
    {
        // Act
        var adsTables = await ReadTableNamesAsync("ads");
        var publicTables = await ReadTableNamesAsync("public");

        // Assert
        adsTables.Should().BeEquivalentTo(
            "__EFMigrationsHistory", "ad_accounts", "ad_entities", "ad_daily_facts",
            "ad_search_term_daily", "ad_change_events", "sync_state");
        publicTables.Should().NotContain("__EFMigrationsHistory");
    }

    [Fact]
    public async Task Rows_written_by_one_context_read_back_identically_through_a_second_context()
    {
        // Arrange
        long campaignId;
        long adGroupId;
        await using (var write = NewContext())
        {
            var account = NewAccount("GoogleAds", "123-456-7890");
            write.Accounts.Add(account);
            await write.SaveChangesAsync();

            var campaign = NewEntity(account.Id, "Campaign", "campaign-1", parentId: null,
                attributesJson: "{\"dailyBudget\": \"500\", \"biddingStrategy\": \"MaximizeConversions\"}");
            write.Entities.Add(campaign);
            await write.SaveChangesAsync();

            var adGroup = NewEntity(account.Id, "AdGroup", "adgroup-1", campaign.Id, "{}");
            write.Entities.Add(adGroup);
            await write.SaveChangesAsync();

            write.DailyFacts.Add(new AdDailyFact
            {
                EntityId = campaign.Id, Date = FactDate, Impressions = 1200, Clicks = 48,
                Cost = 312.5012m, Conversions = 2.5m, ConversionValue = 2150.75m, Currency = "CZK", SyncedAt = SyncedAt,
            });
            write.SearchTermsDaily.Add(new AdSearchTermDaily
            {
                AdGroupEntityId = adGroup.Id, Date = FactDate, SearchTerm = "krém na obličej",
                MatchType = AdSearchTermDaily.UnknownMatchType, Impressions = 300, Clicks = 12,
                Cost = 80.10m, Conversions = 1m, ConversionValue = 640m, Currency = "CZK", SyncedAt = SyncedAt,
            });
            write.ChangeEvents.Add(new AdChangeEvent
            {
                AccountId = account.Id, ExternalEventId = "event-1", OccurredAt = SyncedAt.AddHours(-3),
                Actor = "agency@example.com", ActorKind = "User", EntityId = adGroup.Id, EntityExternalRef = "adgroup-1",
                ChangeType = "StatusChanged", OldValueJson = "{\"status\": \"Enabled\"}", NewValueJson = "{\"status\": \"Paused\"}",
                Source = AdChangeSources.PlatformChangeLog, Origin = AdChangeOrigins.OutOfBand, SyncedAt = SyncedAt,
            });
            write.SyncStates.Add(new AdSyncState
            {
                Platform = "GoogleAds", AccountExternalId = "123-456-7890", Stream = AdSyncStreams.DailyFacts,
                Watermark = SyncedAt, Status = "Succeeded", LastSuccessAt = SyncedAt, UpdatedAt = SyncedAt,
            });
            await write.SaveChangesAsync();
            campaignId = campaign.Id;
            adGroupId = adGroup.Id;
        }

        // Act
        await using var read = NewContext();
        var storedAccount = await read.Accounts.SingleAsync();
        var storedCampaign = await read.Entities.SingleAsync(e => e.ExternalId == "campaign-1");
        var storedAdGroup = await read.Entities.SingleAsync(e => e.ExternalId == "adgroup-1");
        var fact = await read.DailyFacts.SingleAsync();
        var searchTerm = await read.SearchTermsDaily.SingleAsync();
        var change = await read.ChangeEvents.SingleAsync();
        var syncState = await read.SyncStates.SingleAsync();

        // Assert
        storedAccount.IsManaged.Should().BeTrue();
        storedAccount.Currency.Should().Be("CZK");
        storedAdGroup.ParentId.Should().Be(campaignId);
        using (var attributes = JsonDocument.Parse(storedCampaign.AttributesJson))
        {
            attributes.RootElement.GetProperty("dailyBudget").GetString().Should().Be("500");
        }
        fact.EntityId.Should().Be(campaignId);
        fact.Date.Should().Be(FactDate);
        fact.Cost.Should().Be(312.5012m);
        fact.Conversions.Should().Be(2.5m);
        fact.ConversionValue.Should().Be(2150.75m);
        searchTerm.AdGroupEntityId.Should().Be(adGroupId);
        searchTerm.SearchTerm.Should().Be("krém na obličej");
        searchTerm.MatchType.Should().Be(AdSearchTermDaily.UnknownMatchType);
        change.EntityId.Should().Be(adGroupId);
        change.OccurredAt.Should().Be(SyncedAt.AddHours(-3));
        using (var newValue = JsonDocument.Parse(change.NewValueJson!))
        {
            newValue.RootElement.GetProperty("status").GetString().Should().Be("Paused");
        }
        syncState.Watermark.Should().Be(SyncedAt);
        syncState.LastSuccessAt.Should().Be(SyncedAt);
    }

    [Fact]
    public async Task A_second_account_with_the_same_platform_and_external_id_is_rejected()
    {
        // Arrange
        await using (var first = NewContext())
        {
            first.Accounts.Add(NewAccount("Sklik", "sklik-1"));
            await first.SaveChangesAsync();
        }

        // Act — a fresh context, so a failed SaveChanges cannot poison the arrange step's tracker
        await using var second = NewContext();
        second.Accounts.Add(NewAccount("Sklik", "sklik-1"));
        var act = () => second.SaveChangesAsync();

        // Assert
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_second_change_event_with_the_same_account_source_and_external_id_is_rejected()
    {
        // Arrange
        long accountId;
        await using (var first = NewContext())
        {
            var account = NewAccount("MetaAds", "act_1");
            first.Accounts.Add(account);
            await first.SaveChangesAsync();
            first.ChangeEvents.Add(NewChangeEvent(account.Id, "activity-1"));
            await first.SaveChangesAsync();
            accountId = account.Id;
        }

        // Act
        await using var second = NewContext();
        second.ChangeEvents.Add(NewChangeEvent(accountId, "activity-1"));
        var act = () => second.SaveChangesAsync();

        // Assert
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Timestamps_with_a_local_offset_are_stored_as_the_same_instant_in_utc()
    {
        // Arrange — Meta and Sklik report Prague-local offsets; Npgsql alone would refuse these.
        var pragueMorning = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(2));
        await using (var write = NewContext())
        {
            write.SyncStates.Add(new AdSyncState
            {
                Platform = "MetaAds", AccountExternalId = "act_1", Stream = AdSyncStreams.ChangeEvents,
                Watermark = pragueMorning, Status = "Succeeded", LastSuccessAt = pragueMorning, UpdatedAt = pragueMorning,
            });
            await write.SaveChangesAsync();
        }

        // Act
        await using var read = NewContext();
        var state = await read.SyncStates.SingleAsync();

        // Assert
        state.Watermark.Should().Be(pragueMorning);
        state.Watermark!.Value.Offset.Should().Be(TimeSpan.Zero);
        state.LastSuccessAt!.Value.UtcDateTime.Should().Be(new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc));
        state.UpdatedAt.Should().Be(pragueMorning);
    }

    private static AdAccount NewAccount(string platform, string externalId) => new()
    {
        Platform = platform, ExternalId = externalId, Name = "Anela", Currency = "CZK",
        TimeZone = "Europe/Prague", IsManaged = true, CreatedAt = SyncedAt, UpdatedAt = SyncedAt,
    };

    private static AdEntity NewEntity(long accountId, string level, string externalId, long? parentId, string attributesJson) => new()
    {
        AccountId = accountId, Level = level, ExternalId = externalId, ParentId = parentId, Name = externalId,
        Status = "Enabled", AttributesJson = attributesJson, FirstSeenAt = SyncedAt, LastSeenAt = SyncedAt, UpdatedAt = SyncedAt,
    };

    private static AdChangeEvent NewChangeEvent(long accountId, string externalEventId) => new()
    {
        AccountId = accountId, ExternalEventId = externalEventId, OccurredAt = SyncedAt, ActorKind = "Unknown",
        ChangeType = "StatusChanged", Source = AdChangeSources.PlatformChangeLog, Origin = AdChangeOrigins.OutOfBand,
        SyncedAt = SyncedAt,
    };

    private async Task<IReadOnlyList<string>> ReadTableNamesAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema";
        command.Parameters.AddWithValue("schema", schema);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }
}
