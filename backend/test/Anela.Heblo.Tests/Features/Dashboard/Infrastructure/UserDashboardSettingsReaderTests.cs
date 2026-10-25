using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard.Infrastructure;

public class UserDashboardSettingsReaderTests
{
    private static readonly DateTime FixedUtcNow = new(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITileRegistry> _tileRegistryMock = new();
    private readonly Mock<IUserDashboardSettingsRepository> _repositoryMock = new();
    private readonly UserDashboardSettingsReader _reader;

    public UserDashboardSettingsReaderTests()
    {
        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedUtcNow));
        _reader = new UserDashboardSettingsReader(_tileRegistryMock.Object, _repositoryMock.Object, timeProviderMock.Object);
    }

    [Fact]
    public async Task GetEffectiveSettings_WhenNewUser_ProjectsDefaultsWithoutWriting()
    {
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>
        {
            MakeTile("auto1"), MakeTile("auto2"), MakeTile("manual", autoShow: false)
        });
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync((UserDashboardSettings?)null);

        var result = await _reader.GetEffectiveSettingsAsync("user123");

        result.LastModified.Should().Be(FixedUtcNow);
        result.Tiles.Select(t => t.TileId).Should().Equal("auto1", "auto2");
        result.Tiles.Select(t => t.DisplayOrder).Should().Equal(0, 1);
        result.Tiles.Should().OnlyContain(t => t.IsVisible);
        VerifyNoWrites();
    }

    [Fact]
    public async Task GetEffectiveSettings_WhenTileMissing_ProjectsMissingAutoShowTileWithoutMutatingEntity()
    {
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>
        {
            MakeTile("auto1"), MakeTile("auto2"), MakeTile("newautoshow")
        });
        var existing = CreateExistingUserSettings("user123");
        var originalLastModified = existing.LastModified;
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(existing);

        var result = await _reader.GetEffectiveSettingsAsync("user123");

        result.Tiles.Should().HaveCount(3);
        var projected = result.Tiles.Single(t => t.TileId == "newautoshow");
        projected.IsVisible.Should().BeTrue();
        projected.DisplayOrder.Should().Be(2);
        result.LastModified.Should().Be(originalLastModified);
        existing.Tiles.Should().HaveCount(2);
        existing.LastModified.Should().Be(originalLastModified);
        VerifyNoWrites();
    }

    [Fact]
    public async Task GetEffectiveSettings_WhenFullyProvisioned_ReturnsPersistedTilesUnchanged()
    {
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>
        {
            MakeTile("auto1"), MakeTile("auto2")
        });
        var existing = CreateExistingUserSettings("user123");
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(existing);

        var result = await _reader.GetEffectiveSettingsAsync("user123");

        result.Tiles.Select(t => (t.TileId, t.IsVisible, t.DisplayOrder))
            .Should().Equal(("auto1", true, 0), ("auto2", false, 1));
        result.LastModified.Should().Be(existing.LastModified);
        VerifyNoWrites();
    }

    private void VerifyNoWrites()
    {
        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    private static TileMetadata MakeTile(string tileId, bool defaultEnabled = true, bool autoShow = true) =>
        new(tileId, tileId, $"{tileId} description", TileSize.Medium, TileCategory.Finance,
            defaultEnabled, autoShow, Array.Empty<string>());

    private static UserDashboardSettings CreateExistingUserSettings(string userId)
    {
        var settings = new UserDashboardSettings
        {
            UserId = userId,
            LastModified = FixedUtcNow.AddHours(-1)
        };
        settings.Tiles.Add(new UserDashboardTile { UserId = userId, TileId = "auto1", IsVisible = true, DisplayOrder = 0, LastModified = FixedUtcNow.AddHours(-2), DashboardSettings = settings });
        settings.Tiles.Add(new UserDashboardTile { UserId = userId, TileId = "auto2", IsVisible = false, DisplayOrder = 1, LastModified = FixedUtcNow.AddHours(-2), DashboardSettings = settings });
        return settings;
    }
}
