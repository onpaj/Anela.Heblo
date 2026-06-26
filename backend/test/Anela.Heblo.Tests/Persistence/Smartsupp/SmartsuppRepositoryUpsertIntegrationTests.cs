using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Smartsupp;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Smartsupp;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class SmartsuppRepositoryUpsertIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private ApplicationDbContext _context = null!;

    public SmartsuppRepositoryUpsertIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("smartsupp_upsert_x");

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();

        // Create only the tables needed by these tests.
        // Do NOT use EnsureCreatedAsync — the project schema depends on the "vector" extension
        // which is not available in the plain postgres:16 image.
        cmd.CommandText = """
            CREATE SCHEMA IF NOT EXISTS public;

            CREATE TABLE IF NOT EXISTS public."SmartsuppContacts" (
                "Id"             character varying(100)       PRIMARY KEY,
                "Email"          character varying(200),
                "Name"           character varying(200),
                "Phone"          character varying(50),
                "Note"           text,
                "BannedAt"       timestamp without time zone,
                "BannedBy"       character varying(200),
                "GdprApproved"   boolean                      NOT NULL DEFAULT false,
                "TagsJson"       text,
                "PropertiesJson" text,
                "CreatedAt"      timestamp without time zone  NOT NULL,
                "UpdatedAt"      timestamp without time zone  NOT NULL,
                "SyncedAt"       timestamp without time zone  NOT NULL
            );

            CREATE INDEX IF NOT EXISTS "IX_SmartsuppContacts_Email"
                ON public."SmartsuppContacts" ("Email");

            CREATE TABLE IF NOT EXISTS public."SmartsuppConversations" (
                "Id"                     character varying(100)       PRIMARY KEY,
                "ExtId"                  character varying(100),
                "Subject"                text,
                "ContactId"              character varying(100)        REFERENCES public."SmartsuppContacts" ("Id") ON DELETE SET NULL,
                "ContactName"            character varying(200),
                "ContactEmail"           character varying(200),
                "ContactAvatarUrl"       text,
                "VisitorId"              character varying(100),
                "Status"                 character varying(20)         NOT NULL,
                "IsUnread"               boolean                       NOT NULL DEFAULT false,
                "IsOffline"              boolean                       NOT NULL DEFAULT false,
                "IsServed"               boolean                       NOT NULL DEFAULT false,
                "FinishedAt"             timestamp without time zone,
                "Domain"                 character varying(200),
                "Referer"                text,
                "LocationCountry"        character varying(100),
                "LocationCity"           character varying(100),
                "LocationIp"             character varying(50),
                "LocationCode"           character varying(10),
                "VariablesJson"          text,
                "TagsJson"               text,
                "LastMessageAt"          timestamp without time zone,
                "LastMessagePreview"     text,
                "CreatedAt"              timestamp without time zone   NOT NULL,
                "UpdatedAt"              timestamp without time zone   NOT NULL,
                "SyncedAt"               timestamp without time zone   NOT NULL,
                "Rating"                 integer,
                "RatingText"             character varying(1000),
                "CloseType"              character varying(50),
                "ClosedByAgentId"        character varying(100),
                "AssignedAgentIdsJson"   text,
                "Channel"                character varying(50),
                "LastClosedAt"           timestamp without time zone,
                "VisitorUserAgent"       text,
                "VisitorOs"              character varying(100),
                "VisitorBrowser"         character varying(100),
                "VisitorBrowserVersion"  character varying(100),
                "VisitorVisitsCount"     integer,
                "VisitorInfoFetchedAt"   timestamp without time zone
            );

            CREATE INDEX IF NOT EXISTS "IX_SmartsuppConversations_Status_LastMessageAt"
                ON public."SmartsuppConversations" ("Status", "LastMessageAt");

            CREATE INDEX IF NOT EXISTS "IX_SmartsuppConversations_ContactId"
                ON public."SmartsuppConversations" ("ContactId");
            """;

        await cmd.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        _context = new ApplicationDbContext(options);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private SmartsuppRepository CreateRepository(ApplicationDbContext? context = null)
    {
        var apiClient = Substitute.For<ISmartsuppApiClient>();
        return new SmartsuppRepository(
            context ?? _context,
            apiClient,
            NullLogger<SmartsuppRepository>.Instance);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper: read a contact row directly
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<(string? Email, DateTime UpdatedAt)> ReadContactAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Email", "UpdatedAt"
            FROM public."SmartsuppContacts"
            WHERE "Id" = @id
            """;
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Contact '{id}' not found.");
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetDateTime(1));
    }

    private async Task<int> CountContactsAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT COUNT(*) FROM public."SmartsuppContacts" WHERE "Id" = @id""";
        cmd.Parameters.AddWithValue("id", id);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper: read a conversation row directly
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<(string Status, DateTime UpdatedAt, string? ContactName)> ReadConversationAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Status", "UpdatedAt", "ContactName"
            FROM public."SmartsuppConversations"
            WHERE "Id" = @id
            """;
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Conversation '{id}' not found.");
        return (reader.GetString(0), reader.GetDateTime(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task<int> CountConversationsAsync(string id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT COUNT(*) FROM public."SmartsuppConversations" WHERE "Id" = @id""";
        cmd.Parameters.AddWithValue("id", id);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Factory helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static SmartsuppContact MakeContact(string id, string email, DateTime updatedAt) =>
        new()
        {
            Id = id,
            Email = email,
            Name = "Test User",
            GdprApproved = false,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Unspecified),
            SyncedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified),
        };

    private static SmartsuppConversation MakeConversation(
        string id,
        SmartsuppConversationStatus status,
        DateTime updatedAt,
        string? contactName = "Test Contact") =>
        new()
        {
            Id = id,
            Status = status,
            ContactName = contactName,
            IsUnread = false,
            IsOffline = false,
            IsServed = false,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Unspecified),
            SyncedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified),
        };

    // ──────────────────────────────────────────────────────────────────────────
    // Contact tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertContactAsync_NewerEvent_UpdatesEmailAndUpdatedAt()
    {
        // Arrange
        var id = "contact-newer-1";
        var t1 = new DateTime(2026, 6, 1, 10, 0, 0);
        var t2 = new DateTime(2026, 6, 1, 11, 0, 0);
        var repo = CreateRepository();

        await repo.UpsertContactAsync(MakeContact(id, "old@example.com", t1), CancellationToken.None);

        // Act — upsert with newer timestamp and different email
        await repo.UpsertContactAsync(MakeContact(id, "new@example.com", t2), CancellationToken.None);

        // Assert
        var row = await ReadContactAsync(id);
        row.Email.Should().Be("new@example.com", "the newer event must overwrite the stored email");
        row.UpdatedAt.Should().Be(t2, "UpdatedAt must reflect the newer event");
    }

    [Fact]
    public async Task UpsertContactAsync_OlderEvent_DoesNotOverwriteStoredRow()
    {
        // Arrange
        var id = "contact-older-1";
        var t1 = new DateTime(2026, 6, 1, 11, 0, 0);
        var t2 = new DateTime(2026, 6, 1, 10, 0, 0); // older
        var repo = CreateRepository();

        await repo.UpsertContactAsync(MakeContact(id, "current@example.com", t1), CancellationToken.None);

        // Act — upsert with an older timestamp must be a no-op
        await repo.UpsertContactAsync(MakeContact(id, "stale@example.com", t2), CancellationToken.None);

        // Assert
        var row = await ReadContactAsync(id);
        row.Email.Should().Be("current@example.com", "the older event must not overwrite the stored row");
        row.UpdatedAt.Should().Be(t1, "UpdatedAt must not regress");
    }

    [Fact]
    public async Task UpsertContactAsync_ConcurrentInserts_AllSucceedAndProduceSingleRow()
    {
        // Arrange
        const int concurrency = 5;
        var id = "contact-race-1";
        var updatedAt = new DateTime(2026, 6, 1, 12, 0, 0);

        // Each concurrent task gets its own DbContext + connection to actually race on the PK.
        var contexts = new List<ApplicationDbContext>();
        try
        {
            var tasks = Enumerable.Range(0, concurrency).Select(i =>
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(_connectionString)
                    .Options;
                var ctx = new ApplicationDbContext(options);
                contexts.Add(ctx);
                var repo = CreateRepository(ctx);
                var contact = MakeContact(id, $"racer{i}@example.com", updatedAt);
                return repo.UpsertContactAsync(contact, CancellationToken.None);
            }).ToArray();

            // Act
            var act = async () => await Task.WhenAll(tasks);

            // Assert — no exception, exactly one row
            await act.Should().NotThrowAsync("concurrent upserts must be handled atomically");
            (await CountContactsAsync(id)).Should().Be(1, "exactly one row must survive");
        }
        finally
        {
            foreach (var ctx in contexts)
                await ctx.DisposeAsync();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Conversation tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertConversationAsync_NewerEvent_UpdatesStoredRow()
    {
        // Arrange
        var id = "conv-newer-1";
        var t1 = new DateTime(2026, 6, 1, 10, 0, 0);
        var t2 = new DateTime(2026, 6, 1, 11, 0, 0);
        var repo = CreateRepository();

        await repo.UpsertConversationAsync(
            MakeConversation(id, SmartsuppConversationStatus.Open, t1), CancellationToken.None);

        // Act — upsert with newer timestamp and different status
        await repo.UpsertConversationAsync(
            MakeConversation(id, SmartsuppConversationStatus.Resolved, t2), CancellationToken.None);

        // Assert
        var row = await ReadConversationAsync(id);
        row.Status.Should().Be("Resolved", "the newer event must update the status");
        row.UpdatedAt.Should().Be(t2, "UpdatedAt must reflect the newer event");
    }

    [Fact]
    public async Task UpsertConversationAsync_OlderEvent_DoesNotOverwriteStoredRow()
    {
        // Arrange
        var id = "conv-older-1";
        var t1 = new DateTime(2026, 6, 1, 11, 0, 0);
        var t2 = new DateTime(2026, 6, 1, 10, 0, 0); // older
        var repo = CreateRepository();

        await repo.UpsertConversationAsync(
            MakeConversation(id, SmartsuppConversationStatus.Resolved, t1), CancellationToken.None);

        // Act — out-of-order event must be ignored
        await repo.UpsertConversationAsync(
            MakeConversation(id, SmartsuppConversationStatus.Open, t2), CancellationToken.None);

        // Assert
        var row = await ReadConversationAsync(id);
        row.Status.Should().Be("Resolved", "the older event must not revert the status");
        row.UpdatedAt.Should().Be(t1, "UpdatedAt must not regress");
    }

    [Fact]
    public async Task UpsertConversationAsync_NullContactName_DoesNotOverwriteStoredNonNullValue()
    {
        // Arrange
        var id = "conv-coalesce-1";
        var t1 = new DateTime(2026, 6, 1, 10, 0, 0);
        var t2 = new DateTime(2026, 6, 1, 11, 0, 0); // newer
        var repo = CreateRepository();

        // Seed with a known ContactName
        await repo.UpsertConversationAsync(
            MakeConversation(id, SmartsuppConversationStatus.Open, t1, contactName: "Jana"),
            CancellationToken.None);

        // Act — upsert with null ContactName but newer timestamp
        await repo.UpsertConversationAsync(
            MakeConversation(id, SmartsuppConversationStatus.Open, t2, contactName: null),
            CancellationToken.None);

        // Assert — COALESCE preserves the existing non-null ContactName
        var row = await ReadConversationAsync(id);
        row.ContactName.Should().Be("Jana",
            "COALESCE must keep the stored ContactName when the incoming event carries null");
        row.UpdatedAt.Should().Be(t2, "UpdatedAt must still reflect the newer event");
    }
}
