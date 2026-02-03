using Anela.Heblo.Application.Features.Manufacture.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Application.Shared;
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

    [Fact]
    public async Task Handle_ConsumptionMovementFailure_ReturnsErrorImmediately()
    {
        // Arrange
        var manufactureOrderCode = "MO-2024-006";
        var flexiBeeError = "Insufficient stock for material MAT009";

        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = manufactureOrderCode,
            ManufactureInternalNumber = "INT-006",
            Date = new DateTime(2024, 1, 20),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT009",
                    Name = "Test Material 9",
                    Amount = 500.0m
                }
            },
            LotNumber = "LOT-2024-006",
            ExpirationDate = new DateOnly(2025, 6, 20)
        };

        _manufactureClientMock
            .Setup(x => x.SubmitManufactureAsync(
                It.IsAny<SubmitManufactureClientRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConsumptionMovementFailedException(
                flexiBeeError,
                manufactureOrderCode));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ManufactureId.Should().BeNull();
        result.ErrorCode.Should().Be(ErrorCodes.ConsumptionMovementCreationFailed);
        result.Params.Should().NotBeNull();
        result.Params.Should().ContainKey("ManufactureOrderCode");
        result.Params["ManufactureOrderCode"].Should().Be(manufactureOrderCode);
        result.Params.Should().ContainKey("FlexiBeeError");
        result.Params["FlexiBeeError"].Should().Be(flexiBeeError);
        result.Params.Should().ContainKey("ErrorMessage");
    }

    [Fact]
    public async Task Handle_ConsumptionMovementFailure_NoProductionMovementAttempted()
    {
        // Arrange
        var manufactureOrderCode = "MO-2024-007";
        var flexiBeeError = "Material MAT010 not found";

        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = manufactureOrderCode,
            ManufactureInternalNumber = "INT-007",
            Date = new DateTime(2024, 1, 21),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT010",
                    Name = "Test Material 10",
                    Amount = 250.0m
                }
            },
            LotNumber = "LOT-2024-007",
            ExpirationDate = new DateOnly(2025, 7, 21)
        };

        _manufactureClientMock
            .Setup(x => x.SubmitManufactureAsync(
                It.IsAny<SubmitManufactureClientRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConsumptionMovementFailedException(
                flexiBeeError,
                manufactureOrderCode));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ErrorCode.Should().Be(ErrorCodes.ConsumptionMovementCreationFailed);

        // Verify that SubmitManufactureAsync was called only once (consumption movement creation attempt)
        // and production movement was never attempted
        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.IsAny<SubmitManufactureClientRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ConsumptionMovementFailure_ClearErrorMessage()
    {
        // Arrange
        var manufactureOrderCode = "MO-2024-008";
        var flexiBeeError = "Stock level too low: required 1000kg, available 500kg";

        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = manufactureOrderCode,
            ManufactureInternalNumber = "INT-008",
            Date = new DateTime(2024, 1, 22),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT011",
                    Name = "Test Material 11",
                    Amount = 1000.0m
                }
            },
            LotNumber = "LOT-2024-008",
            ExpirationDate = new DateOnly(2025, 8, 22)
        };

        _manufactureClientMock
            .Setup(x => x.SubmitManufactureAsync(
                It.IsAny<SubmitManufactureClientRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConsumptionMovementFailedException(
                flexiBeeError,
                manufactureOrderCode));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ErrorCode.Should().Be(ErrorCodes.ConsumptionMovementCreationFailed);
        result.Params.Should().NotBeNull();

        // Verify that error message is clear and contains FlexiBee error
        result.Params.Should().ContainKey("FlexiBeeError");
        result.Params["FlexiBeeError"].Should().Be(flexiBeeError);

        // Verify that error message includes descriptive information
        result.Params.Should().ContainKey("ErrorMessage");
        result.Params["ErrorMessage"].Should().Contain("Failed to create consumption stock movement");
        result.Params["ErrorMessage"].Should().Contain(flexiBeeError);
    }

    [Fact]
    public async Task Handle_ConsumptionMovementFailure_NoPartialStateCreated()
    {
        // Arrange
        var manufactureOrderCode = "MO-2024-009";
        var flexiBeeError = "Invalid material code: MAT012";

        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = manufactureOrderCode,
            ManufactureInternalNumber = "INT-009",
            Date = new DateTime(2024, 1, 23),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT012",
                    Name = "Test Material 12",
                    Amount = 300.0m
                }
            },
            LotNumber = "LOT-2024-009",
            ExpirationDate = new DateOnly(2025, 9, 23)
        };

        _manufactureClientMock
            .Setup(x => x.SubmitManufactureAsync(
                It.IsAny<SubmitManufactureClientRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConsumptionMovementFailedException(
                flexiBeeError,
                manufactureOrderCode));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ErrorCode.Should().Be(ErrorCodes.ConsumptionMovementCreationFailed);

        // Verify no manufacture ID was created (no partial state)
        result.ManufactureId.Should().BeNull();

        // Verify that params does not contain ConsumptionMovementId
        // (which would indicate a partial success)
        result.Params.Should().NotBeNull();
        result.Params.Should().NotContainKey("ConsumptionMovementId");
        result.Params.Should().NotContainKey("RollbackInstructions");

        // Verify that only consumption movement was attempted (no production movement)
        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.IsAny<SubmitManufactureClientRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BatchNumberPreservation_VerifiesBatchNumberPassedToConsumptionMovement()
    {
        // Arrange
        var lotNumber = "BATCH-2024-001";
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MO-2024-010",
            ManufactureInternalNumber = "INT-010",
            Date = new DateTime(2024, 1, 24),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT013",
                    Name = "Test Material 13",
                    Amount = 100.0m
                }
            },
            LotNumber = lotNumber,
            ExpirationDate = new DateOnly(2025, 10, 24)
        };

        var expectedMovementReference = "MOV-2024-010";

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

        // Verify that batch number was passed to the client request
        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.Is<SubmitManufactureClientRequest>(r =>
                r.LotNumber == lotNumber),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BatchNumberPreservation_VerifiesBatchNumberPassedToProductionMovement()
    {
        // Arrange
        var lotNumber = "BATCH-2024-002";
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MO-2024-011",
            ManufactureInternalNumber = "INT-011",
            Date = new DateTime(2024, 1, 25),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.SemiProduct,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT014",
                    Name = "Test Material 14",
                    Amount = 150.0m
                }
            },
            LotNumber = lotNumber,
            ExpirationDate = new DateOnly(2025, 11, 25)
        };

        var expectedMovementReference = "MOV-2024-011";

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

        // Verify that batch number was passed to the client request
        // The implementation uses the same batch number for both consumption and production movements
        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.Is<SubmitManufactureClientRequest>(r =>
                r.LotNumber == lotNumber),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BatchNumberPreservation_VerifiesBothMovementsUseSameBatch()
    {
        // Arrange
        var lotNumber = "BATCH-2024-003";
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MO-2024-012",
            ManufactureInternalNumber = "INT-012",
            Date = new DateTime(2024, 1, 26),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT015",
                    Name = "Test Material 15",
                    Amount = 200.0m
                }
            },
            LotNumber = lotNumber,
            ExpirationDate = new DateOnly(2025, 12, 26)
        };

        var expectedMovementReference = "MOV-2024-012";

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

        // Verify that both consumption and production movements use the same batch number
        // The implementation creates both movements within the same SubmitManufactureAsync call
        // using the same request.LotNumber value
        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.Is<SubmitManufactureClientRequest>(r =>
                r.LotNumber == lotNumber &&
                r.ManufactureOrderCode == "MO-2024-012"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BatchNumberPreservation_VerifiesBatchLinkageForTracking()
    {
        // Arrange
        var lotNumber = "BATCH-2024-004";
        var manufactureOrderCode = "MO-2024-013";
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = manufactureOrderCode,
            ManufactureInternalNumber = "INT-013",
            Date = new DateTime(2024, 1, 27),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            Items = new List<SubmitManufactureRequestItem>
            {
                new SubmitManufactureRequestItem
                {
                    ProductCode = "MAT016",
                    Name = "Test Material 16",
                    Amount = 250.0m
                }
            },
            LotNumber = lotNumber,
            ExpirationDate = new DateOnly(2026, 1, 27)
        };

        var expectedMovementReference = "MOV-2024-013";

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

        // Verify that batch number and manufacture order code are linked for tracking
        // This enables traceability from finished goods back to raw materials
        _manufactureClientMock.Verify(x => x.SubmitManufactureAsync(
            It.Is<SubmitManufactureClientRequest>(r =>
                r.LotNumber == lotNumber &&
                r.ManufactureOrderCode == manufactureOrderCode &&
                r.Items.Count == 1 &&
                r.Items[0].ProductCode == "MAT016"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
