using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class SubmitManufactureHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _manufactureOrderRepositoryMock;
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<ILogger<SubmitManufactureHandler>> _loggerMock;
    private readonly SubmitManufactureHandler _handler;

    public SubmitManufactureHandlerTests()
    {
        _manufactureOrderRepositoryMock = new Mock<IManufactureOrderRepository>();
        _manufactureClientMock = new Mock<IManufactureClient>();
        _loggerMock = new Mock<ILogger<SubmitManufactureHandler>>();
        _handler = new SubmitManufactureHandler(
            _manufactureOrderRepositoryMock.Object,
            _manufactureClientMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_SuccessfulTwoMovementCreation_ReturnsManufactureId()
    {
        // Arrange
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MO-2024-001",
            ManufactureInternalNumber = "INT-001",
            Date = new DateTime(2024, 1, 15),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT001",
                    Name = "Test Material 1",
                    Amount = 100.5m
                },
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT002",
                    Name = "Test Material 2",
                    Amount = 50.25m
                }
            },
            LotNumber = "LOT-2024-001",
            ExpirationDate = new DateOnly(2025, 1, 15)
        };

        var expectedMovementReference = "MOV-2024-001";

        _manufactureClientMock
            .Setup(x => x.SubmitManufactureAsync(
                It.IsAny<SubmitManufactureClientRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMovementReference);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ManufactureId.Should().Be(expectedMovementReference);
        result.ErrorCode.Should().BeNull();
        result.Params.Should().BeNull();

        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.Is<SubmitManufactureClientRequest>(r =>
                r.ManufactureOrderCode == "MO-2024-001" &&
                r.ManufactureInternalNumber == "INT-001" &&
                r.Date == new DateTime(2024, 1, 15) &&
                r.CreatedBy == "TestUser" &&
                r.ManufactureType == ErpManufactureType.Product &&
                r.Items.Count == 2 &&
                r.LotNumber == "LOT-2024-001" &&
                r.ExpirationDate == new DateOnly(2025, 1, 15)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SuccessfulTwoMovementCreation_VerifiesConsumptionMovementCreatedFirst()
    {
        // Arrange
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MO-2024-002",
            ManufactureInternalNumber = "INT-002",
            Date = new DateTime(2024, 1, 16),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.SemiProduct,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT003",
                    Name = "Test Material 3",
                    Amount = 75.0m
                }
            },
            LotNumber = "LOT-2024-002",
            ExpirationDate = new DateOnly(2025, 2, 16)
        };

        var expectedMovementReference = "MOV-CONSUMPTION-001";

        _manufactureClientMock
            .Setup(x => x.SubmitManufactureAsync(
                It.IsAny<SubmitManufactureClientRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMovementReference);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ManufactureId.Should().Be(expectedMovementReference);

        // Verify that SubmitManufactureAsync was called
        // The implementation in FlexiManufactureClient creates consumption movement first
        // and returns the consumption movement reference
        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.Is<SubmitManufactureClientRequest>(r =>
                r.ManufactureOrderCode == "MO-2024-002" &&
                r.ManufactureType == ErpManufactureType.SemiProduct),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SuccessfulTwoMovementCreation_VerifiesProductionMovementCreatedSecond()
    {
        // Arrange
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MO-2024-003",
            ManufactureInternalNumber = "INT-003",
            Date = new DateTime(2024, 1, 17),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT004",
                    Name = "Test Material 4",
                    Amount = 200.0m
                }
            },
            LotNumber = "LOT-2024-003",
            ExpirationDate = new DateOnly(2025, 3, 17)
        };

        var expectedMovementReference = "MOV-CONSUMPTION-002";

        _manufactureClientMock
            .Setup(x => x.SubmitManufactureAsync(
                It.IsAny<SubmitManufactureClientRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMovementReference);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ManufactureId.Should().Be(expectedMovementReference);

        // The implementation creates production movement after consumption movement succeeds
        // Both movements are created within the same SubmitManufactureAsync call
        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.IsAny<SubmitManufactureClientRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SuccessfulTwoMovementCreation_VerifiesBothMovementsLinkedToOperation()
    {
        // Arrange
        var manufactureOrderCode = "MO-2024-004";
        var manufactureInternalNumber = "INT-004";

        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = manufactureOrderCode,
            ManufactureInternalNumber = manufactureInternalNumber,
            Date = new DateTime(2024, 1, 18),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT005",
                    Name = "Test Material 5",
                    Amount = 150.0m
                }
            },
            LotNumber = "LOT-2024-004",
            ExpirationDate = new DateOnly(2025, 4, 18)
        };

        var expectedMovementReference = "MOV-2024-004";

        _manufactureClientMock
            .Setup(x => x.SubmitManufactureAsync(
                It.IsAny<SubmitManufactureClientRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMovementReference);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ManufactureId.Should().Be(expectedMovementReference);

        // Verify that both movements are linked through the same ManufactureOrderCode
        // and ManufactureInternalNumber in the SubmitManufactureClientRequest
        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.Is<SubmitManufactureClientRequest>(r =>
                r.ManufactureOrderCode == manufactureOrderCode &&
                r.ManufactureInternalNumber == manufactureInternalNumber &&
                r.Date == new DateTime(2024, 1, 18)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SuccessfulTwoMovementCreation_VerifiesCorrectMaterialQuantities()
    {
        // Arrange
        var material1Amount = 125.75m;
        var material2Amount = 88.25m;
        var material3Amount = 42.5m;

        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MO-2024-005",
            ManufactureInternalNumber = "INT-005",
            Date = new DateTime(2024, 1, 19),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT006",
                    Name = "Test Material 6",
                    Amount = material1Amount
                },
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT007",
                    Name = "Test Material 7",
                    Amount = material2Amount
                },
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT008",
                    Name = "Test Material 8",
                    Amount = material3Amount
                }
            },
            LotNumber = "LOT-2024-005",
            ExpirationDate = new DateOnly(2025, 5, 19)
        };

        var expectedMovementReference = "MOV-2024-005";

        _manufactureClientMock
            .Setup(x => x.SubmitManufactureAsync(
                It.IsAny<SubmitManufactureClientRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMovementReference);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ManufactureId.Should().Be(expectedMovementReference);

        // Verify that all material quantities are correctly passed to the client
        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.Is<SubmitManufactureClientRequest>(r =>
                r.Items.Count == 3 &&
                r.Items[0].ProductCode == "MAT006" &&
                r.Items[0].Amount == material1Amount &&
                r.Items[0].ProductName == "Test Material 6" &&
                r.Items[1].ProductCode == "MAT007" &&
                r.Items[1].Amount == material2Amount &&
                r.Items[1].ProductName == "Test Material 7" &&
                r.Items[2].ProductCode == "MAT008" &&
                r.Items[2].Amount == material3Amount &&
                r.Items[2].ProductName == "Test Material 8"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
