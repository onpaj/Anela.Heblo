using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetAvailableTiles;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;
using Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;
using Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class DashboardControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly DashboardController _controller;

    public DashboardControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new DashboardController(_mediatorMock.Object);

        // Setup mock user context
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-123")
        }));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetAvailableTiles_ShouldReturnOkWithTiles()
    {
        // Arrange
        var expectedTiles = new[]
        {
            new DashboardTileDto
            {
                TileId = "tile1",
                Title = "Test Tile 1",
                Description = "Description 1",
                Size = "Small",
                Category = "Analytics"
            },
            new DashboardTileDto
            {
                TileId = "tile2",
                Title = "Test Tile 2",
                Description = "Description 2",
                Size = "Large",
                Category = "Finance"
            }
        };

        var response = new GetAvailableTilesResponse { Tiles = expectedTiles };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetAvailableTilesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetAvailableTiles();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var tiles = okResult.Value.Should().BeAssignableTo<IEnumerable<DashboardTileDto>>().Subject;
        tiles.Should().HaveCount(2);
        tiles.Should().ContainEquivalentOf(expectedTiles[0]);
        tiles.Should().ContainEquivalentOf(expectedTiles[1]);

        _mediatorMock.Verify(x => x.Send(
            It.IsAny<GetAvailableTilesRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUserSettings_ShouldReturnOkWithUserSettings()
    {
        // Arrange
        var expectedSettings = new UserDashboardSettingsDto
        {
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 }
            }
        };

        var response = new GetUserSettingsResponse { Settings = expectedSettings };

        _mediatorMock
            .Setup(x => x.Send(It.Is<GetUserSettingsRequest>(r => r.UserId == "test-user-123"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetUserSettings();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var settings = okResult.Value.Should().BeAssignableTo<UserDashboardSettingsDto>().Subject;
        settings.Should().BeEquivalentTo(expectedSettings);

        _mediatorMock.Verify(x => x.Send(
            It.Is<GetUserSettingsRequest>(r => r.UserId == "test-user-123"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveUserSettings_ShouldReturnOk()
    {
        // Arrange
        var request = new SaveUserSettingsRequest
        {
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 },
                new UserDashboardTileDto { TileId = "tile2", IsVisible = false, DisplayOrder = 1 }
            }
        };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<SaveUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaveUserSettingsResponse { Success = true });

        // Act
        var result = await _controller.SaveUserSettings(request);

        // Assert
        result.Should().BeOfType<OkResult>();

        _mediatorMock.Verify(x => x.Send(
            It.Is<SaveUserSettingsRequest>(r =>
                r.UserId == "test-user-123" &&
                r.Tiles.Length == 2 &&
                r.Tiles.Any(t => t.TileId == "tile1" && t.IsVisible) &&
                r.Tiles.Any(t => t.TileId == "tile2" && !t.IsVisible)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTileData_ShouldReturnOkWithTileData()
    {
        // Arrange
        var expectedTiles = new[]
        {
            new DashboardTileDto
            {
                TileId = "tile1",
                Title = "Analytics Tile",
                Data = new { Count = 42 }
            }
        };

        var response = new GetTileDataResponse { Tiles = expectedTiles };

        _mediatorMock
            .Setup(x => x.Send(It.Is<GetTileDataRequest>(r => r.UserId == "test-user-123"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetTileData();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var tiles = okResult.Value.Should().BeAssignableTo<IEnumerable<DashboardTileDto>>().Subject;
        tiles.Should().HaveCount(1);
        tiles.First().TileId.Should().Be("tile1");

        _mediatorMock.Verify(x => x.Send(
            It.Is<GetTileDataRequest>(r => r.UserId == "test-user-123"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnableTile_ShouldReturnOk()
    {
        // Arrange
        var tileId = "analytics-tile";

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<EnableTileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnableTileResponse { Success = true });

        // Act
        var result = await _controller.EnableTile(tileId);

        // Assert
        result.Should().BeOfType<OkResult>();

        _mediatorMock.Verify(x => x.Send(
            It.Is<EnableTileRequest>(r =>
                r.UserId == "test-user-123" &&
                r.TileId == tileId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisableTile_ShouldReturnOk()
    {
        // Arrange
        var tileId = "analytics-tile";

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<DisableTileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisableTileResponse { Success = true });

        // Act
        var result = await _controller.DisableTile(tileId);

        // Assert
        result.Should().BeOfType<OkResult>();

        _mediatorMock.Verify(x => x.Send(
            It.Is<DisableTileRequest>(r =>
                r.UserId == "test-user-123" &&
                r.TileId == tileId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserId_WhenNoClaimsPresent_ShouldThrowException()
    {
        // Arrange
        var controller = new DashboardController(_mediatorMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(() => controller.GetUserSettings());
        exception.Message.Should().Be("User not found");

        // Verify that mediator was never called since exception was thrown before
        _mediatorMock.Verify(x => x.Send(
            It.IsAny<GetUserSettingsRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentUserId_WhenSubClaimPresent_ShouldUseSubClaim()
    {
        // Arrange
        var controller = new DashboardController(_mediatorMock.Object);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "sub-user-456")
        }));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserSettingsResponse { Settings = new UserDashboardSettingsDto() });

        // Act
        await controller.GetUserSettings();

        // Assert
        _mediatorMock.Verify(x => x.Send(
            It.Is<GetUserSettingsRequest>(r => r.UserId == "sub-user-456"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserId_WhenOidClaimPresent_ShouldUseOidClaim()
    {
        // Arrange
        var controller = new DashboardController(_mediatorMock.Object);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("oid", "oid-user-789")
        }));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserSettingsResponse { Settings = new UserDashboardSettingsDto() });

        // Act
        await controller.GetUserSettings();

        // Assert
        _mediatorMock.Verify(x => x.Send(
            It.Is<GetUserSettingsRequest>(r => r.UserId == "oid-user-789"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserId_WhenMultipleClaimsPresent_ShouldPrioritizeNameIdentifier()
    {
        // Arrange
        var controller = new DashboardController(_mediatorMock.Object);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "name-id-123"),
            new Claim("sub", "sub-user-456"),
            new Claim("oid", "oid-user-789")
        }));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetUserSettingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserSettingsResponse { Settings = new UserDashboardSettingsDto() });

        // Act
        await controller.GetUserSettings();

        // Assert
        _mediatorMock.Verify(x => x.Send(
            It.Is<GetUserSettingsRequest>(r => r.UserId == "name-id-123"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}