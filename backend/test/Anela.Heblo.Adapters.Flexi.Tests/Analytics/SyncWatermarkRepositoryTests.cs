using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class SyncWatermarkRepositoryTests
{
    private static AnalyticsDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AnalyticsDbContext(options);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenNoExistingState_ReturnsNewStateWithNullWatermark()
    {
        // Arrange
        await using var ctx = CreateInMemoryContext();
        var repo = new SyncWatermarkRepository(ctx);

        // Act
        var state = await repo.GetOrCreateAsync("ledger_entry");

        // Assert
        state.EntityName.Should().Be("ledger_entry");
        state.Watermark.Should().BeNull();
        state.LastRunStatus.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenStateExists_ReturnsSavedState()
    {
        // Arrange
        await using var ctx = CreateInMemoryContext();
        var expectedWatermark = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ctx.SyncStates.Add(new SyncState
        {
            EntityName = "ledger_entry",
            Watermark = expectedWatermark,
            LastRunStatus = "OK"
        });
        await ctx.SaveChangesAsync();
        var repo = new SyncWatermarkRepository(ctx);

        // Act
        var state = await repo.GetOrCreateAsync("ledger_entry");

        // Assert
        state.Watermark.Should().Be(expectedWatermark);
        state.LastRunStatus.Should().Be("OK");
    }

    [Fact]
    public async Task SaveAsync_PersistsChangesToDatabase()
    {
        // Arrange
        await using var ctx = CreateInMemoryContext();
        var repo = new SyncWatermarkRepository(ctx);
        var state = await repo.GetOrCreateAsync("department");
        var newWatermark = DateTimeOffset.UtcNow;

        // Act
        state.Watermark = newWatermark;
        state.LastRunStatus = "OK";
        await repo.SaveAsync(state);

        // Assert
        var saved = await ctx.SyncStates.FindAsync("department");
        saved!.Watermark.Should().BeCloseTo(newWatermark, TimeSpan.FromSeconds(1));
        saved.LastRunStatus.Should().Be("OK");
    }
}
