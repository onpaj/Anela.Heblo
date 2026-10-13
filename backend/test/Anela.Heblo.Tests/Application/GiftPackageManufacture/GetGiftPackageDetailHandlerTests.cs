using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageDetail;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

public class GetGiftPackageDetailHandlerTests
{
    private readonly Mock<IGiftPackageQueryService> _serviceMock = new();

    private GetGiftPackageDetailHandler CreateSut() => new(_serviceMock.Object);

    [Fact]
    public async Task Handle_ReturnsSuccessWithGiftPackage_WhenServiceSucceeds()
    {
        // Arrange
        var giftPackage = new GiftPackageDto
        {
            Code = "SET001",
            Name = "Sample Gift Set"
        };

        _serviceMock
            .Setup(s => s.GetGiftPackageDetailAsync("SET001", 1.0m, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(giftPackage);

        var request = new GetGiftPackageDetailRequest
        {
            GiftPackageCode = "SET001",
            SalesCoefficient = 1.0m
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.GiftPackage.Should().NotBeNull();
        result.GiftPackage!.Code.Should().Be("SET001");

        _serviceMock.Verify(
            s => s.GetGiftPackageDetailAsync("SET001", 1.0m, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsValidationError_WhenServiceThrowsArgumentException()
    {
        // Arrange
        // Use single-argument constructor — two-argument ctor appends " (Parameter 'name')" to Message.
        _serviceMock
            .Setup(s => s.GetGiftPackageDetailAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Gift package code 'MISSING' not found"));

        var request = new GetGiftPackageDetailRequest
        {
            GiftPackageCode = "MISSING",
            SalesCoefficient = 1.0m
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorCode.Should().NotBe(ErrorCodes.InternalServerError);
        result.GiftPackage.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsInternalServerError_WhenServiceThrowsUnexpectedException()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GetGiftPackageDetailAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Downstream stock service unavailable"));

        var request = new GetGiftPackageDetailRequest
        {
            GiftPackageCode = "SET001",
            SalesCoefficient = 1.0m
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        result.ErrorCode.Should().NotBe(ErrorCodes.ValidationError);
        result.GiftPackage.Should().BeNull();
    }
}
