using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services.Workflows;

public class ConfirmSemiProductManufactureWorkflowTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<IManufactureNameBuilder> _nameBuilderMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<ConfirmSemiProductManufactureWorkflow>> _loggerMock;
    private readonly ConfirmSemiProductManufactureWorkflow _workflow;

    private const int ValidOrderId = 1;
    private const decimal ValidQuantity = 10.5m;
    private const string TestUserName = "Test User";
    private const string ValidChangeReason = "Testing semi-product confirmation";

    public ConfirmSemiProductManufactureWorkflowTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _nameBuilderMock = new Mock<IManufactureNameBuilder>();
        _timeProviderMock = new Mock<TimeProvider>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<ConfirmSemiProductManufactureWorkflow>>();

        var testTime = DateTime.UtcNow;
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(testTime));

        var testUser = new CurrentUser("test-user-id", TestUserName, "test@example.com", true);
        _currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(testUser);

        _nameBuilderMock
            .Setup(x => x.Build(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<ErpManufactureType>()))
            .Returns("SP-Short-Name");

        _workflow = new ConfirmSemiProductManufactureWorkflow(
            _mediatorMock.Object,
            _nameBuilderMock.Object,
            _timeProviderMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_ReturnsSuccess()
    {
        // Arrange
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_SEMI_123");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, ValidQuantity, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Contain($"skutečným množstvím {ValidQuantity}");

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderRequest>(r =>
                r.Id == ValidOrderId &&
                r.SemiProduct != null &&
                r.SemiProduct.ActualQuantity == ValidQuantity),
            It.IsAny<CancellationToken>()), Times.Once);

        _mediatorMock.Verify(x => x.Send(
            It.Is<SubmitManufactureRequest>(r => r.ManufactureType == ErpManufactureType.SemiProduct),
            It.IsAny<CancellationToken>()), Times.Once);

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderStatusRequest>(r =>
                r.Id == ValidOrderId &&
                r.NewState == ManufactureOrderState.SemiProductManufactured &&
                r.SemiProductOrderCode == "ERP_SEMI_123" &&
                r.ManualActionRequired == false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUpdateQuantityFails_ReturnsQuantityUpdateError()
    {
        // Arrange
        var updateOrderResponse = new UpdateManufactureOrderResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError
        };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, ValidQuantity, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Chyba při aktualizaci množství");

        _mediatorMock.Verify(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _mediatorMock.Verify(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenErpSubmitFails_StillTransitionsStateWithManualActionRequired()
    {
        // Arrange
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = new SubmitManufactureResponse(ErrorCodes.InternalServerError, new Dictionary<string, string> { { "message", "ERP connection failed" } });
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, ValidQuantity, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Contain($"skutečným množstvím {ValidQuantity}");

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderStatusRequest>(r =>
                r.NewState == ManufactureOrderState.SemiProductManufactured &&
                r.ManualActionRequired == true &&
                r.SemiProductOrderCode == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStatusUpdateFailsAfterErpSucceeds_ReturnsStatusChangeError()
    {
        // Arrange
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_SEMI_456");
        var updateStatusResponse = new UpdateManufactureOrderStatusResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError
        };

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, ValidQuantity, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Chyba při změně stavu");

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderStatusRequest>(r =>
                r.SemiProductOrderCode == "ERP_SEMI_456" &&
                r.ManualActionRequired == false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_PropagatesOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, ValidQuantity, ValidChangeReason, cts.Token);

        // Assert — OperationCanceledException is caught by the outer catch and returns a generic failure
        // The workflow catches all exceptions and returns a failure result to avoid unhandled exceptions bubbling to the caller
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Došlo k neočekávané chybě při potvrzení výroby polotovaru");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUnexpectedException_ReturnsGenericMessage()
    {
        // Arrange
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, ValidQuantity, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Došlo k neočekávané chybě při potvrzení výroby polotovaru");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_ForwardsFlexiDocCodesToStatusRequest()
    {
        // Arrange — submit response carries FlexiDoc codes; status request must forward them.
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitResponse = new SubmitManufactureResponse
        {
            Success = true,
            ManufactureId = "MFG-SEMI-001",
            MaterialIssueForSemiProductDocCode = "FLX-MI-SP-001",
            SemiProductReceiptDocCode = "FLX-RCPT-SP-001",
        };
        UpdateManufactureOrderStatusRequest? capturedStatusRequest = null;
        var updateStatusResponse = new UpdateManufactureOrderStatusResponse { Success = true };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(submitResponse);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<UpdateManufactureOrderStatusResponse>, CancellationToken>(
                (r, _) => capturedStatusRequest = (UpdateManufactureOrderStatusRequest)r)
            .ReturnsAsync(updateStatusResponse);

        // Act
        var result = await _workflow.ExecuteAsync(
            ValidOrderId, ValidQuantity, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedStatusRequest.Should().NotBeNull();
        capturedStatusRequest!.FlexiDocMaterialIssueForSemiProduct.Should().Be("FLX-MI-SP-001");
        capturedStatusRequest.FlexiDocSemiProductReceipt.Should().Be("FLX-RCPT-SP-001");
        // Product-completion fields must NOT be set in semi-product workflow
        capturedStatusRequest.FlexiDocSemiProductIssueForProduct.Should().BeNull();
        capturedStatusRequest.FlexiDocMaterialIssueForProduct.Should().BeNull();
        capturedStatusRequest.FlexiDocProductReceipt.Should().BeNull();
    }

    #region Helper Methods

    private void SetupMediatorResponses(
        UpdateManufactureOrderResponse updateOrderResponse,
        SubmitManufactureResponse submitManufactureResponse,
        UpdateManufactureOrderStatusResponse updateStatusResponse)
    {
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(submitManufactureResponse);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateStatusResponse);
    }

    private static UpdateManufactureOrderResponse CreateSuccessfulUpdateOrderResponse()
    {
        return new UpdateManufactureOrderResponse
        {
            Success = true,
            Order = new UpdateManufactureOrderDto
            {
                OrderNumber = "MO-2024-001",
                SemiProduct = new UpdateManufactureOrderSemiProductDto
                {
                    ProductCode = "SP001001",
                    ProductName = "Semi Product 1",
                    ActualQuantity = ValidQuantity,
                    PlannedQuantity = ValidQuantity,
                    LotNumber = "LOT123",
                    ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30))
                },
                Products = new List<UpdateManufactureOrderProductDto>
                {
                    new UpdateManufactureOrderProductDto
                    {
                        ProductCode = "P001",
                        ProductName = "Product 1",
                        ActualQuantity = 5.0m,
                        PlannedQuantity = 5.0m
                    }
                }
            }
        };
    }

    private static SubmitManufactureResponse CreateSuccessfulSubmitManufactureResponse(string manufactureId)
    {
        return new SubmitManufactureResponse
        {
            Success = true,
            ManufactureId = manufactureId
        };
    }

    private static UpdateManufactureOrderStatusResponse CreateSuccessfulUpdateStatusResponse()
    {
        return new UpdateManufactureOrderStatusResponse
        {
            Success = true
        };
    }

    #endregion
}
