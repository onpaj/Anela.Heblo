using System.Text.Json;
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.DashboardTiles;

public class CriticalGiftPackagesTileTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly CriticalGiftPackagesTile _tile;

    public CriticalGiftPackagesTileTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _timeProviderMock = new Mock<TimeProvider>();
        _tile = new CriticalGiftPackagesTile(_mediatorMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public async Task LoadDataAsync_WhenResponseNotSuccessful_ReturnsErrorStatus()
    {
        // Arrange
        var response = new GetAvailableGiftPackagesResponse { Success = false };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetAvailableGiftPackagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("status").GetString().Should().Be("error");
        json.GetProperty("error").GetString().Should().Be("Failed to load gift packages data");
        json.TryGetProperty("data", out _).Should().BeFalse();

        _mediatorMock.Verify(
            x => x.Send(It.IsAny<GetAvailableGiftPackagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadDataAsync_WhenServiceThrows_ReturnsExceptionErrorStatus()
    {
        // Arrange
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetAvailableGiftPackagesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated gift package service failure"));

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("status").GetString().Should().Be("error");
        json.GetProperty("error").GetString().Should().Be("Simulated gift package service failure");
        json.TryGetProperty("data", out _).Should().BeFalse();
    }
}
