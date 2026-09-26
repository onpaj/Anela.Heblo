using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class SaveUserSettingsHandlerTests
{
    private readonly Mock<IUserDashboardSettingsMutator> _mutatorMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly SaveUserSettingsHandler _handler;

    public SaveUserSettingsHandlerTests()
    {
        _mutatorMock = new Mock<IUserDashboardSettingsMutator>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user123", Name: null, Email: "user@example.com", IsAuthenticated: true));

        _mutatorMock
            .Setup(x => x.MutateBulkAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyList<UserDashboardTileDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDashboardSettingsMutationResult(SettingsLoaded: true, TileFound: true, TileAppended: false));

        _handler = new SaveUserSettingsHandler(_mutatorMock.Object, _currentUserServiceMock.Object);
    }

    private void SetCurrentUserId(string? id)
    {
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: id ?? string.Empty, Name: null, Email: "user@example.com", IsAuthenticated: !string.IsNullOrEmpty(id)));
    }

    [Fact]
    public async Task Handle_PassesCurrentUserIdAndTilesToMutator()
    {
        SetCurrentUserId("user123");
        var tiles = new[]
        {
            new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 },
            new UserDashboardTileDto { TileId = "tile2", IsVisible = false, DisplayOrder = 1 }
        };
        var request = new SaveUserSettingsRequest { Tiles = tiles };

        await _handler.Handle(request, CancellationToken.None);

        _mutatorMock.Verify(x => x.MutateBulkAsync(
            "user123",
            It.Is<IReadOnlyList<UserDashboardTileDto>>(t => t.SequenceEqual(tiles)),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTilesIsNull_PassesEmptyListToMutator()
    {
        SetCurrentUserId("user123");
        var request = new SaveUserSettingsRequest { Tiles = null! };

        await _handler.Handle(request, CancellationToken.None);

        _mutatorMock.Verify(x => x.MutateBulkAsync(
            "user123",
            It.Is<IReadOnlyList<UserDashboardTileDto>>(t => t.Count == 0),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNullOrEmpty_PassesItThroughUnchanged()
    {
        SetCurrentUserId(null);
        var request = new SaveUserSettingsRequest { Tiles = Array.Empty<UserDashboardTileDto>() };

        await _handler.Handle(request, CancellationToken.None);

        _mutatorMock.Verify(x => x.MutateBulkAsync(
            string.Empty,
            It.IsAny<IReadOnlyList<UserDashboardTileDto>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AlwaysReturnsSuccessResponse()
    {
        SetCurrentUserId("user123");
        var request = new SaveUserSettingsRequest { Tiles = Array.Empty<UserDashboardTileDto>() };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CallsMutatorExactlyOnce()
    {
        SetCurrentUserId("user123");
        var request = new SaveUserSettingsRequest { Tiles = Array.Empty<UserDashboardTileDto>() };

        await _handler.Handle(request, CancellationToken.None);

        _mutatorMock.Verify(x => x.MutateBulkAsync(
            It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<UserDashboardTileDto>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
