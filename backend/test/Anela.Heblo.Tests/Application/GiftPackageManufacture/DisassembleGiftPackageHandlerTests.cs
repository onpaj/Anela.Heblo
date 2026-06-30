using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.DisassembleGiftPackage;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

public class DisassembleGiftPackageHandlerTests
{
    private readonly Mock<IGiftPackageManufactureService> _serviceMock = new();

    private DisassembleGiftPackageHandler CreateSut() =>
        new(_serviceMock.Object);

    [Fact]
    public async Task Handle_ReturnsSuccessWithDisassembly_WhenServiceSucceeds()
    {
        // Arrange
        var disassembly = new GiftPackageDisassemblyDto
        {
            GiftPackageCode = "SET001",
            QuantityDisassembled = 2,
            DisassembledAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            DisassembledBy = "test-user",
            ReturnedComponents = new List<GiftPackageDisassemblyItemDto>()
        };

        _serviceMock
            .Setup(s => s.DisassembleGiftPackageAsync("SET001", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(disassembly);

        var request = new DisassembleGiftPackageRequest
        {
            GiftPackageCode = "SET001",
            Quantity = 2
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Disassembly.GiftPackageCode.Should().Be("SET001");
        result.Disassembly.QuantityDisassembled.Should().Be(2);

        _serviceMock.Verify(
            s => s.DisassembleGiftPackageAsync("SET001", 2, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsInvalidOperation_WhenServiceThrowsInvalidOperationException()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.DisassembleGiftPackageAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Package SET001 does not exist"));

        var request = new DisassembleGiftPackageRequest
        {
            GiftPackageCode = "SET001",
            Quantity = 2
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.Params.Should().ContainKey("ErrorMessage")
            .WhoseValue.Should().Be("Package SET001 does not exist");
    }

    [Fact]
    public async Task Handle_ReturnsInvalidValue_WhenServiceThrowsArgumentException()
    {
        // Arrange
        // Use single-argument constructor — two-argument ctor appends " (Parameter 'name')" to Message.
        _serviceMock
            .Setup(s => s.DisassembleGiftPackageAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Quantity must be greater than zero"));

        var request = new DisassembleGiftPackageRequest
        {
            GiftPackageCode = "SET001",
            Quantity = -1
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        result.ErrorCode.Should().NotBe(ErrorCodes.InvalidOperation);
        result.Params.Should().ContainKey("ErrorMessage")
            .WhoseValue.Should().Be("Quantity must be greater than zero");
    }
}
