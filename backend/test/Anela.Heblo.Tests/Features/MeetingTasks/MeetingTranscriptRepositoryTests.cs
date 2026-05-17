using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MeetingTasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class MeetingTranscriptRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly MeetingTranscriptRepository _repository;

    public MeetingTranscriptRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new MeetingTranscriptRepository(_context);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTranscriptWithTasks_WhenExists()
    {
        // Arrange
        var transcript = BuildTranscript("plaud-1", taskCount: 2);
        _context.MeetingTranscripts.Add(transcript);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(transcript.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(transcript.Id);
        result.Tasks.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetListAsync_FiltersByStatus_PaginatesAndOrdersByPlaudCreatedAtDescending()
    {
        // Arrange — three transcripts, two PendingReview with distinct PlaudCreatedAt, one Approved
        var older = BuildTranscript("plaud-old", taskCount: 0);
        older.PlaudCreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        older.Status = MeetingTranscriptStatus.PendingReview;

        var newer = BuildTranscript("plaud-new", taskCount: 1);
        newer.PlaudCreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        newer.Status = MeetingTranscriptStatus.PendingReview;

        var approved = BuildTranscript("plaud-approved", taskCount: 0);
        approved.PlaudCreatedAt = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc);
        approved.Status = MeetingTranscriptStatus.Approved;

        _context.MeetingTranscripts.AddRange(older, newer, approved);
        await _context.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter: MeetingTranscriptStatus.PendingReview,
            isManager: true,
            userEmail: null,
            page: 1,
            pageSize: 10);

        // Assert
        totalCount.Should().Be(2);
        items.Should().HaveCount(2);
        items[0].PlaudRecordingId.Should().Be("plaud-new");   // newer first
        items[1].PlaudRecordingId.Should().Be("plaud-old");
        items[0].Tasks.Should().HaveCount(1);                 // Tasks eagerly loaded
    }

    [Fact]
    public async Task GetListAsync_WithoutStatusFilter_ReturnsAll()
    {
        _context.MeetingTranscripts.Add(BuildTranscript("plaud-a", 0));
        _context.MeetingTranscripts.Add(BuildTranscript("plaud-b", 0));
        await _context.SaveChangesAsync();

        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter: null, isManager: true, userEmail: null, page: 1, pageSize: 10);

        totalCount.Should().Be(2);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExistsByPlaudIdAsync_ReturnsTrueWhenPresent_FalseOtherwise()
    {
        _context.MeetingTranscripts.Add(BuildTranscript("plaud-x", 0));
        await _context.SaveChangesAsync();

        (await _repository.ExistsByPlaudIdAsync("plaud-x")).Should().BeTrue();
        (await _repository.ExistsByPlaudIdAsync("plaud-missing")).Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_PersistsAggregateAndChildren_AfterSaveChanges()
    {
        var transcript = BuildTranscript("plaud-new", taskCount: 3);

        await _repository.AddAsync(transcript);
        await _repository.SaveChangesAsync();

        var reloaded = await _context.MeetingTranscripts
            .Include(t => t.Tasks)
            .FirstAsync(t => t.PlaudRecordingId == "plaud-new");

        reloaded.Tasks.Should().HaveCount(3);
    }

    private static MeetingTranscript BuildTranscript(string plaudId, int taskCount)
    {
        var now = DateTime.UtcNow;
        var transcript = new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = plaudId,
            PlaudCreatedAt = now,
            Subject = $"Subject {plaudId}",
            Summary = $"Summary {plaudId}",
            RawTranscript = $"Raw {plaudId}",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = now
        };

        for (var i = 0; i < taskCount; i++)
        {
            transcript.Tasks.Add(new ProposedTask
            {
                Id = Guid.NewGuid(),
                Title = $"Task {i}",
                Description = $"Description {i}",
                Assignee = "alice",
                Status = ProposedTaskStatus.Pending,
                IsManuallyAdded = false
            });
        }

        return transcript;
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
