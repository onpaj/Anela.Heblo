using Anela.Heblo.Application.Features.Manufacture.Services;
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
using DiscardResidualSemiProductRequest = Anela.Heblo.Application.Features.Manufacture.UseCases.DiscardResidualSemiProduct.DiscardResidualSemiProductRequest;
using DiscardResidualSemiProductResponse = Anela.Heblo.Application.Features.Manufacture.UseCases.DiscardResidualSemiProduct.DiscardResidualSemiProductResponse;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ManufactureOrderApplicationServiceTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<ManufactureOrderApplicationService>> _loggerMock;
    private readonly Mock<IProductNameFormatter> _productNameFormatterMock;
    private readonly ManufactureOrderApplicationService _service;

    private const int ValidOrderId = 1;
    private const decimal ValidQuantity = 10.5m;
    private const string TestUserName = "Test User";
    private const string ValidChangeReason = "Testing manufacture confirmation";

    public ManufactureOrderApplicationServiceTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _timeProviderMock = new Mock<TimeProvider>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<ManufactureOrderApplicationService>>();
        _productNameFormatterMock = new Mock<IProductNameFormatter>();

        var testTime = DateTime.UtcNow;
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(testTime));

        var testUser = new CurrentUser("test-user-id", TestUserName, "test@example.com", true);
        _currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(testUser);

        _productNameFormatterMock.Setup(x => x.ShortProductName(It.IsAny<string>())).Returns("Short Name");

        _service = new ManufactureOrderApplicationService(
            _mediatorMock.Object,
            _timeProviderMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object,
            _productNameFormatterMock.Object);
    }

    #region ConfirmSemiProductManufactureAsync Tests

    [Fact]
    public async Task ConfirmSemiProductManufactureAsync_SuccessfulFlow_UpdatesStateAndSetsErpData()
    {
        // Arrange
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_ORDER_123");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        // Act & Assert
        try
        {
            var result = await _service.ConfirmSemiProductManufactureAsync(ValidOrderId, ValidQuantity, ValidChangeReason);

            result.Should().NotBeNull();
            if (!result.Success)
            {
                throw new Exception($"Service failed with message: {result.Message}");
            }
            result.Success.Should().BeTrue();
            result.Message.Should().Contain($"skutečným množstvím {ValidQuantity}");

            VerifyUpdateOrderCall();
            VerifySubmitManufactureCall(ManufactureType.SemiProduct);
            VerifyUpdateStatusCall(ManufactureOrderState.SemiProductManufactured, "ERP_ORDER_123", null, null, false);
        }
        catch (Exception ex)
        {
            throw new Exception($"Test failed with exception: {ex.Message}", ex);
        }
    }

    [Fact]
    public async Task ConfirmSemiProductManufactureAsync_ErpFailure_StillUpdatesState_SetsManualActionRequired()
    {
        // Arrange
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateFailedSubmitManufactureResponse("ERP connection failed");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        // Act
        var result = await _service.ConfirmSemiProductManufactureAsync(ValidOrderId, ValidQuantity, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Contain($"skutečným množstvím {ValidQuantity}");

        VerifyUpdateOrderCall();
        VerifySubmitManufactureCall(ManufactureType.SemiProduct);
        VerifyUpdateStatusCall(ManufactureOrderState.SemiProductManufactured, null, null, null, true);
    }

    [Fact]
    public async Task ConfirmSemiProductManufactureAsync_UpdateQuantityFailure_ReturnsError_DoesNotUpdateState()
    {
        // Arrange
        var updateOrderResponse = CreateFailedUpdateOrderResponse("Update failed");

        _mediatorMock.Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        // Act
        var result = await _service.ConfirmSemiProductManufactureAsync(ValidOrderId, ValidQuantity, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Chyba při aktualizaci množství");

        VerifyUpdateOrderCall();
        VerifyNoSubmitManufactureCall();
        VerifyNoUpdateStatusCall();
    }

    [Fact]
    public async Task ConfirmSemiProductManufactureAsync_UpdateStatusFailure_ReturnsError()
    {
        // Arrange
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_ORDER_123");
        var updateStatusResponse = CreateFailedUpdateStatusResponse("Status update failed");

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        // Act
        var result = await _service.ConfirmSemiProductManufactureAsync(ValidOrderId, ValidQuantity, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Chyba při změně stavu");

        VerifyUpdateOrderCall();
        VerifySubmitManufactureCall(ManufactureType.SemiProduct);
        VerifyUpdateStatusCall(ManufactureOrderState.SemiProductManufactured, "ERP_ORDER_123", null, null, false);
    }

    [Fact]
    public async Task ConfirmSemiProductManufactureAsync_UnexpectedException_ReturnsErrorMessage_LogsException()
    {
        // Arrange
        _mediatorMock.Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _service.ConfirmSemiProductManufactureAsync(ValidOrderId, ValidQuantity, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Došlo k neočekávané chybě při potvrzení výroby polotovaru");

        VerifyErrorLogged();
    }

    #endregion

    #region ConfirmProductCompletionAsync Tests

    [Fact]
    public async Task ConfirmProductCompletionAsync_SuccessfulFlow_UpdatesStateAndSetsAllErpData()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m }, { 2, 3.5m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_PRODUCT_456");
        var discardResponse = CreateSuccessfulDiscardResponse("DISCARD_REF_789");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse, discardResponse);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();

        VerifyUpdateProductsCall(productQuantities);
        VerifySubmitManufactureCall(ManufactureType.Product);
        VerifyDiscardResidueCall();
        VerifyUpdateStatusCall(ManufactureOrderState.Completed, null, "ERP_PRODUCT_456", "DISCARD_REF_789", false);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_ErpManufactureFailure_StillUpdatesState_SetsManualActionRequired()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateFailedSubmitManufactureResponse("ERP manufacture failed");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();

        VerifyUpdateProductsCall(productQuantities);
        VerifySubmitManufactureCall(ManufactureType.Product);
        VerifyNoDiscardResidueCall(); // Discard not called when manufacture fails
        VerifyUpdateStatusCall(ManufactureOrderState.Completed, null, null, null, true);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_DiscardResidueFailure_StillUpdatesState_SetsManualActionRequired()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_PRODUCT_456");
        var discardResponse = CreateFailedDiscardResponse("Discard failed");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse, discardResponse);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();

        VerifyUpdateProductsCall(productQuantities);
        VerifySubmitManufactureCall(ManufactureType.Product);
        VerifyDiscardResidueCall();
        VerifyUpdateStatusCall(ManufactureOrderState.Completed, null, "ERP_PRODUCT_456", null, true);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_BothErpAndDiscardFailure_StillUpdatesState_SetsManualActionRequired()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateFailedSubmitManufactureResponse("ERP failed");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();

        VerifyUpdateProductsCall(productQuantities);
        VerifySubmitManufactureCall(ManufactureType.Product);
        VerifyNoDiscardResidueCall();
        VerifyUpdateStatusCall(ManufactureOrderState.Completed, null, null, null, true);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_UpdateQuantityFailure_ReturnsError_DoesNotUpdateState()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateFailedUpdateOrderResponse("Update products failed");

        _mediatorMock.Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Chyba při aktualizaci množství produktů");

        VerifyUpdateProductsCall(productQuantities);
        VerifyNoSubmitManufactureCall();
        VerifyNoDiscardResidueCall();
        VerifyNoUpdateStatusCall();
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_UpdateStatusFailure_ReturnsError()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_PRODUCT_456");
        var discardResponse = CreateSuccessfulDiscardResponse("DISCARD_REF_789");
        var updateStatusResponse = CreateFailedUpdateStatusResponse("Status update failed");

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse, discardResponse);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Chyba při změně stavu");

        VerifyUpdateProductsCall(productQuantities);
        VerifySubmitManufactureCall(ManufactureType.Product);
        VerifyDiscardResidueCall();
        VerifyUpdateStatusCall(ManufactureOrderState.Completed, null, "ERP_PRODUCT_456", "DISCARD_REF_789", false);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_UnexpectedException_ReturnsErrorMessage_LogsException()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        
        _mediatorMock.Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("Došlo k neočekávané chybě při dokončení výroby produktů");

        VerifyErrorLogged();
    }

    #endregion

    #region Helper Methods

    private void SetupMediatorResponses(
        UpdateManufactureOrderResponse updateOrderResponse,
        SubmitManufactureResponse submitManufactureResponse,
        UpdateManufactureOrderStatusResponse updateStatusResponse,
        DiscardResidualSemiProductResponse? discardResponse = null)
    {
        _mediatorMock.Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        _mediatorMock.Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(submitManufactureResponse);

        _mediatorMock.Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateStatusResponse);

        if (discardResponse != null)
        {
            _mediatorMock.Setup(x => x.Send(It.IsAny<DiscardResidualSemiProductRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(discardResponse);
        }
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

    private static UpdateManufactureOrderResponse CreateFailedUpdateOrderResponse(string errorMessage)
    {
        return new UpdateManufactureOrderResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError
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

    private static SubmitManufactureResponse CreateFailedSubmitManufactureResponse(string errorMessage)
    {
        return new SubmitManufactureResponse(ErrorCodes.InternalServerError, new Dictionary<string, string> { { "message", errorMessage } });
    }

    private static UpdateManufactureOrderStatusResponse CreateSuccessfulUpdateStatusResponse()
    {
        return new UpdateManufactureOrderStatusResponse
        {
            Success = true
        };
    }

    private static UpdateManufactureOrderStatusResponse CreateFailedUpdateStatusResponse(string errorMessage)
    {
        return new UpdateManufactureOrderStatusResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError
        };
    }

    private static DiscardResidualSemiProductResponse CreateSuccessfulDiscardResponse(string stockMovementReference)
    {
        return new DiscardResidualSemiProductResponse
        {
            Success = true,
            StockMovementReference = stockMovementReference
        };
    }

    private static DiscardResidualSemiProductResponse CreateFailedDiscardResponse(string errorMessage)
    {
        return new DiscardResidualSemiProductResponse(ErrorCodes.InternalServerError, new Dictionary<string, string> { { "message", errorMessage } });
    }

    private void VerifyUpdateOrderCall()
    {
        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderRequest>(r => 
                r.Id == ValidOrderId && 
                r.SemiProduct != null && 
                r.SemiProduct.ActualQuantity == ValidQuantity),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private void VerifyUpdateProductsCall(Dictionary<int, decimal> productQuantities)
    {
        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderRequest>(r => 
                r.Id == ValidOrderId && 
                r.Products != null &&
                r.Products.Count == productQuantities.Count),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private void VerifySubmitManufactureCall(ManufactureType expectedType)
    {
        _mediatorMock.Verify(x => x.Send(
            It.Is<SubmitManufactureRequest>(r => r.ManufactureType == expectedType),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private void VerifyNoSubmitManufactureCall()
    {
        _mediatorMock.Verify(x => x.Send(
            It.IsAny<SubmitManufactureRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private void VerifyUpdateStatusCall(
        ManufactureOrderState expectedState,
        string? expectedSemiProductCode,
        string? expectedProductCode,
        string? expectedDiscardCode,
        bool expectedManualActionRequired)
    {
        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderStatusRequest>(r => 
                r.Id == ValidOrderId &&
                r.NewState == expectedState &&
                r.SemiProductOrderCode == expectedSemiProductCode &&
                r.ProductOrderCode == expectedProductCode &&
                r.DiscardRedisueDocumentCode == expectedDiscardCode &&
                r.ManualActionRequired == expectedManualActionRequired),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private void VerifyNoUpdateStatusCall()
    {
        _mediatorMock.Verify(x => x.Send(
            It.IsAny<UpdateManufactureOrderStatusRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private void VerifyDiscardResidueCall()
    {
        _mediatorMock.Verify(x => x.Send(
            It.IsAny<DiscardResidualSemiProductRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private void VerifyNoDiscardResidueCall()
    {
        _mediatorMock.Verify(x => x.Send(
            It.IsAny<DiscardResidualSemiProductRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private void VerifyErrorLogged()
    {
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion
}