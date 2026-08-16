using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MindMaps;

/// <summary>
/// Regression guard for a defect that reached a running environment: adding a child to a
/// TRACKED parent's navigation collection with the primary key already populated makes EF
/// classify the entity as Modified rather than Added. It then issues an UPDATE against a row
/// that does not exist yet, the statement affects 0 rows, and SaveChanges throws
/// DbUpdateConcurrencyException. Attach-meeting, the job's version snapshot and restore-version
/// all shared this shape, and every handler test mocked the repository, so nothing caught it.
///
/// These tests assert entity STATE rather than persistence, because the classification happens
/// in DetectChanges and is provider-independent.
/// </summary>
public class MindMapChildEntityStateTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mindmap-state-{Guid.NewGuid()}")
            .Options);

    private static MindMap SeedAndLoadMap(ApplicationDbContext ctx)
    {
        var map = new MindMap
        {
            Id = Guid.NewGuid(),
            Name = "Mapa",
            CurrentJson = "{}",
            Status = MindMapStatus.Idle,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ctx.MindMaps.Add(map);
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        // Reload so the parent is tracked as Unchanged, exactly as a handler sees it.
        return ctx.MindMaps.Include(x => x.Meetings).Include(x => x.Versions).Single();
    }

    [Fact]
    public void AddingMeetingToTrackedMap_MarksItAdded_NotModified()
    {
        using var ctx = NewContext();
        var map = SeedAndLoadMap(ctx);

        var meeting = new MindMapMeeting
        {
            MindMapId = map.Id,
            MeetingTranscriptId = Guid.NewGuid(),
            AttachedAt = DateTime.UtcNow
        };
        map.Meetings.Add(meeting);
        ctx.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Added, ctx.Entry(meeting).State);
        Assert.NotEqual(Guid.Empty, meeting.Id);
    }

    [Fact]
    public void AddingVersionToTrackedMap_MarksItAdded_NotModified()
    {
        using var ctx = NewContext();
        var map = SeedAndLoadMap(ctx);

        var version = new MindMapVersion
        {
            MindMapId = map.Id,
            VersionNumber = 1,
            Json = "{}",
            CreatedAt = DateTime.UtcNow
        };
        map.Versions.Add(version);
        ctx.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Added, ctx.Entry(version).State);
        Assert.NotEqual(Guid.Empty, version.Id);
    }
}
