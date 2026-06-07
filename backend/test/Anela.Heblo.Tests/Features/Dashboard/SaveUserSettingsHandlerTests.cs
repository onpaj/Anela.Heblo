using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class SaveUserSettingsHandlerTests
{
    private readonly Mock<IUserDashboardSettingsRepository> _repositoryMock;
    private readonly Mock<IUserDashboardSettingsLock> _lockMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly TimeProvider _timeProvider;
    private readonly SaveUserSettingsHandler _handler;

    private static readonly DateTime FixedUtcNow = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public SaveUserSettingsHandlerTests()
    {
        _repositoryMock = new Mock<IUserDashboardSettingsRepository>();
        _lockMock = new Mock<IUserDashboardSettingsLock>();
        _mediatorMock = new Mock<IMediator>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

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

        // Mediator returns a default GetUserSettingsResponse
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserSettingsResponse());

        // Default: current user has ID "user123"
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user123", Name: null, Email: "user@example.com", IsAuthenticated: true));

        _handler = new SaveUserSettingsHandler(
            _repositoryMock.Object,
            _lockMock.Object,
            _timeProvider,
            _mediatorMock.Object,
            _currentUserServiceMock.Object);
    }

    private void SetCurrentUserId(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            _currentUserServiceMock
                .Setup(x => x.GetCurrentUser())
                .Returns(new CurrentUser(Id: id ?? string.Empty, Name: null, Email: "user@example.com", IsAuthenticated: false));
        }
        else
        {
            _currentUserServiceMock
                .Setup(x => x.GetCurrentUser())
                .Returns(new CurrentUser(Id: id, Name: null, Email: "user@example.com", IsAuthenticated: true));
        }
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ShouldUseAnonymous()
    {
        // Arrange
        SetCurrentUserId(null);

        var request = new SaveUserSettingsRequest
        {
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 }
            }
        };

        var existingSettings = CreateSampleUserSettings("anonymous");
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync("anonymous"))
            .ReturnsAsync(existingSettings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _mediatorMock.Verify(x => x.Send(
            It.IsAny<GetUserSettingsRequest>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(x => x.GetByUserIdAsync("anonymous"), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(
            It.Is<UserDashboardSettings>(s => s.UserId == "anonymous")),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ShouldUseAnonymous()
    {
        // Arrange
        SetCurrentUserId("");

        var request = new SaveUserSettingsRequest
        {
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 }
            }
        };

        var existingSettings = CreateSampleUserSettings("anonymous");
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync("anonymous"))
            .ReturnsAsync(existingSettings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(x => x.UpdateAsync(
            It.Is<UserDashboardSettings>(s => s.UserId == "anonymous")),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValidUserId_ShouldSaveSettings()
    {
        // Arrange
        var userId = "user123";
        SetCurrentUserId(userId);

        var request = new SaveUserSettingsRequest
        {
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 },
                new UserDashboardTileDto { TileId = "tile2", IsVisible = false, DisplayOrder = 1 }
            }
        };

        var existingSettings = new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>
            {
                new() { UserId = userId, TileId = "tile1", IsVisible = false, DisplayOrder = 5, LastModified = DateTime.UtcNow },
                new() { UserId = userId, TileId = "tile2", IsVisible = true, DisplayOrder = 6, LastModified = DateTime.UtcNow }
            }
        };

        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(existingSettings);

        UserDashboardSettings? capturedSettings = null;
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()))
            .Callback<UserDashboardSettings>(s => capturedSettings = s);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        capturedSettings.Should().NotBeNull();
        capturedSettings!.UserId.Should().Be(userId);
        capturedSettings.Tiles.Should().HaveCount(2);
        capturedSettings.Tiles.Should().Contain(t => t.TileId == "tile1" && t.IsVisible && t.DisplayOrder == 0);
        capturedSettings.Tiles.Should().Contain(t => t.TileId == "tile2" && !t.IsVisible && t.DisplayOrder == 1);
        capturedSettings.LastModified.Should().Be(FixedUtcNow);
    }

    [Fact]
    public async Task Handle_WhenNoTiles_ShouldSaveEmptySettings()
    {
        // Arrange
        var userId = "user123";
        SetCurrentUserId(userId);

        var request = new SaveUserSettingsRequest
        {
            Tiles = Array.Empty<UserDashboardTileDto>()
        };

        var existingSettings = CreateSampleUserSettings(userId);
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(existingSettings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(x => x.UpdateAsync(
            It.Is<UserDashboardSettings>(s => s.UserId == userId && s.Tiles.Count == 0)),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNullTiles_ShouldNotMutateExistingTiles()
    {
        // Arrange
        var userId = "user123";
        SetCurrentUserId(userId);

        var request = new SaveUserSettingsRequest
        {
            Tiles = null
        };

        var existingSettings = new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>
            {
                new() { UserId = userId, TileId = "tile1", IsVisible = true, DisplayOrder = 0, LastModified = DateTime.UtcNow }
            }
        };

        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(existingSettings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert — existing tile unchanged, UpdateAsync still called (LastModified updated)
        _repositoryMock.Verify(x => x.UpdateAsync(
            It.Is<UserDashboardSettings>(s => s.UserId == userId && s.Tiles.Count == 1)),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldReturnSuccessResponse()
    {
        // Arrange
        SetCurrentUserId("user123");

        var request = new SaveUserSettingsRequest
        {
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 }
            }
        };

        var existingSettings = CreateSampleUserSettings("user123");
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync("user123"))
            .ReturnsAsync(existingSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AcquiresLockOncePerCall()
    {
        // Arrange
        var userId = "user123";
        SetCurrentUserId(userId);

        var request = new SaveUserSettingsRequest
        {
            Tiles = Array.Empty<UserDashboardTileDto>()
        };

        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(CreateSampleUserSettings(userId));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _lockMock.Verify(x => x.AcquireAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SendsGetUserSettingsBeforeAcquiringLock()
    {
        // Arrange
        var userId = "user123";
        SetCurrentUserId(userId);

        var request = new SaveUserSettingsRequest
        {
            Tiles = Array.Empty<UserDashboardTileDto>()
        };

        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(CreateSampleUserSettings(userId));

        var callOrder = new List<string>();

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("mediator"))
            .ReturnsAsync(new GetUserSettingsResponse());

        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _lockMock
            .Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("lock"))
            .ReturnsAsync(noOpDisposable.Object);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        callOrder.Should().ContainInOrder("mediator", "lock");
        _mediatorMock.Verify(x => x.Send(
            It.IsAny<GetUserSettingsRequest>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static UserDashboardSettings CreateSampleUserSettings(string userId)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>()
        };
    }
}
