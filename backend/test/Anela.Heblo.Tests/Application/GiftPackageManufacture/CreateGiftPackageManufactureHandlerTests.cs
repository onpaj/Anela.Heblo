using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

public class CreateGiftPackageManufactureHandlerTests
{
    private readonly Mock<IGiftPackageManufactureService> _serviceMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

    private CreateGiftPackageManufactureHandler CreateSut() =>
        new(_serviceMock.Object, _currentUserServiceMock.Object, NullLogger<CreateGiftPackageManufactureHandler>.Instance);

    [Fact]
    public async Task Handle_ForwardsResolvedUserName_ToCreateManufactureAsync()
    {
        // Arrange
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-1", Name: "jane.doe", Email: "jane.doe@example.com", IsAuthenticated: true));

        var manufacture = new GiftPackageManufactureDto
        {
            GiftPackageCode = "SET001",
            QuantityCreated = 3,
            CreatedBy = "jane.doe",
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        _serviceMock
            .Setup(s => s.CreateManufactureAsync("SET001", 3, false, "jane.doe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(manufacture);

        var request = new CreateGiftPackageManufactureRequest
        {
            GiftPackageCode = "SET001",
            Quantity = 3,
            AllowStockOverride = false
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Manufacture.Should().BeSameAs(manufacture);
        _serviceMock.Verify(
            s => s.CreateManufactureAsync("SET001", 3, false, "jane.doe", It.IsAny<CancellationToken>()),
            Times.Once);
        _currentUserServiceMock.Verify(x => x.GetCurrentUser(), Times.Once);
    }

    [Fact]
    public async Task Handle_FallsBackToSystem_WhenCurrentUserNameIsNull()
    {
        // Arrange
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-1", Name: null, Email: null, IsAuthenticated: true));
        AllowOverride();

        var manufacture = new GiftPackageManufactureDto
        {
            GiftPackageCode = "SET001",
            QuantityCreated = 1,
            CreatedBy = "System",
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        _serviceMock
            .Setup(s => s.CreateManufactureAsync("SET001", 1, true, "System", It.IsAny<CancellationToken>()))
            .ReturnsAsync(manufacture);

        var request = new CreateGiftPackageManufactureRequest
        {
            GiftPackageCode = "SET001",
            Quantity = 1,
            AllowStockOverride = true
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Manufacture.Should().BeSameAs(manufacture);
        _serviceMock.Verify(
            s => s.CreateManufactureAsync("SET001", 1, true, "System", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_MapsInsufficientStock_ToInvalidOperationWithMessageParam()
    {
        // Arrange
        ArrangeUser("jane.doe");
        _serviceMock
            .Setup(s => s.CreateManufactureAsync("SET001", 100, false, "jane.doe", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InsufficientStockException("Nelze vyrobit 100 ks - nedostatek zásob na skladě. Ingredient 1 (ING001): potřeba 200 ks, skladem 184 ks"));

        // Act
        var result = await CreateSut().Handle(CreateRequest(quantity: 100), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.Params.Should().ContainKey("ErrorMessage");
        result.Params!["ErrorMessage"].Should().Contain("ING001");
        result.Manufacture.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MapsInvalidQuantity_ToInvalidValueWithCleanMessage()
    {
        // Arrange
        ArrangeUser("jane.doe");
        _serviceMock
            .Setup(s => s.CreateManufactureAsync("SET001", 0, false, "jane.doe", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentOutOfRangeException("quantity", 0, "Množství musí být větší než 0"));

        // Act
        var result = await CreateSut().Handle(CreateRequest(quantity: 0), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        result.Params!["ErrorMessage"].Should().Be("Množství musí být větší než 0",
            "the .NET parameter-name suffix must not reach a user-facing toast");
    }

    [Fact]
    public async Task Handle_DoesNotSwallowInvalidOperationException_FromInfrastructure()
    {
        // Arrange - EF Core raises bare InvalidOperationException for tracking and concurrency
        // failures. Turning those into a 400 would hide real bugs behind an out-of-stock toast.
        ArrangeUser("jane.doe");
        _serviceMock
            .Setup(s => s.CreateManufactureAsync("SET001", 3, false, "jane.doe", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The instance of entity type cannot be tracked"));

        // Act
        var act = () => CreateSut().Handle(CreateRequest(quantity: 3), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_DoesNotSwallowArgumentException_ForUnknownGiftPackage()
    {
        // Arrange
        ArrangeUser("jane.doe");
        _serviceMock
            .Setup(s => s.CreateManufactureAsync("SET001", 3, false, "jane.doe", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Gift package 'SET001' not found or is not a set product"));

        // Act
        var act = () => CreateSut().Handle(CreateRequest(quantity: 3), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }


    [Fact]
    public async Task Handle_WhenOverrideRequestedWithoutPermission_RejectsWithoutCallingService()
    {
        // Arrange - allowStockOverride skips the warehouse-stock check entirely, so it is gated by
        // its own capability rather than by plain write access.
        ArrangeUser("jane.doe");

        // Act
        var result = await CreateSut().Handle(CreateRequest(quantity: 3, allowStockOverride: true), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InsufficientPermissions);
        result.Manufacture.Should().BeNull();
        _serviceMock.Verify(
            s => s.CreateManufactureAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOverrideRequestedWithPermission_PassesOverrideToService()
    {
        // Arrange
        ArrangeUser("jane.doe");
        AllowOverride();
        _serviceMock
            .Setup(s => s.CreateManufactureAsync("SET001", 3, true, "jane.doe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GiftPackageManufactureDto { GiftPackageCode = "SET001", QuantityCreated = 3 });

        // Act
        var result = await CreateSut().Handle(CreateRequest(quantity: 3, allowStockOverride: true), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _serviceMock.Verify(
            s => s.CreateManufactureAsync("SET001", 3, true, "jane.doe", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithoutOverrideRequested_DoesNotRequireTheOverrideCapability()
    {
        // Arrange - the ordinary path must stay open to anyone holding gift-package write access.
        ArrangeUser("jane.doe");
        _serviceMock
            .Setup(s => s.CreateManufactureAsync("SET001", 3, false, "jane.doe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GiftPackageManufactureDto { GiftPackageCode = "SET001", QuantityCreated = 3 });

        // Act
        var result = await CreateSut().Handle(CreateRequest(quantity: 3), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _currentUserServiceMock.Verify(x => x.IsInRole(It.IsAny<string>()), Times.Never);
    }

    private void AllowOverride() =>
        _currentUserServiceMock
            .Setup(x => x.IsInRole(AccessRoles.WarehouseStockOverrideRead))
            .Returns(true);

    private void ArrangeUser(string? name) =>
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-1", Name: name, Email: null, IsAuthenticated: true));

    private static CreateGiftPackageManufactureRequest CreateRequest(int quantity, bool allowStockOverride = false) =>
        new()
        {
            GiftPackageCode = "SET001",
            Quantity = quantity,
            AllowStockOverride = allowStockOverride
        };
}
