using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MeetingTasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Repositories;

public class MeetingTranscriptRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly MeetingTranscriptRepository _repository;

    public MeetingTranscriptRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new MeetingTranscriptRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetListAsync_Manager_SeesAllTranscripts()
    {
        await SeedTranscriptAsync(MeetingAccessLevel.Private);
        await SeedTranscriptAsync(MeetingAccessLevel.Public);
        await SeedTranscriptAsync(MeetingAccessLevel.Restricted, grantedEmail: "other@test.com");

        var (items, total) = await _repository.GetListAsync(
            statusFilter: null, searchText: null, searchInTranscript: false, isManager: true, userEmail: "manager@test.com",
            page: 1, pageSize: 20, ct: default);

        total.Should().Be(3);
        items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetListAsync_NonManager_SeesOnlyPublic()
    {
        await SeedTranscriptAsync(MeetingAccessLevel.Private);
        await SeedTranscriptAsync(MeetingAccessLevel.Public);

        var (items, total) = await _repository.GetListAsync(
            statusFilter: null, searchText: null, searchInTranscript: false, isManager: false, userEmail: "user@test.com",
            page: 1, pageSize: 20, ct: default);

        total.Should().Be(1);
        items.Should().HaveCount(1);
        items[0].AccessLevel.Should().Be(MeetingAccessLevel.Public);
    }

    [Fact]
    public async Task GetListAsync_NonManager_SeesRestrictedWhenGranted()
    {
        await SeedTranscriptAsync(MeetingAccessLevel.Restricted, grantedEmail: "user@test.com");
        await SeedTranscriptAsync(MeetingAccessLevel.Restricted, grantedEmail: "other@test.com");

        var (items, total) = await _repository.GetListAsync(
            statusFilter: null, searchText: null, searchInTranscript: false, isManager: false, userEmail: "user@test.com",
            page: 1, pageSize: 20, ct: default);

        total.Should().Be(1);
        items[0].AccessGrants.Should().ContainSingle(g => g.UserEmail == "user@test.com");
    }

    [Fact]
    public async Task GetListAsync_NonManager_PrivateNotReturned()
    {
        await SeedTranscriptAsync(MeetingAccessLevel.Private);

        var (items, total) = await _repository.GetListAsync(
            statusFilter: null, searchText: null, searchInTranscript: false, isManager: false, userEmail: "user@test.com",
            page: 1, pageSize: 20, ct: default);

        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetListAsync_PaginationReflectsFilteredSet()
    {
        for (var i = 0; i < 3; i++) await SeedTranscriptAsync(MeetingAccessLevel.Public);
        await SeedTranscriptAsync(MeetingAccessLevel.Private);

        var (items, total) = await _repository.GetListAsync(
            statusFilter: null, searchText: null, searchInTranscript: false, isManager: false, userEmail: "user@test.com",
            page: 1, pageSize: 2, ct: default);

        total.Should().Be(3);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_IncludesAccessGrants()
    {
        var id = await SeedTranscriptAsync(MeetingAccessLevel.Restricted, grantedEmail: "user@test.com");

        var result = await _repository.GetByIdAsync(id);

        result.Should().NotBeNull();
        result!.AccessGrants.Should().ContainSingle(g => g.UserEmail == "user@test.com");
    }

    [Fact]
    public async Task SetAccessAsync_ReplacesGrantsAndSetsLevel()
    {
        var id = await SeedTranscriptAsync(MeetingAccessLevel.Restricted, grantedEmail: "old@test.com");
        var transcript = await _context.MeetingTranscripts
            .Include(x => x.AccessGrants)
            .FirstAsync(x => x.Id == id);

        var newGrants = new List<MeetingAccessGrant>
        {
            new() { Id = Guid.NewGuid(), MeetingTranscriptId = id, UserEmail = "new@test.com", GrantedAt = DateTime.UtcNow, GrantedByUserEmail = "manager@test.com" }
        };

        await _repository.SetAccessAsync(transcript, MeetingAccessLevel.Public, newGrants, default);
        await _repository.SaveChangesAsync(default);

        var updated = await _context.MeetingTranscripts
            .Include(x => x.AccessGrants)
            .FirstAsync(x => x.Id == id);
        updated.AccessLevel.Should().Be(MeetingAccessLevel.Public);
        updated.AccessGrants.Should().ContainSingle(g => g.UserEmail == "new@test.com");
        updated.AccessGrants.Should().NotContain(g => g.UserEmail == "old@test.com");
    }

    private async Task<Guid> SeedTranscriptAsync(MeetingAccessLevel level, string? grantedEmail = null)
    {
        var transcript = new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = Guid.NewGuid().ToString(),
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Test",
            Summary = "Test",
            RawTranscript = "Test",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            AccessLevel = level,
            AccessGrants = grantedEmail is null
                ? []
                : [new MeetingAccessGrant { Id = Guid.NewGuid(), UserEmail = grantedEmail, GrantedAt = DateTime.UtcNow, GrantedByUserEmail = "seeder" }]
        };
        _context.MeetingTranscripts.Add(transcript);
        await _context.SaveChangesAsync();
        return transcript.Id;
    }
}
