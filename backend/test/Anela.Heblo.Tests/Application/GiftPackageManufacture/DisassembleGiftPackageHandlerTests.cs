using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.DisassembleGiftPackage;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

public class DisassembleGiftPackageHandlerTests
{
    private readonly Mock<IGiftPackageManufactureService> _serviceMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

    private DisassembleGiftPackageHandler CreateSut()
    {
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-1", Name: "test-user", Email: "test-user@example.com", IsAuthenticated: true));
        return new(_serviceMock.Object, _currentUserServiceMock.Object);
    }

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
            .Setup(s => s.DisassembleGiftPackageAsync("SET001", 2, "test-user", It.IsAny<CancellationToken>()))
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
            s => s.DisassembleGiftPackageAsync("SET001", 2, "test-user", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsInvalidOperation_WhenServiceThrowsInvalidOperationException()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.DisassembleGiftPackageAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

    [Fact]
    public async Task Handle_ForwardsSystemFallback_WhenCurrentUserNameIsNull()
    {
        // Arrange
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-1", Name: null, Email: null, IsAuthenticated: true));

        var disassembly = new GiftPackageDisassemblyDto
        {
            GiftPackageCode = "SET001",
            QuantityDisassembled = 1,
            DisassembledAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            DisassembledBy = "System",
            ReturnedComponents = new List<GiftPackageDisassemblyItemDto>()
        };

        _serviceMock
            .Setup(s => s.DisassembleGiftPackageAsync("SET001", 1, "System", It.IsAny<CancellationToken>()))
            .ReturnsAsync(disassembly);

        var request = new DisassembleGiftPackageRequest
        {
            GiftPackageCode = "SET001",
            Quantity = 1
        };

        // Act
        var handler = new DisassembleGiftPackageHandler(_serviceMock.Object, _currentUserServiceMock.Object);
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _serviceMock.Verify(
            s => s.DisassembleGiftPackageAsync("SET001", 1, "System", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
