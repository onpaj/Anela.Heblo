using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class SubmitManufactureHandlerBatchNumberTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IManufactureClient> _clientMock;
    private readonly Mock<ILogger<SubmitManufactureHandler>> _loggerMock;
    private readonly SubmitManufactureHandler _handler;

    public SubmitManufactureHandlerBatchNumberTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _clientMock = new Mock<IManufactureClient>();
        _loggerMock = new Mock<ILogger<SubmitManufactureHandler>>();

        _handler = new SubmitManufactureHandler(
            _repositoryMock.Object,
            _clientMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithBatchNumber_PassesBatchNumberToManufactureClient()
    {
        // Arrange
        const string expectedBatchNumber = "BATCH-2026-001";
        var expirationDate = new DateOnly(2027, 12, 31);

        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MFG-001",
            ManufactureInternalNumber = "INT-001",
            Date = DateTime.UtcNow,
            CreatedBy = "test@example.com",
            ManufactureType = ErpManufactureType.Product,
            LotNumber = expectedBatchNumber,
            ExpirationDate = expirationDate,
            Items = new List<SubmitManufactureRequestItem>
            {
                new()
                {
                    ProductCode = "MAT-001",
                    Name = "Material 1",
                    Amount = 10
                }
            }
        };

        SubmitManufactureClientRequest? capturedClientRequest = null;
        _clientMock
            .Setup(x => x.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SubmitManufactureClientRequest, CancellationToken>((req, ct) => capturedClientRequest = req)
            .ReturnsAsync("DOC-123");

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        capturedClientRequest.Should().NotBeNull("client request should be created");
        capturedClientRequest!.LotNumber.Should().Be(expectedBatchNumber,
            "batch number should be passed to manufacture client for consumption movement");
        capturedClientRequest.ExpirationDate.Should().Be(expirationDate,
            "expiration date should be passed to manufacture client");
    }

    [Fact]
    public async Task Handle_WithDifferentBatchNumbers_PassesCorrectBatchToClient()
    {
        // Arrange
        const string batch1 = "BATCH-A-001";
        const string batch2 = "BATCH-B-002";
        var expiration1 = new DateOnly(2027, 6, 30);
        var expiration2 = new DateOnly(2028, 12, 31);

        var request1 = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MFG-001",
            ManufactureInternalNumber = "INT-001",
            Date = DateTime.UtcNow,
            CreatedBy = "test@example.com",
            ManufactureType = ErpManufactureType.Product,
            LotNumber = batch1,
            ExpirationDate = expiration1,
            Items = new List<SubmitManufactureRequestItem>
            {
                new() { ProductCode = "PRD-001", Name = "Product 1", Amount = 5 }
            }
        };

        var request2 = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MFG-002",
            ManufactureInternalNumber = "INT-002",
            Date = DateTime.UtcNow,
            CreatedBy = "test@example.com",
            ManufactureType = ErpManufactureType.SemiProduct,
            LotNumber = batch2,
            ExpirationDate = expiration2,
            Items = new List<SubmitManufactureRequestItem>
            {
                new() { ProductCode = "SEMI-001", Name = "Semi Product 1", Amount = 10 }
            }
        };

        var capturedRequests = new List<SubmitManufactureClientRequest>();
        _clientMock
            .Setup(x => x.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SubmitManufactureClientRequest, CancellationToken>((req, ct) => capturedRequests.Add(req))
            .ReturnsAsync("DOC-123");

        // Act
        await _handler.Handle(request1, CancellationToken.None);
        await _handler.Handle(request2, CancellationToken.None);

        // Assert
        capturedRequests.Should().HaveCount(2, "two manufacture requests should be processed");

        var firstRequest = capturedRequests[0];
        firstRequest.LotNumber.Should().Be(batch1, "first request should have batch1");
        firstRequest.ExpirationDate.Should().Be(expiration1, "first request should have expiration1");

        var secondRequest = capturedRequests[1];
        secondRequest.LotNumber.Should().Be(batch2, "second request should have batch2");
        secondRequest.ExpirationDate.Should().Be(expiration2, "second request should have expiration2");
    }

    [Fact]
    public async Task Handle_WithNullBatchNumber_PassesNullToClient()
    {
        // Arrange
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MFG-003",
            ManufactureInternalNumber = "INT-003",
            Date = DateTime.UtcNow,
            CreatedBy = "test@example.com",
            ManufactureType = ErpManufactureType.Product,
            LotNumber = null, // No batch number
            ExpirationDate = null, // No expiration date
            Items = new List<SubmitManufactureRequestItem>
            {
                new() { ProductCode = "PRD-002", Name = "Product 2", Amount = 15 }
            }
        };

        SubmitManufactureClientRequest? capturedClientRequest = null;
        _clientMock
            .Setup(x => x.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SubmitManufactureClientRequest, CancellationToken>((req, ct) => capturedClientRequest = req)
            .ReturnsAsync("DOC-456");

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        capturedClientRequest.Should().NotBeNull();
        capturedClientRequest!.LotNumber.Should().BeNull("null batch number should be preserved");
        capturedClientRequest.ExpirationDate.Should().BeNull("null expiration date should be preserved");
    }

    [Fact]
    public async Task Handle_BatchNumberLinkage_SameBatchUsedForConsumptionAndProduction()
    {
        // Arrange
        const string batchNumber = "BATCH-2026-LINKED";
        var expirationDate = new DateOnly(2027, 9, 15);

        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = "MFG-LINK-001",
            ManufactureInternalNumber = "INT-LINK-001",
            Date = DateTime.UtcNow,
            CreatedBy = "test@example.com",
            ManufactureType = ErpManufactureType.SemiProduct,
            LotNumber = batchNumber,
            ExpirationDate = expirationDate,
            Items = new List<SubmitManufactureRequestItem>
            {
                new() { ProductCode = "SEMI-002", Name = "Semi Product 2", Amount = 25 }
            }
        };

        SubmitManufactureClientRequest? capturedClientRequest = null;
        _clientMock
            .Setup(x => x.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SubmitManufactureClientRequest, CancellationToken>((req, ct) => capturedClientRequest = req)
            .ReturnsAsync("DOC-789");

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert - Verify that the batch number is passed through
        capturedClientRequest.Should().NotBeNull();
        capturedClientRequest!.LotNumber.Should().Be(batchNumber,
            "same batch number should be used for both consumption and production movements (verified by client implementation)");
        capturedClientRequest.ExpirationDate.Should().Be(expirationDate,
            "same expiration date should be used for both movements (verified by client implementation)");

        // Verify the client was called exactly once with the batch information
        _clientMock.Verify(
            x => x.SubmitManufactureAsync(
                It.Is<SubmitManufactureClientRequest>(r =>
                    r.LotNumber == batchNumber &&
                    r.ExpirationDate == expirationDate),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "client should be called once with the correct batch number and expiration date that will be used for both movements");
    }
}
