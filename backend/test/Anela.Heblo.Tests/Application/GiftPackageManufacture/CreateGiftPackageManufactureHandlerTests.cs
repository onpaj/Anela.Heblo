using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

public class CreateGiftPackageManufactureHandlerTests
{
    private readonly Mock<IGiftPackageManufactureService> _serviceMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

    private CreateGiftPackageManufactureHandler CreateSut() =>
        new(_serviceMock.Object, _currentUserServiceMock.Object);

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
}
