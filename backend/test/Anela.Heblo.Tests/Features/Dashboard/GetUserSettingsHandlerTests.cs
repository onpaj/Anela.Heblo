using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class GetUserSettingsHandlerTests
{
    private readonly Mock<IUserDashboardSettingsReader> _readerMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly GetUserSettingsHandler _handler;

    public GetUserSettingsHandlerTests()
    {
        _readerMock = new Mock<IUserDashboardSettingsReader>();
        _currentUserMock = new Mock<ICurrentUserService>();
        SetCurrentUserId("user123");

        _handler = new GetUserSettingsHandler(_readerMock.Object, _currentUserMock.Object);
    }

    private void SetCurrentUserId(string? id)
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: id, Name: "Test", Email: null, IsAuthenticated: !string.IsNullOrEmpty(id)));
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIdIsNull_ShouldUseAnonymous()
    {
        SetCurrentUserId(null);
        _readerMock
            .Setup(x => x.GetEffectiveSettingsAsync("anonymous", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDashboardSettingsDto());

        var result = await _handler.Handle(new GetUserSettingsRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        _readerMock.Verify(x => x.GetEffectiveSettingsAsync("anonymous", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCurrentUserIdIsEmpty_ShouldUseAnonymous()
    {
        SetCurrentUserId("");
        _readerMock
            .Setup(x => x.GetEffectiveSettingsAsync("anonymous", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDashboardSettingsDto());

        var result = await _handler.Handle(new GetUserSettingsRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        _readerMock.Verify(x => x.GetEffectiveSettingsAsync("anonymous", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldReturnReaderResultForCurrentUser()
    {
        var expected = new UserDashboardSettingsDto
        {
            Tiles = new[] { new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 } },
            LastModified = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        _readerMock
            .Setup(x => x.GetEffectiveSettingsAsync("user123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _handler.Handle(new GetUserSettingsRequest(), CancellationToken.None);

        result.Settings.Should().BeSameAs(expected);
        _readerMock.Verify(x => x.GetEffectiveSettingsAsync("user123", It.IsAny<CancellationToken>()), Times.Once);
    }
}
