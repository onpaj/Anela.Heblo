using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard.Infrastructure;

public class UserDashboardSettingsDefaultsTests
{
    private static readonly DateTime Now = new(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Earlier = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TileMetadata Tile(string id, bool defaultEnabled = true, bool autoShow = true)
        => new(id, id, id, TileSize.Small, TileCategory.Finance, defaultEnabled, autoShow, Array.Empty<string>());

    private static UserDashboardSettings Existing(params (string id, int order)[] tiles)
    {
        var s = new UserDashboardSettings { UserId = "u1", LastModified = Earlier };
        foreach (var (id, order) in tiles)
        {
            s.Tiles.Add(new UserDashboardTile
            {
                UserId = "u1",
                TileId = id,
                IsVisible = false,
                DisplayOrder = order,
                LastModified = Earlier,
                DashboardSettings = s
            });
        }
        return s;
    }

    [Fact]
    public void AutoShowTiles_FiltersOnDefaultEnabledAndAutoShow()
    {
        var registry = new Mock<ITileRegistry>();
        registry.Setup(r => r.GetAvailableTiles()).Returns(new[]
        {
            Tile("a"), Tile("b", defaultEnabled: false), Tile("c", autoShow: false)
        });

        UserDashboardSettingsDefaults.AutoShowTiles(registry.Object).Select(t => t.TileId)
            .Should().Equal("a");
    }

    [Fact]
    public void BuildDefault_SetsOrderTimestampsAndBackReference()
    {
        var settings = UserDashboardSettingsDefaults.BuildDefault("u1", new[] { Tile("a"), Tile("b") }, Now);

        settings.UserId.Should().Be("u1");
        settings.LastModified.Should().Be(Now);
        settings.Tiles.Select(t => (t.TileId, t.DisplayOrder, t.IsVisible)).Should()
            .Equal(("a", 0, true), ("b", 1, true));
        settings.Tiles.Should().OnlyContain(t => ReferenceEquals(t.DashboardSettings, settings) && t.LastModified == Now);
    }

    [Fact]
    public void BackfillMissing_NothingMissing_ReturnsFalseAndLeavesSettingsUntouched()
    {
        var settings = Existing(("a", 0));

        var changed = UserDashboardSettingsDefaults.BackfillMissing(settings, new[] { Tile("a") }, Now);

        changed.Should().BeFalse();
        settings.LastModified.Should().Be(Earlier);
        settings.Tiles.Should().HaveCount(1);
    }

    [Fact]
    public void BackfillMissing_AppendsAfterMaxOrderAndUpdatesLastModified()
    {
        var settings = Existing(("a", 5));

        var changed = UserDashboardSettingsDefaults.BackfillMissing(settings, new[] { Tile("a"), Tile("b"), Tile("c") }, Now);

        changed.Should().BeTrue();
        settings.LastModified.Should().Be(Now);
        settings.Tiles.Where(t => t.TileId != "a").Select(t => (t.TileId, t.DisplayOrder)).Should()
            .Equal(("b", 6), ("c", 7));
    }

    [Fact]
    public void ProjectMissing_DoesNotMutateSettings()
    {
        var settings = Existing(("a", 0));

        var projected = UserDashboardSettingsDefaults.ProjectMissing(settings, new[] { Tile("a"), Tile("b") }, Now);

        projected.Select(t => t.TileId).Should().Equal("a", "b");
        settings.Tiles.Should().HaveCount(1);
        settings.LastModified.Should().Be(Earlier);
    }

    [Fact]
    public void ProjectMissing_MatchesBackfillMissingResult()
    {
        var autoShow = new[] { Tile("a"), Tile("b"), Tile("c") };
        var projectedFrom = Existing(("a", 3));
        var backfilled = Existing(("a", 3));

        var projected = UserDashboardSettingsDefaults.ProjectMissing(projectedFrom, autoShow, Now);
        UserDashboardSettingsDefaults.BackfillMissing(backfilled, autoShow, Now);

        projected.Select(t => (t.TileId, t.IsVisible, t.DisplayOrder)).Should()
            .Equal(backfilled.Tiles.Select(t => (t.TileId, t.IsVisible, t.DisplayOrder)));
    }
}
