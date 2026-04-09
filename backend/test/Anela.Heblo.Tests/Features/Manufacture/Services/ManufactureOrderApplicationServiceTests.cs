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

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ManufactureOrderApplicationServiceTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<IResidueDistributionCalculator> _residueCalculatorMock;
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
        _manufactureClientMock = new Mock<IManufactureClient>();
        _residueCalculatorMock = new Mock<IResidueDistributionCalculator>();
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
            _manufactureClientMock.Object,
            _residueCalculatorMock.Object,
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
            VerifySubmitManufactureCall(ErpManufactureType.SemiProduct);
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
        VerifySubmitManufactureCall(ErpManufactureType.SemiProduct);
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
    public async Task CreateManufactureOrderInErp_WhenSubmitFails_DoesNotLogSuccess()
    {
        // Arrange
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateFailedSubmitManufactureResponse("ERP connection failed");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        // Act
        await _service.ConfirmSemiProductManufactureAsync(ValidOrderId, ValidQuantity, ValidChangeReason);

        // Assert: LogInformation with "Successfully created manufacture" must NEVER fire when submit failed
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Successfully created manufacture")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "Success log must not fire when ERP submit failed");
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
        VerifySubmitManufactureCall(ErpManufactureType.SemiProduct);
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
    public async Task ConfirmProductCompletionAsync_WithinThreshold_AutoProceeds_SubmitsAndUpdatesBoM()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m }, { 2, 3.5m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_PRODUCT_456");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();
        var distribution = CreateDistributionWithinThreshold();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, overrideConfirmed: false, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();

        VerifyUpdateProductsCall(productQuantities);
        VerifySubmitManufactureCall(ErpManufactureType.Product);
        VerifyUpdateStatusCall(ManufactureOrderState.Completed, null, "ERP_PRODUCT_456", null, false,
            expectedWeightWithinTolerance: true, expectedWeightDifference: 1m);
        VerifyBoMUpdateCalledForEachProduct(distribution);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_OutsideThreshold_NotConfirmed_ReturnsNeedsConfirmation()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var distribution = CreateDistributionOutsideThreshold();

        _mediatorMock.Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, overrideConfirmed: false, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
        result.Distribution.Should().NotBeNull();
        result.Distribution!.IsWithinAllowedThreshold.Should().BeFalse();

        VerifyNoSubmitManufactureCall();
        VerifyNoUpdateStatusCall();
        _manufactureClientMock.Verify(
            x => x.UpdateBoMIngredientAmountAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_OutsideThreshold_OverrideConfirmed_Proceeds()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_PRODUCT_789");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();
        var distribution = CreateDistributionOutsideThreshold();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, overrideConfirmed: true, ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();

        VerifySubmitManufactureCall(ErpManufactureType.Product);
        VerifyUpdateStatusCall(ManufactureOrderState.Completed, null, "ERP_PRODUCT_789", null, false,
            expectedWeightWithinTolerance: false, expectedWeightDifference: 20m);
        VerifyBoMUpdateCalledForEachProduct(distribution);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_ErpManufactureFailure_StillUpdatesState_SetsManualActionRequired()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateFailedSubmitManufactureResponse("ERP manufacture failed");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();
        var distribution = CreateDistributionWithinThreshold();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, changeReason: ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();

        VerifyUpdateProductsCall(productQuantities);
        VerifySubmitManufactureCall(ErpManufactureType.Product);
        VerifyUpdateStatusCall(ManufactureOrderState.Completed, null, null, null, true,
            expectedWeightWithinTolerance: true, expectedWeightDifference: 1m);
        _manufactureClientMock.Verify(
            x => x.UpdateBoMIngredientAmountAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, changeReason: ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Chyba při aktualizaci množství produktů");

        VerifyUpdateProductsCall(productQuantities);
        VerifyNoSubmitManufactureCall();
        VerifyNoUpdateStatusCall();
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_UpdateStatusFailure_ReturnsError()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_PRODUCT_456");
        var updateStatusResponse = CreateFailedUpdateStatusResponse("Status update failed");
        var distribution = CreateDistributionWithinThreshold();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, changeReason: ValidChangeReason);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Chyba při změně stavu");

        VerifyUpdateProductsCall(productQuantities);
        VerifySubmitManufactureCall(ErpManufactureType.Product);
        VerifyUpdateStatusCall(ManufactureOrderState.Completed, null, "ERP_PRODUCT_456", null, false,
            expectedWeightWithinTolerance: true, expectedWeightDifference: 1m);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_OutsideThreshold_OverrideConfirmed_AddsWeightToleranceNote()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_PRODUCT_789");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();
        var distribution = CreateDistributionOutsideThreshold(); // DifferencePercentage=25.0, AllowedResiduePercentage=5.0

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, overrideConfirmed: true, ValidChangeReason);

        // Assert
        result.Success.Should().BeTrue();
        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderStatusRequest>(r =>
                r.Note != null &&
                r.Note.Contains("mimo toleranci") &&
                r.Note.Contains("25.00%") &&
                r.Note.Contains("5.00%")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_UnexpectedException_ReturnsErrorMessage_LogsException()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };

        _mediatorMock.Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, changeReason: ValidChangeReason);

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
        UpdateManufactureOrderStatusResponse updateStatusResponse)
    {
        _mediatorMock.Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        _mediatorMock.Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(submitManufactureResponse);

        _mediatorMock.Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
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

    private static ResidueDistribution CreateDistributionWithinThreshold()
    {
        return new ResidueDistribution
        {
            IsWithinAllowedThreshold = true,
            ActualSemiProductQuantity = 100m,
            TheoreticalConsumption = 99m,
            Difference = 1m,
            DifferencePercentage = 1.0,
            AllowedResiduePercentage = 5.0,
            Products = new List<ProductConsumptionDistribution>
            {
                new ProductConsumptionDistribution
                {
                    ProductCode = "P001",
                    ProductName = "Product 1",
                    ActualPieces = 5m,
                    AdjustedGramsPerUnit = 19.8m,
                    AdjustedConsumption = 99m,
                }
            }
        };
    }

    private static ResidueDistribution CreateDistributionOutsideThreshold()
    {
        return new ResidueDistribution
        {
            IsWithinAllowedThreshold = false,
            ActualSemiProductQuantity = 100m,
            TheoreticalConsumption = 80m,
            Difference = 20m,
            DifferencePercentage = 25.0,
            AllowedResiduePercentage = 5.0,
            Products = new List<ProductConsumptionDistribution>
            {
                new ProductConsumptionDistribution
                {
                    ProductCode = "P001",
                    ProductName = "Product 1",
                    ActualPieces = 5m,
                    AdjustedGramsPerUnit = 20m,
                    AdjustedConsumption = 100m,
                }
            }
        };
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

    private void VerifySubmitManufactureCall(ErpManufactureType expectedType)
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
        bool expectedManualActionRequired,
        bool? expectedWeightWithinTolerance = null,
        decimal? expectedWeightDifference = null)
    {
        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderStatusRequest>(r =>
                r.Id == ValidOrderId &&
                r.NewState == expectedState &&
                r.SemiProductOrderCode == expectedSemiProductCode &&
                r.ProductOrderCode == expectedProductCode &&
                r.DiscardRedisueDocumentCode == expectedDiscardCode &&
                r.ManualActionRequired == expectedManualActionRequired &&
                r.WeightWithinTolerance == expectedWeightWithinTolerance &&
                r.WeightDifference == expectedWeightDifference),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private void VerifyNoUpdateStatusCall()
    {
        _mediatorMock.Verify(x => x.Send(
            It.IsAny<UpdateManufactureOrderStatusRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private void VerifyBoMUpdateCalledForEachProduct(ResidueDistribution distribution)
    {
        foreach (var product in distribution.Products)
        {
            _manufactureClientMock.Verify(
                x => x.UpdateBoMIngredientAmountAsync(
                    product.ProductCode,
                    It.IsAny<string>(),
                    (double)product.AdjustedGramsPerUnit,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
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

    #region CreateManufactureName Safe Substring Tests

    [Fact]
    public async Task ConfirmProductCompletion_WhenProductCodeShorterThanPrefix_DoesNotThrow()
    {
        // Arrange – semi-product code only 3 chars (shorter than the 6-char prefix)
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };

        var updateOrderResponse = new UpdateManufactureOrderResponse
        {
            Success = true,
            Order = new UpdateManufactureOrderDto
            {
                OrderNumber = "MO-SHORT-001",
                SemiProduct = new UpdateManufactureOrderSemiProductDto
                {
                    ProductCode = "AB3",
                    ProductName = "Short Code Product",
                    ActualQuantity = 5.0m,
                    PlannedQuantity = 5.0m,
                    LotNumber = "LOT001",
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

        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_SHORT_001");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();
        var distribution = CreateDistributionWithinThreshold();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act & Assert – must not throw and must succeed (ArgumentOutOfRangeException would be caught and returned as failure)
        var act = async () => await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, overrideConfirmed: false, ValidChangeReason);
        await act.Should().NotThrowAsync();
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, overrideConfirmed: false, ValidChangeReason);
        result.Success.Should().BeTrue("short product code must not cause ArgumentOutOfRangeException");
    }

    [Fact]
    public async Task ConfirmProductCompletion_WhenManufactureNameWouldExceed40Chars_IsTruncated()
    {
        // Arrange – long product name that pushes the manufacture name well past 40 chars
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };

        var updateOrderResponse = new UpdateManufactureOrderResponse
        {
            Success = true,
            Order = new UpdateManufactureOrderDto
            {
                OrderNumber = "MO-LONG-001",
                SemiProduct = new UpdateManufactureOrderSemiProductDto
                {
                    ProductCode = "ABCDEF",
                    ProductName = "A Very Long Semi Product Name That Will Overflow",
                    ActualQuantity = 5.0m,
                    PlannedQuantity = 5.0m,
                    LotNumber = "LOT001",
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

        // Return a long short-name so the formatted name definitely exceeds 40 chars
        _productNameFormatterMock
            .Setup(x => x.ShortProductName(It.IsAny<string>()))
            .Returns("VeryLongShortNameThatExceedsFortyCharactersLimit");

        string? capturedInternalNumber = null;
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_LONG_001");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();
        var distribution = CreateDistributionWithinThreshold();

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<SubmitManufactureResponse>, CancellationToken>((req, _) =>
                capturedInternalNumber = ((SubmitManufactureRequest)req).ManufactureInternalNumber)
            .ReturnsAsync(submitManufactureResponse);
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateStatusResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, overrideConfirmed: false, ValidChangeReason);

        // Assert
        capturedInternalNumber.Should().NotBeNull();
        capturedInternalNumber!.Length.Should().BeLessOrEqualTo(40);
    }

    [Fact]
    public async Task ConfirmSemiProductManufacture_WhenProductCodeShorterThanPrefix_DoesNotThrow()
    {
        // Arrange – semi-product code only 2 chars (shorter than the 6-char prefix)
        var updateOrderResponse = new UpdateManufactureOrderResponse
        {
            Success = true,
            Order = new UpdateManufactureOrderDto
            {
                OrderNumber = "MO-SEMI-SHORT-001",
                SemiProduct = new UpdateManufactureOrderSemiProductDto
                {
                    ProductCode = "XY",
                    ProductName = "Tiny Code Semi Product",
                    ActualQuantity = ValidQuantity,
                    PlannedQuantity = ValidQuantity,
                    LotNumber = "LOT002",
                    ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30))
                },
                Products = new List<UpdateManufactureOrderProductDto>()
            }
        };

        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_SEMI_SHORT_001");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        // Act & Assert – must not throw and must succeed (ArgumentOutOfRangeException would be caught and returned as failure)
        var act = async () => await _service.ConfirmSemiProductManufactureAsync(ValidOrderId, ValidQuantity, ValidChangeReason);
        await act.Should().NotThrowAsync();
        var result = await _service.ConfirmSemiProductManufactureAsync(ValidOrderId, ValidQuantity, ValidChangeReason);
        result.Success.Should().BeTrue("short semi-product code must not cause ArgumentOutOfRangeException");
    }

    #endregion
}
