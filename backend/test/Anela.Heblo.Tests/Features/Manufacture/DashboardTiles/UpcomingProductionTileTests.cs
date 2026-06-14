using System.Reflection;
using Anela.Heblo.Application.Features.Manufacture.DashboardTiles;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Anela.Heblo.Tests.Features.Manufacture.DashboardTiles;

public class UpcomingProductionTileTests
{
    // Monday 2026-06-15 12:00 UTC — ReferenceDate for TodayProductionTile = June 15 = today (weekly).
    // Also: NextDayProductionTile.GetNextWorkingDay(June 15) = June 16 = today+1 (weekly).
    private static readonly DateTimeOffset FrozenMondayUtc =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    // Friday 2026-06-19 12:00 UTC — NextDayProductionTile.GetNextWorkingDay(June 19) = June 22 (Mon),
    // which is neither today nor today+1, so GenerateDrillDownFilters() returns "grid".
    private static readonly DateTimeOffset FrozenFridayUtc =
        new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IManufactureOrderRepository> _repositoryMock = new();

    [Fact]
    public async Task TodayProductionTile_GenerateDrillDownFilters_ReturnsWeeklyView()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(FrozenMondayUtc);
        var tile = new TodayProductionTile(_repositoryMock.Object, timeProvider);
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureOrder>());

        // Act
        var payload = await tile.LoadDataAsync();
        var filters = GetAnonymousProperty(payload, "drillDown.filters");

        // Assert
        Assert.Equal("2026-06-15", GetAnonymousProperty(filters!, "date"));
        Assert.Equal("weekly", GetAnonymousProperty(filters!, "view"));
    }

    [Fact]
    public async Task NextDayProductionTile_OnWeekday_GenerateDrillDownFilters_ReturnsWeeklyView()
    {
        // Arrange: Monday → next working day = Tuesday = today + 1.
        var timeProvider = new FakeTimeProvider(FrozenMondayUtc);
        var tile = new NextDayProductionTile(_repositoryMock.Object, timeProvider);
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureOrder>());

        // Act
        var payload = await tile.LoadDataAsync();
        var filters = GetAnonymousProperty(payload, "drillDown.filters");

        // Assert
        Assert.Equal("2026-06-16", GetAnonymousProperty(filters!, "date"));
        Assert.Equal("weekly", GetAnonymousProperty(filters!, "view"));
    }

    [Fact]
    public async Task NextDayProductionTile_OnFriday_GenerateDrillDownFilters_ReturnsGridView()
    {
        // Arrange: Friday → next working day = Monday, which is today + 3, neither today nor today+1.
        var timeProvider = new FakeTimeProvider(FrozenFridayUtc);
        var tile = new NextDayProductionTile(_repositoryMock.Object, timeProvider);
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureOrder>());

        // Act
        var payload = await tile.LoadDataAsync();
        var filters = GetAnonymousProperty(payload, "drillDown.filters");

        // Assert
        Assert.Equal("2026-06-22", GetAnonymousProperty(filters!, "date"));
        Assert.Equal("grid", GetAnonymousProperty(filters!, "view"));
    }

    [Fact]
    public async Task LoadDataAsync_LastUpdated_ComesFromTimeProvider()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(FrozenMondayUtc);
        var tile = new TodayProductionTile(_repositoryMock.Object, timeProvider);
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureOrder>());

        // Act
        var payload = await tile.LoadDataAsync();
        var lastUpdated = (DateTime)GetAnonymousProperty(payload, "metadata.lastUpdated")!;

        // Assert
        Assert.Equal(FrozenMondayUtc.UtcDateTime, lastUpdated);
        Assert.Equal(DateTimeKind.Utc, lastUpdated.Kind);
    }

    private static object? GetAnonymousProperty(object source, string path)
    {
        object? current = source;
        foreach (var name in path.Split('.'))
        {
            if (current is null) return null;
            var prop = current.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            current = prop?.GetValue(current);
        }
        return current;
    }
}
