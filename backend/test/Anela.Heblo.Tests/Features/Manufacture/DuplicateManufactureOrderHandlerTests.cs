using Anela.Heblo.Application.Features.Manufacture.UseCases.DuplicateManufactureOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class DuplicateManufactureOrderHandlerTests
{
    private const int SourceOrderId = 42;
    private const int PersistedOrderId = 1234;
    private const string GeneratedOrderNumber = "MO-2026-0042";
    private const string DisplayName = "Test User";
    private const string ResponsiblePerson = "Jane Foreman";

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IManufactureOrderRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly DuplicateManufactureOrderHandler _handler;

    public DuplicateManufactureOrderHandlerTests()
    {
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", DisplayName, "test@example.com", true));

        _handler = new DuplicateManufactureOrderHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object);
    }

    private static ManufactureOrder BuildSourceOrder(bool includeSemiProduct)
    {
        var order = new ManufactureOrder
        {
            Id = SourceOrderId,
            OrderNumber = "MO-2025-9999",
            ResponsiblePerson = ResponsiblePerson,
            State = ManufactureOrderState.Completed,
            PlannedDate = new DateOnly(2025, 1, 1),
            CreatedByUser = "Original Author",
            StateChangedByUser = "Original Author",
        };

        if (includeSemiProduct)
        {
            order.SemiProduct = new ManufactureOrderSemiProduct
            {
                ProductCode = "SEMI-001",
                ProductName = "Source Semi Product",
                PlannedQuantity = 1000m,
                ActualQuantity = 950m, // intentionally distinct from planned
                BatchMultiplier = 1.5m,
                ExpirationMonths = 24,
                LotNumber = "OLD-LOT",
                ExpirationDate = new DateOnly(2027, 1, 31),
            };
        }

        order.Products.Add(new ManufactureOrderProduct
        {
            ProductCode = "PROD-A",
            ProductName = "Source Product A",
            SemiProductCode = "SEMI-001",
            PlannedQuantity = 100m,
            ActualQuantity = 90m, // intentionally distinct from planned
            LotNumber = "OLD-LOT",
            ExpirationDate = new DateOnly(2027, 1, 31),
        });

        order.Products.Add(new ManufactureOrderProduct
        {
            ProductCode = "PROD-B",
            ProductName = "Source Product B",
            SemiProductCode = "SEMI-001",
            PlannedQuantity = 200m,
            ActualQuantity = 180m,
            LotNumber = "OLD-LOT",
            ExpirationDate = new DateOnly(2027, 1, 31),
        });

        return order;
    }

    [Fact]
    public async Task Handle_ReturnsOrderNotFound_WhenSourceOrderDoesNotExist()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(SourceOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        var request = new DuplicateManufactureOrderRequest { SourceOrderId = SourceOrderId };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.ErrorCode.Should().Be(ErrorCodes.OrderNotFound);
        response.Success.Should().BeFalse();

        _repositoryMock.Verify(
            x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicatesAllFields_WhenSourceHasSemiProductAndProducts()
    {
        // Arrange
        var sourceOrder = BuildSourceOrder(includeSemiProduct: true);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(SourceOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceOrder);
        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        ManufactureOrder? captured = null;
        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .Callback<ManufactureOrder, CancellationToken>((order, _) => captured = order)
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) =>
            {
                order.Id = PersistedOrderId;
                return order;
            });

        var request = new DuplicateManufactureOrderRequest { SourceOrderId = SourceOrderId };

        var expectedLot = ManufactureOrderExtensions.GetDefaultLot(FixedNow.UtcDateTime);
        var expectedExpiration = ManufactureOrderExtensions.GetDefaultExpiration(
            FixedNow.UtcDateTime,
            sourceOrder.SemiProduct!.ExpirationMonths);
        var expectedPlannedDate = DateOnly.FromDateTime(FixedNow.UtcDateTime);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert response
        response.Should().NotBeNull();
        response.ErrorCode.Should().BeNull();
        response.Id.Should().Be(PersistedOrderId);
        response.OrderNumber.Should().Be(GeneratedOrderNumber);

        // Assert captured order
        captured.Should().NotBeNull();
        captured!.OrderNumber.Should().Be(GeneratedOrderNumber);
        captured.State.Should().Be(ManufactureOrderState.Draft);
        captured.CreatedByUser.Should().Be(DisplayName);
        captured.StateChangedByUser.Should().Be(DisplayName);
        captured.ResponsiblePerson.Should().Be(ResponsiblePerson);
        captured.PlannedDate.Should().Be(expectedPlannedDate);

        // Assert duplicated semi-product
        captured.SemiProduct.Should().NotBeNull();
        captured.SemiProduct!.ProductCode.Should().Be(sourceOrder.SemiProduct.ProductCode);
        captured.SemiProduct.ProductName.Should().Be(sourceOrder.SemiProduct.ProductName);
        captured.SemiProduct.PlannedQuantity.Should().Be(sourceOrder.SemiProduct.PlannedQuantity);
        captured.SemiProduct.ActualQuantity.Should().Be(sourceOrder.SemiProduct.PlannedQuantity);
        captured.SemiProduct.BatchMultiplier.Should().Be(sourceOrder.SemiProduct.BatchMultiplier);
        captured.SemiProduct.ExpirationMonths.Should().Be(sourceOrder.SemiProduct.ExpirationMonths);
        captured.SemiProduct.LotNumber.Should().Be(expectedLot);
        captured.SemiProduct.ExpirationDate.Should().Be(expectedExpiration);

        // Assert duplicated products (collection-shaped, order-preserving)
        captured.Products.Should().HaveCount(sourceOrder.Products.Count);
        for (var i = 0; i < sourceOrder.Products.Count; i++)
        {
            var src = sourceOrder.Products[i];
            var dup = captured.Products[i];

            dup.ProductCode.Should().Be(src.ProductCode);
            dup.ProductName.Should().Be(src.ProductName);
            dup.SemiProductCode.Should().Be(src.SemiProductCode);
            dup.PlannedQuantity.Should().Be(src.PlannedQuantity);
            dup.ActualQuantity.Should().Be(src.PlannedQuantity);
            dup.LotNumber.Should().Be(expectedLot);
            dup.ExpirationDate.Should().Be(expectedExpiration);
        }
    }
}
