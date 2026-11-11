using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class DisableTileHandlerTests
{
    private readonly Mock<IUserDashboardSettingsRepository> _repositoryMock;
    private readonly Mock<IUserDashboardSettingsLock> _lockMock;
    private readonly Mock<IUserDashboardSettingsProvisioner> _provisionerMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly TimeProvider _timeProvider;
    private readonly DisableTileHandler _handler;

    private static readonly DateTime FixedUtcNow = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public DisableTileHandlerTests()
    {
        _repositoryMock = new Mock<IUserDashboardSettingsRepository>();
        _lockMock = new Mock<IUserDashboardSettingsLock>();
        _provisionerMock = new Mock<IUserDashboardSettingsProvisioner>();
        _currentUserMock = new Mock<ICurrentUserService>();

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(FixedUtcNow));
        _timeProvider = timeProviderMock.Object;

        // No-op disposable returned by the lock
        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable
            .Setup(x => x.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        _lockMock
            .Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(noOpDisposable.Object);

        // Provisioner returns default settings
        _provisionerMock
            .Setup(x => x.EnsureProvisionedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDashboardSettings());

        // Default current user mock to return user123
        _currentUserMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user123", Name: "Test User", Email: "test@example.com", IsAuthenticated: true));

        var mutator = new UserDashboardSettingsMutator(
            _repositoryMock.Object,
            _lockMock.Object,
            _timeProvider,
            _provisionerMock.Object);

        _handler = new DisableTileHandler(mutator, _currentUserMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ShouldUseAnonymous()
    {
        // Arrange
        _currentUserMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: null, Name: null, Email: null, IsAuthenticated: false));

        var request = new DisableTileRequest { TileId = "tile1" };
        var userSettings = CreateSampleUserSettings("anonymous");

        _repositoryMock
            .Setup(x => x.GetByUserIdAsync("anonymous"))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(x => x.GetByUserIdAsync("anonymous"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTileExists_ShouldDisableTile()
    {
        // Arrange
        var userId = "user123";
        var tileId = "tile1";
        var request = new DisableTileRequest { TileId = tileId };
        var userSettings = CreateSampleUserSettings(userId);

        // Ensure tile exists and is enabled
        userSettings.Tiles.First(t => t.TileId == tileId).IsVisible = true;

        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(userSettings);

        UserDashboardSettings? capturedSettings = null;
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()))
            .Callback<UserDashboardSettings>(s => capturedSettings = s);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        capturedSettings.Should().NotBeNull();
        var disabledTile = capturedSettings!.Tiles.First(t => t.TileId == tileId);
        disabledTile.IsVisible.Should().BeFalse();
        disabledTile.LastModified.Should().Be(FixedUtcNow);
        capturedSettings.LastModified.Should().Be(FixedUtcNow);

        _repositoryMock.Verify(x => x.GetByUserIdAsync(userId), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(userSettings), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTileDoesNotExist_ShouldNotCallUpdate()
    {
        // Arrange
        var userId = "user123";
        var tileId = "nonexistent-tile";
        var request = new DisableTileRequest { TileId = tileId };
        var userSettings = CreateSampleUserSettings(userId);
        var originalTileCount = userSettings.Tiles.Count;

        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        userSettings.Tiles.Should().HaveCount(originalTileCount);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTileIdIsNull_ShouldReturnFailure()
    {
        // Arrange
        var request = new DisableTileRequest { TileId = null };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();

        _provisionerMock.Verify(x => x.EnsureProvisionedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.GetByUserIdAsync(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTileIdIsEmpty_ShouldReturnFailure()
    {
        // Arrange
        var request = new DisableTileRequest { TileId = "" };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();

        _provisionerMock.Verify(x => x.EnsureProvisionedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.GetByUserIdAsync(It.IsAny<string>()), Times.Never);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AcquiresLockOncePerCall()
    {
        // Arrange
        var userId = "user123";
        var request = new DisableTileRequest { TileId = "tile1" };

        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(CreateSampleUserSettings(userId));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _lockMock.Verify(x => x.AcquireAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EnsuresProvisionedBeforeAcquiringLock()
    {
        // Arrange
        var userId = "user123";
        var request = new DisableTileRequest { TileId = "tile1" };

        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(CreateSampleUserSettings(userId));

        var callOrder = new List<string>();

        _provisionerMock
            .Setup(x => x.EnsureProvisionedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("provisioner"))
            .ReturnsAsync(new UserDashboardSettings());

        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockMock
            .Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("lock"))
            .ReturnsAsync(noOpDisposable.Object);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        callOrder.Should().ContainInOrder("provisioner", "lock");
        _provisionerMock.Verify(x => x.EnsureProvisionedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static UserDashboardSettings CreateSampleUserSettings(string userId)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>
            {
                new()
                {
                    UserId = userId,
                    TileId = "tile1",
                    IsVisible = true,
                    DisplayOrder = 0,
                    LastModified = DateTime.UtcNow.AddHours(-1)
                },
                new()
                {
                    UserId = userId,
                    TileId = "tile2",
                    IsVisible = false,
                    DisplayOrder = 1,
                    LastModified = DateTime.UtcNow.AddHours(-1)
                }
            }
        };
    }
}
