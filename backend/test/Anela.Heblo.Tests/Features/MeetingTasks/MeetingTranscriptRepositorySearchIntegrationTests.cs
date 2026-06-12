using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MeetingTasks;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class MeetingTranscriptRepositorySearchIntegrationTests : IAsyncLifetime
{
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;
    private ApplicationDbContext _context = null!;
    private MeetingTranscriptRepository _repository = null!;

    public MeetingTranscriptRepositorySearchIntegrationTests(PostgresSharedContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("meeting");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        _context = new ApplicationDbContext(options);

        // Create only the three MeetingTasks tables we exercise.
        // EnsureCreatedAsync would try to install the "vector" extension which is
        // not available in the plain postgres:16 image, causing the whole suite to fail.
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS public."MeetingTranscripts" (
                "Id"              uuid         NOT NULL PRIMARY KEY,
                "PlaudRecordingId" varchar(200) NOT NULL,
                "PlaudCreatedAt"  timestamp    NOT NULL,
                "Subject"         varchar(500) NOT NULL,
                "Summary"         text         NOT NULL,
                "RawTranscript"   text         NOT NULL,
                "Status"          varchar(50)  NOT NULL,
                "ReceivedAt"      timestamp    NOT NULL,
                "ReviewedAt"      timestamp    NULL,
                "ReviewedByUser"  varchar(200) NULL,
                "AccessLevel"     varchar(20)  NOT NULL DEFAULT 'Private'
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "UX_MeetingTranscripts_PlaudRecordingId"
                ON public."MeetingTranscripts" ("PlaudRecordingId");

            CREATE INDEX IF NOT EXISTS "IX_MeetingTranscripts_Status"
                ON public."MeetingTranscripts" ("Status");

            CREATE INDEX IF NOT EXISTS "IX_MeetingTranscripts_AccessLevel"
                ON public."MeetingTranscripts" ("AccessLevel");

            CREATE INDEX IF NOT EXISTS "IX_MeetingTranscripts_ReceivedAt"
                ON public."MeetingTranscripts" ("ReceivedAt");

            CREATE TABLE IF NOT EXISTS public."ProposedTasks" (
                "Id"                  uuid         NOT NULL PRIMARY KEY,
                "MeetingTranscriptId" uuid         NOT NULL REFERENCES public."MeetingTranscripts"("Id") ON DELETE CASCADE,
                "Title"               varchar(500) NOT NULL,
                "Description"         text         NOT NULL,
                "Assignee"            varchar(200) NOT NULL,
                "AssigneeEmail"       varchar(320) NULL,
                "DueDate"             timestamp    NULL,
                "Status"              varchar(50)  NOT NULL,
                "ExternalTaskId"      varchar(200) NULL,
                "IsManuallyAdded"     boolean      NOT NULL DEFAULT false
            );

            CREATE INDEX IF NOT EXISTS "IX_ProposedTasks_MeetingTranscriptId"
                ON public."ProposedTasks" ("MeetingTranscriptId");

            CREATE TABLE IF NOT EXISTS public."MeetingAccessGrants" (
                "Id"                  uuid         NOT NULL PRIMARY KEY,
                "MeetingTranscriptId" uuid         NOT NULL REFERENCES public."MeetingTranscripts"("Id") ON DELETE CASCADE,
                "UserEmail"           varchar(320) NOT NULL,
                "UserDisplayName"     varchar(200) NULL,
                "GrantedAt"           timestamp    NOT NULL,
                "GrantedByUserEmail"  varchar(320) NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "UX_MeetingAccessGrants_TranscriptId_UserEmail"
                ON public."MeetingAccessGrants" ("MeetingTranscriptId", "UserEmail");
            """;
        await cmd.ExecuteNonQueryAsync();

        _repository = new MeetingTranscriptRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private static MeetingTranscript BuildTranscript(
        string plaudId,
        string subject,
        string summary,
        string rawTranscript,
        MeetingTranscriptStatus status = MeetingTranscriptStatus.PendingReview)
    {
        return new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = plaudId,
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = subject,
            Summary = summary,
            RawTranscript = rawTranscript,
            Status = status,
            ReceivedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetListAsync_WithSearchText_ReturnsRecordsMatchingSubject()
    {
        // Arrange
        var hit = BuildTranscript("p1", "Sprint Planning Q2", "Plan for Q2 sprint", "raw content");
        var miss = BuildTranscript("p2", "Weekly Standup", "Standup notes", "raw content");
        _context.MeetingTranscripts.AddRange(hit, miss);
        await _context.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter: null, searchText: "Sprint", searchInTranscript: false,
            isManager: true, userEmail: null, page: 1, pageSize: 10);

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(x => x.PlaudRecordingId == "p1");
    }

    [Fact]
    public async Task GetListAsync_WithSearchText_ReturnsRecordsMatchingSummary()
    {
        // Arrange
        var hit = BuildTranscript("p3", "Q3 Planning", "Budget review for Q3", "some raw text");
        var miss = BuildTranscript("p4", "Daily Sync", "Quick sync about deployment", "some raw text");
        _context.MeetingTranscripts.AddRange(hit, miss);
        await _context.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter: null, searchText: "Budget", searchInTranscript: false,
            isManager: true, userEmail: null, page: 1, pageSize: 10);

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(x => x.PlaudRecordingId == "p3");
    }

    [Fact]
    public async Task GetListAsync_SearchIsCaseInsensitive()
    {
        // Arrange
        var transcript = BuildTranscript("p5", "výroční zpráva", "shrnutí výsledků", "raw");
        _context.MeetingTranscripts.Add(transcript);
        await _context.SaveChangesAsync();

        // Act — search with uppercase
        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter: null, searchText: "VÝROČNÍ", searchInTranscript: false,
            isManager: true, userEmail: null, page: 1, pageSize: 10);

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(x => x.PlaudRecordingId == "p5");
    }

    [Fact]
    public async Task GetListAsync_WithSearchText_DoesNotSearchTranscript_WhenSearchInTranscriptFalse()
    {
        // Arrange — subject and summary do NOT contain "confidential", only raw transcript does
        var transcript = BuildTranscript("p6", "Routine Meeting", "Normal agenda", "CONFIDENTIAL: salary discussion");
        _context.MeetingTranscripts.Add(transcript);
        await _context.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter: null, searchText: "confidential", searchInTranscript: false,
            isManager: true, userEmail: null, page: 1, pageSize: 10);

        // Assert
        totalCount.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetListAsync_WithSearchText_SearchesTranscript_WhenSearchInTranscriptTrue()
    {
        // Arrange — subject and summary do NOT contain the term; only raw transcript does.
        // Uses a unique search term to avoid collision with p6 which also has transcript-only content.
        var transcript = BuildTranscript("p7", "Routine Meeting 2", "Normal agenda", "TRANSCRIPT_ONLY_KEYWORD_P7: budget discussion");
        _context.MeetingTranscripts.Add(transcript);
        await _context.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter: null, searchText: "TRANSCRIPT_ONLY_KEYWORD_P7", searchInTranscript: true,
            isManager: true, userEmail: null, page: 1, pageSize: 10);

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle(x => x.PlaudRecordingId == "p7");
    }

    [Fact]
    public async Task GetListAsync_WithNullSearchText_ReturnsAll()
    {
        // Arrange
        _context.MeetingTranscripts.AddRange(
            BuildTranscript("p8", "Meeting A", "Summary A", "raw"),
            BuildTranscript("p9", "Meeting B", "Summary B", "raw"));
        await _context.SaveChangesAsync();

        // Act
        var (_, totalCount) = await _repository.GetListAsync(
            statusFilter: null, searchText: null, searchInTranscript: false,
            isManager: true, userEmail: null, page: 1, pageSize: 10);

        // Assert — at least the two records seeded in this test are returned
        totalCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetListAsync_SearchAndStatusFilter_BothApply()
    {
        // Arrange
        var pending = BuildTranscript("p10", "Budget Meeting", "Q4 budget", "raw", MeetingTranscriptStatus.PendingReview);
        var approved = BuildTranscript("p11", "Budget Review", "Approved budget", "raw", MeetingTranscriptStatus.Approved);
        _context.MeetingTranscripts.AddRange(pending, approved);
        await _context.SaveChangesAsync();

        // Act — search "budget" but only Approved status
        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter: MeetingTranscriptStatus.Approved, searchText: "budget", searchInTranscript: false,
            isManager: true, userEmail: null, page: 1, pageSize: 10);

        // Assert — only the Approved record matches both filters
        totalCount.Should().Be(1);
        items.Should().ContainSingle(x => x.PlaudRecordingId == "p11");
    }
}
