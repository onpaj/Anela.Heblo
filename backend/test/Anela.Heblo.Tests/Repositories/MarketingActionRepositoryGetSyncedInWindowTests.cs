using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Marketing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.Repositories;

public class MarketingActionRepositoryGetSyncedInWindowTests : IDisposable
{
    private static readonly DateTime WindowFrom = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowTo = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InsideWindow = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OutsideWindow = new(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _context;
    private readonly MarketingActionRepository _repository;

    public MarketingActionRepositoryGetSyncedInWindowTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new MarketingActionRepository(_context, NullLogger<MarketingActionRepository>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetSyncedInWindowAsync_ReturnsOnlySyncedNonDeletedActionsInsideWindow()
    {
        // Arrange
        var inside = await SeedAsync(startDate: InsideWindow, outlookEventId: "evt-inside");
        await SeedAsync(startDate: OutsideWindow, outlookEventId: "evt-outside");
        await SeedAsync(startDate: InsideWindow, outlookEventId: null);
        await SeedAsync(startDate: InsideWindow, outlookEventId: "evt-deleted", deleted: true);

        // Act
        var result = await _repository.GetSyncedInWindowAsync(WindowFrom, WindowTo);

        // Assert
        result.Should().ContainSingle(a => a.Id == inside.Id);
    }

    [Fact]
    public async Task GetSyncedInWindowAsync_IncludesActionsOnWindowBoundaries()
    {
        // Arrange
        await SeedAsync(startDate: WindowFrom, outlookEventId: "evt-start");
        await SeedAsync(startDate: WindowTo, outlookEventId: "evt-end");

        // Act
        var result = await _repository.GetSyncedInWindowAsync(WindowFrom, WindowTo);

        // Assert
        result.Should().HaveCount(2);
    }

    private async Task<MarketingAction> SeedAsync(DateTime startDate, string? outlookEventId, bool deleted = false)
    {
        var action = new MarketingAction(
            title: $"Action {Guid.NewGuid():N}",
            description: null,
            actionType: MarketingActionType.Blog,
            startDate: startDate,
            endDate: null,
            createdByUserId: "seed-user",
            createdByUsername: "Seeder",
            utcNow: DateTime.UtcNow);

        if (outlookEventId is not null)
        {
            action.MarkOutlookSynced(outlookEventId, DateTime.UtcNow);
        }

        if (deleted)
        {
            action.SoftDelete("seed-user", "Seeder", DateTime.UtcNow);
        }

        _context.Set<MarketingAction>().Add(action);
        await _context.SaveChangesAsync();
        return action;
    }
}
