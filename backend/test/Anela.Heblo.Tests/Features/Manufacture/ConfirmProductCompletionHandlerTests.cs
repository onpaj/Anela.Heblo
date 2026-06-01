using Anela.Heblo.Application.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmProductCompletion;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ConfirmProductCompletionHandlerTests
{
    private readonly Mock<IConfirmProductCompletionWorkflow> _workflowMock = new();
    private readonly Mock<ILogger<ConfirmProductCompletionHandler>> _loggerMock = new();
    private readonly IMapper _mapper;
    private readonly ConfirmProductCompletionHandler _handler;

    public ConfirmProductCompletionHandlerTests()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<ManufactureOrderMappingProfile>();
        }, NullLoggerFactory.Instance);
        _mapper = config.CreateMapper();
        _handler = new ConfirmProductCompletionHandler(_workflowMock.Object, _mapper, _loggerMock.Object);
    }

    private static ConfirmProductCompletionRequest BuildRequest() => new()
    {
        Id = 1,
        Products = new List<ProductActualQuantityRequest>
        {
            new() { Id = 10, ActualQuantity = 5m },
            new() { Id = 20, ActualQuantity = 7m },
        },
        OverrideConfirmed = false,
        ChangeReason = "test reason",
    };

    [Fact]
    public async Task Handle_WorkflowSuccess_ReturnsSuccessfulEmptyResponse()
    {
        // Arrange
        var request = BuildRequest();
        _workflowMock
            .Setup(w => w.ExecuteAsync(
                1,
                It.Is<Dictionary<int, decimal>>(d => d.Count == 2 && d[10] == 5m && d[20] == 7m),
                false,
                "test reason",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfirmProductCompletionResult());

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.RequiresConfirmation.Should().BeFalse();
        response.Distribution.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WorkflowRequiresConfirmation_ReturnsSuccessfulResponseWithMappedDistribution()
    {
        // Arrange
        var request = BuildRequest();
        var distribution = new ResidueDistribution
        {
            ActualSemiProductQuantity = 15m,
            TheoreticalConsumption = 12m,
            Difference = 3m,
            DifferencePercentage = 25.0,
            IsWithinAllowedThreshold = false,
            AllowedResiduePercentage = 5.0,
            Products = new List<ProductConsumptionDistribution>
            {
                new()
                {
                    ProductCode = "PROD-A",
                    ProductName = "Product A",
                    ActualPieces = 100m,
                    TheoreticalGramsPerUnit = 0.12m,
                    TheoreticalConsumption = 12m,
                    AdjustedConsumption = 15m,
                    AdjustedGramsPerUnit = 0.15m,
                    ProportionRatio = 1.0,
                },
            },
        };
        _workflowMock
            .Setup(w => w.ExecuteAsync(
                It.IsAny<int>(),
                It.IsAny<Dictionary<int, decimal>>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConfirmProductCompletionResult.NeedsConfirmation(distribution));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.RequiresConfirmation.Should().BeTrue();
        response.Distribution.Should().NotBeNull();
        response.Distribution!.ActualSemiProductQuantity.Should().Be(15m);
        response.Distribution.IsWithinAllowedThreshold.Should().BeFalse();
        response.Distribution.Products.Should().HaveCount(1);
        response.Distribution.Products[0].ProductCode.Should().Be("PROD-A");
        response.Distribution.Products[0].AdjustedGramsPerUnit.Should().Be(0.15m);
    }

    [Fact]
    public async Task Handle_WorkflowFailure_ReturnsBadRequestResponseWithErrorMessage()
    {
        // Arrange
        var request = BuildRequest();
        _workflowMock
            .Setup(w => w.ExecuteAsync(
                It.IsAny<int>(),
                It.IsAny<Dictionary<int, decimal>>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfirmProductCompletionResult("Chyba při aktualizaci množství produktů: InternalServerError"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        response.Message.Should().Be("Chyba při aktualizaci množství produktů: InternalServerError");
        response.RequiresConfirmation.Should().BeFalse();
        response.Distribution.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WorkflowThrowsException_ReturnsInternalServerErrorResponse()
    {
        // Arrange
        var request = BuildRequest();
        _workflowMock
            .Setup(w => w.ExecuteAsync(
                It.IsAny<int>(),
                It.IsAny<Dictionary<int, decimal>>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        response.Message.Should().Be("Došlo k neočekávané chybě při dokončení výroby produktů");
    }

    [Fact]
    public async Task Handle_PassesOverrideConfirmedAndChangeReasonToWorkflow()
    {
        // Arrange
        var request = BuildRequest();
        request.OverrideConfirmed = true;
        request.ChangeReason = "override";
        _workflowMock
            .Setup(w => w.ExecuteAsync(1, It.IsAny<Dictionary<int, decimal>>(), true, "override", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfirmProductCompletionResult());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _workflowMock.Verify(
            w => w.ExecuteAsync(1, It.IsAny<Dictionary<int, decimal>>(), true, "override", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
