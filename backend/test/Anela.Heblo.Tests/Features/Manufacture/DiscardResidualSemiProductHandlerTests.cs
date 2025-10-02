using Anela.Heblo.Application.Features.Manufacture.UseCases.DiscardResidualSemiProduct;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ApplicationRequest = Anela.Heblo.Application.Features.Manufacture.UseCases.DiscardResidualSemiProduct.DiscardResidualSemiProductRequest;
using DomainRequest = Anela.Heblo.Domain.Features.Manufacture.DiscardResidualSemiProductRequest;
using DomainResponse = Anela.Heblo.Domain.Features.Manufacture.DiscardResidualSemiProductResponse;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class DiscardResidualSemiProductHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<ILogger<DiscardResidualSemiProductHandler>> _loggerMock;
    private readonly DiscardResidualSemiProductHandler _handler;

    public DiscardResidualSemiProductHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _manufactureClientMock = new Mock<IManufactureClient>();
        _loggerMock = new Mock<ILogger<DiscardResidualSemiProductHandler>>();
        _handler = new DiscardResidualSemiProductHandler(_catalogRepositoryMock.Object, _manufactureClientMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_SuccessfulDiscard_ReturnsSuccessResponse()
    {
        // Arrange
        var request = new ApplicationRequest
        {
            ManufactureOrderCode = "TEST001",
            ProductCode = "SP001001",
            ProductName = "Test Semi Product",
            CompletionDate = new DateTime(2024, 1, 15, 14, 30, 0),
            CompletedBy = "TestUser"
        };

        var product = new CatalogAggregate
        {
            ProductCode = "SP001001",
            ProductName = "Test Semi Product",
            Properties = new CatalogProperties
            {
                AllowedResiduePercentage = 10.0
            }
        };

        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(It.Is<string>(id => id == "SP001001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var clientResponse = new DomainResponse
        {
            Success = true,
            QuantityFound = 5.0,
            QuantityDiscarded = 5.0,
            RequiresManualApproval = false,
            StockMovementReference = "MOV_123",
            Details = "Successfully auto-discarded 5 units"
        };

        _manufactureClientMock
            .Setup(x => x.DiscardResidualSemiProductAsync(It.IsAny<DomainRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.QuantityFound.Should().Be(5.0);
        result.QuantityDiscarded.Should().Be(5.0);
        result.RequiresManualApproval.Should().BeFalse();
        result.StockMovementReference.Should().Be("MOV_123");
        result.Details.Should().Be("Successfully auto-discarded 5 units");

        _manufactureClientMock.Verify(x => x.DiscardResidualSemiProductAsync(
            It.Is<DomainRequest>(r => 
                r.ManufactureOrderCode == "TEST001" && 
                r.ProductCode == "SP001001" &&
                r.ProductName == "Test Semi Product"), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RequiresManualApproval_ReturnsResponseWithManualApprovalFlag()
    {
        // Arrange
        var request = new ApplicationRequest
        {
            ManufactureOrderCode = "TEST002",
            ProductCode = "SP002",
            CompletionDate = new DateTime(2024, 1, 15, 14, 30, 0)
        };

        var product = new CatalogAggregate
        {
            ProductCode = "SP002",
            Properties = new CatalogProperties
            {
                AllowedResiduePercentage = 5.0
            }
        };

        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(It.Is<string>(id => id == "SP002"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var clientResponse = new DomainResponse
        {
            Success = true,
            QuantityFound = 10.0,
            QuantityDiscarded = 0.0,
            RequiresManualApproval = true,
            Details = "Quantity exceeds auto-discard limit - manual approval required"
        };

        _manufactureClientMock
            .Setup(x => x.DiscardResidualSemiProductAsync(It.IsAny<DomainRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.QuantityFound.Should().Be(10.0);
        result.QuantityDiscarded.Should().Be(0.0);
        result.RequiresManualApproval.Should().BeTrue();
        result.Details.Should().Contain("manual approval required");
    }

    [Fact]
    public async Task Handle_ClientThrowsException_ReturnsErrorResponse()
    {
        // Arrange
        var request = new ApplicationRequest
        {
            ManufactureOrderCode = "TEST003",
            ProductCode = "SP003",
            CompletionDate = new DateTime(2024, 1, 15, 14, 30, 0)
        };

        var product = new CatalogAggregate
        {
            ProductCode = "SP003",
            Properties = new CatalogProperties
            {
                AllowedResiduePercentage = 10.0
            }
        };

        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(It.Is<string>(id => id == "SP003"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        _manufactureClientMock
            .Setup(x => x.DiscardResidualSemiProductAsync(It.IsAny<DomainRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service unavailable"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().NotBeNull();
        result.Params.Should().ContainKey("ErrorMessage");
        result.Params!["ErrorMessage"].Should().Contain("Service unavailable");
    }

    [Fact]
    public async Task Handle_ClientReturnsFailure_ReturnsSuccessResponseWithDetails()
    {
        // Arrange
        var request = new ApplicationRequest
        {
            ManufactureOrderCode = "TEST004",
            ProductCode = "SP004",
            CompletionDate = new DateTime(2024, 1, 15, 14, 30, 0)
        };

        var product = new CatalogAggregate
        {
            ProductCode = "SP004",
            Properties = new CatalogProperties
            {
                AllowedResiduePercentage = 10.0
            }
        };

        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(It.Is<string>(id => id == "SP004"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var clientResponse = new DomainResponse
        {
            Success = false,
            QuantityFound = 0.0,
            QuantityDiscarded = 0.0,
            RequiresManualApproval = false,
            ErrorMessage = "Stock service temporarily unavailable"
        };

        _manufactureClientMock
            .Setup(x => x.DiscardResidualSemiProductAsync(It.IsAny<DomainRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse(); // Handler always returns success, business logic handles the failure
        result.QuantityFound.Should().Be(0.0);
        result.QuantityDiscarded.Should().Be(0.0);
    }

    [Fact]
    public async Task Handle_ProductNotFound_ReturnsErrorResponse()
    {
        // Arrange
        var request = new ApplicationRequest
        {
            ManufactureOrderCode = "TEST005",
            ProductCode = "SP005",
            CompletionDate = new DateTime(2024, 1, 15, 14, 30, 0)
        };

        _catalogRepositoryMock
            .Setup(x => x.GetByIdAsync(It.Is<string>(id => id == "SP005"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().NotBeNull();

        // Verify that the manufacture client was not called when product is not found
        _manufactureClientMock.Verify(x => x.DiscardResidualSemiProductAsync(
            It.IsAny<DomainRequest>(), 
            It.IsAny<CancellationToken>()), Times.Never);
    }
}