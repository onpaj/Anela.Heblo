using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;

namespace Anela.Heblo.Tests.Features.Manufacture.UseCases;

public class UpdateManufactureOrderStatusHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<ILogger<UpdateManufactureOrderStatusHandler>> _loggerMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly UpdateManufactureOrderStatusHandler _handler;
    private static readonly DateTime FixedUtcNow = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    public UpdateManufactureOrderStatusHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderStatusHandler>>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedUtcNow));

        var httpContext = new Mock<HttpContext>();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser") }, "test");
        httpContext.Setup(x => x.User).Returns(new ClaimsPrincipal(identity));
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext.Object);

        _handler = new UpdateManufactureOrderStatusHandler(
            _repositoryMock.Object,
            _timeProviderMock.Object,
            _loggerMock.Object,
            _httpContextAccessorMock.Object);
    }

    [Fact]
    public async Task Handle_WhenWeightFieldsProvided_SetsThemOnSavedOrder()
    {
        // Arrange
        var order = new ManufactureOrder
        {
            Id = 1,
            OrderNumber = "MO-2024-001",
            State = ManufactureOrderState.SemiProductManufactured,
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = "System",
            CreatedDate = DateTime.UtcNow,
            CreatedByUser = "System",
            Products = new List<ManufactureOrderProduct>(),
            Notes = new List<ManufactureOrderNote>()
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.Completed,
            WeightWithinTolerance = true,
            WeightDifference = 5.5m
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        _repositoryMock.Verify(x => x.UpdateOrderAsync(
            It.Is<ManufactureOrder>(o =>
                o.WeightWithinTolerance == true &&
                o.WeightDifference == 5.5m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenWeightFieldsNull_DoesNotOverwriteExistingValues()
    {
        // Arrange
        var order = new ManufactureOrder
        {
            Id = 1,
            OrderNumber = "MO-2024-001",
            State = ManufactureOrderState.Planned,
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = "System",
            CreatedDate = DateTime.UtcNow,
            CreatedByUser = "System",
            WeightWithinTolerance = true,
            WeightDifference = 3.0m,
            Products = new List<ManufactureOrderProduct>(),
            Notes = new List<ManufactureOrderNote>()
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.SemiProductManufactured,
            WeightWithinTolerance = null,
            WeightDifference = null
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert - existing values preserved
        _repositoryMock.Verify(x => x.UpdateOrderAsync(
            It.Is<ManufactureOrder>(o =>
                o.WeightWithinTolerance == true &&
                o.WeightDifference == 3.0m),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
