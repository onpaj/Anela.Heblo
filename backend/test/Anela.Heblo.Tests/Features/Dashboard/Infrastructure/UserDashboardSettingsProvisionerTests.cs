using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard.Infrastructure;

public class UserDashboardSettingsProvisionerTests
{
    private static readonly DateTime FixedUtcNow = new(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITileRegistry> _tileRegistryMock = new();
    private readonly Mock<IUserDashboardSettingsRepository> _repositoryMock = new();
    private readonly Mock<IUserDashboardSettingsLock> _lockMock = new();
    private readonly Mock<IAsyncDisposable> _lockHandle = new();
    private readonly UserDashboardSettingsProvisioner _provisioner;

    public UserDashboardSettingsProvisionerTests()
    {
        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedUtcNow));

        _lockHandle.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockMock.Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_lockHandle.Object);

        _provisioner = new UserDashboardSettingsProvisioner(
            _tileRegistryMock.Object,
            _repositoryMock.Object,
            _lockMock.Object,
            timeProviderMock.Object);
    }

    [Fact]
    public async Task EnsureProvisioned_WhenNewUser_CreatesDefaultSettingsWithAutoShowTiles()
    {
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>
        {
            MakeTile("auto1"), MakeTile("auto2"), MakeTile("manual", autoShow: false)
        });
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock.Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>())).ReturnsAsync((UserDashboardSettings s) => s);

        var result = await _provisioner.EnsureProvisionedAsync("user123");

        result.UserId.Should().Be("user123");
        result.LastModified.Should().Be(FixedUtcNow);
        result.Tiles.Select(t => t.TileId).Should().Equal("auto1", "auto2");
        _repositoryMock.Verify(x => x.AddAsync(It.Is<UserDashboardSettings>(s => s.UserId == "user123" && s.Tiles.Count == 2)), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task EnsureProvisioned_WhenNewUser_WithNoAutoShowTiles_CreatesEmptySettings()
    {
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata> { MakeTile("manual", autoShow: false) });
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock.Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>())).ReturnsAsync((UserDashboardSettings s) => s);

        var result = await _provisioner.EnsureProvisionedAsync("user123");

        result.Tiles.Should().BeEmpty();
        _repositoryMock.Verify(x => x.AddAsync(It.Is<UserDashboardSettings>(s => s.Tiles.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task EnsureProvisioned_WhenFullyProvisioned_DoesNotWrite()
    {
        var existing = CreateExistingUserSettings("user123");
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata> { MakeTile("auto1"), MakeTile("auto2") });
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(existing);

        var result = await _provisioner.EnsureProvisionedAsync("user123");

        result.Should().BeSameAs(existing);
        result.Tiles.Should().HaveCount(2);
        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task EnsureProvisioned_WhenNewAutoShowTile_BackfillsAndUpdates()
    {
        var existing = CreateExistingUserSettings("user123");
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>
        {
            MakeTile("auto1"), MakeTile("auto2"), MakeTile("newautoshow")
        });
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync(existing);

        var result = await _provisioner.EnsureProvisionedAsync("user123");

        result.Tiles.Should().HaveCount(3);
        var newTile = result.Tiles.Single(t => t.TileId == "newautoshow");
        newTile.IsVisible.Should().BeTrue();
        newTile.DisplayOrder.Should().Be(2);
        result.LastModified.Should().Be(FixedUtcNow);
        _repositoryMock.Verify(x => x.UpdateAsync(existing), Times.Once);
        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task EnsureProvisioned_AcquiresLockOnceAndReleasesItBeforeReturning()
    {
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>());
        _repositoryMock.Setup(x => x.GetByUserIdAsync("user123")).ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock.Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>())).ReturnsAsync((UserDashboardSettings s) => s);

        await _provisioner.EnsureProvisionedAsync("user123");

        _lockMock.Verify(x => x.AcquireAsync("user123", It.IsAny<CancellationToken>()), Times.Once);
        _lockHandle.Verify(x => x.DisposeAsync(), Times.Once);
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
