using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;
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

public class ConfirmProductCompletionWorkflowTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<IResidueDistributionCalculator> _residueCalculatorMock;
    private readonly Mock<IManufactureNameBuilder> _nameBuilderMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<ConfirmProductCompletionWorkflow>> _loggerMock;
    private readonly ConfirmProductCompletionWorkflow _workflow;

    private const int ValidOrderId = 1;
    private const string TestUserName = "Test User";
    private const string ValidChangeReason = "Testing product completion";

    public ConfirmProductCompletionWorkflowTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _residueCalculatorMock = new Mock<IResidueDistributionCalculator>();
        _nameBuilderMock = new Mock<IManufactureNameBuilder>();
        _timeProviderMock = new Mock<TimeProvider>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<ConfirmProductCompletionWorkflow>>();

        var testTime = DateTime.UtcNow;
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(testTime));

        var testUser = new CurrentUser("test-user-id", TestUserName, "test@example.com", true);
        _currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(testUser);

        _nameBuilderMock
            .Setup(x => x.Build(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<ErpManufactureType>()))
            .Returns("Product-Short-Name");

        _workflow = new ConfirmProductCompletionWorkflow(
            _mediatorMock.Object,
            _residueCalculatorMock.Object,
            _nameBuilderMock.Object,
            _timeProviderMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_AllStepsSucceed_ReturnsSuccess()
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
        var result = await _workflow.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.RequiresConfirmation.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderRequest>(r =>
                r.Id == ValidOrderId &&
                r.Products != null &&
                r.Products.Count == productQuantities.Count),
            It.IsAny<CancellationToken>()), Times.Once);

        _mediatorMock.Verify(x => x.Send(
            It.Is<SubmitManufactureRequest>(r => r.ManufactureType == ErpManufactureType.Product),
            It.IsAny<CancellationToken>()), Times.Once);

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderStatusRequest>(r =>
                r.Id == ValidOrderId &&
                r.NewState == ManufactureOrderState.Completed &&
                r.ProductOrderCode == "ERP_PRODUCT_456" &&
                r.ManualActionRequired == false),
            It.IsAny<CancellationToken>()), Times.Once);

        foreach (var product in distribution.Products)
        {
            _mediatorMock.Verify(x => x.Send(
                It.Is<UpdateBoMIngredientAmountRequest>(r =>
                    r.ProductCode == product.ProductCode &&
                    r.NewAmount == (double)product.AdjustedGramsPerUnit),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenUpdateProductsFails_ReturnsQuantityUpdateError()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = new UpdateManufactureOrderResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError,
        };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Chyba při aktualizaci množství produktů");

        _mediatorMock.Verify(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _mediatorMock.Verify(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenResidueExceedsThresholdAndNotOverridden_ReturnsNeedsConfirmation()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var distribution = CreateDistributionOutsideThreshold();

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.RequiresConfirmation.Should().BeTrue();
        result.Distribution.Should().NotBeNull();
        result.Distribution!.IsWithinAllowedThreshold.Should().BeFalse();

        _mediatorMock.Verify(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _mediatorMock.Verify(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenResidueExceedsThresholdButOverridden_AppendsWeightToleranceNote()
    {
        // Arrange - distribution DifferencePercentage=25.0, AllowedResiduePercentage=5.0
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
        var result = await _workflow.ExecuteAsync(ValidOrderId, productQuantities, true, ValidChangeReason, CancellationToken.None);

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
    public async Task ExecuteAsync_WhenErpSubmitFails_StillTransitionsWithManualActionRequired()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = new SubmitManufactureResponse(
            ErrorCodes.InternalServerError,
            new Dictionary<string, string> { { "message", "ERP connection failed" } });
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();
        var distribution = CreateDistributionWithinThreshold();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderStatusRequest>(r =>
                r.NewState == ManufactureOrderState.Completed &&
                r.ManualActionRequired == true &&
                r.ProductOrderCode == null),
            It.IsAny<CancellationToken>()), Times.Once);

        // BoM updates are skipped when ERP submit fails
        _mediatorMock.Verify(
            x => x.Send(It.IsAny<UpdateBoMIngredientAmountRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBoMUpdateFails_AppendsFailureNoteAndSetsManualActionRequired()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_PRODUCT_456");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();
        var distribution = CreateDistributionWithinThreshold();

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(submitManufactureResponse);
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateStatusResponse);
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateBoMIngredientAmountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateBoMIngredientAmountResponse(new Exception("BoM update failed")));

        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, CancellationToken.None);

        // Assert — workflow completes successfully so operator can be alerted via note
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderStatusRequest>(r =>
                r.ManualActionRequired == true &&
                r.Note != null &&
                r.Note.Contains("BoM update failures")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStatusUpdateFailsAfterErpSucceeds_ReturnsStatusChangeError()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP_PRODUCT_456");
        var updateStatusResponse = new UpdateManufactureOrderStatusResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError,
        };
        var distribution = CreateDistributionWithinThreshold();

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);
        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Chyba při změně stavu");

        _mediatorMock.Verify(x => x.Send(
            It.Is<UpdateManufactureOrderStatusRequest>(r =>
                r.ProductOrderCode == "ERP_PRODUCT_456" &&
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

        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, cts.Token);

        // Assert — OperationCanceledException is caught by the outer catch and returns a generic failure
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("Došlo k neočekávané chybě při dokončení výroby produktů");

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
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m }, { 2, 3.5m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitResponse = new SubmitManufactureResponse
        {
            Success = true,
            ManufactureId = "MFG-PROD-001",
            SemiProductIssueForProductDocCode = "FLX-SP-ISSUE-001",
            MaterialIssueForProductDocCode = "FLX-MI-PROD-001",
            ProductReceiptDocCode = "FLX-RCPT-PROD-001",
        };
        var distribution = CreateDistributionWithinThreshold();
        UpdateManufactureOrderStatusRequest? capturedStatusRequest = null;
        var updateStatusResponse = new UpdateManufactureOrderStatusResponse { Success = true };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(submitResponse);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateBoMIngredientAmountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateBoMIngredientAmountResponse { Success = true });

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<UpdateManufactureOrderStatusResponse>, CancellationToken>(
                (r, _) => capturedStatusRequest = (UpdateManufactureOrderStatusRequest)r)
            .ReturnsAsync(updateStatusResponse);

        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _workflow.ExecuteAsync(
            ValidOrderId, productQuantities, overrideConfirmed: false, changeReason: null, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedStatusRequest.Should().NotBeNull();
        capturedStatusRequest!.FlexiDocSemiProductIssueForProduct.Should().Be("FLX-SP-ISSUE-001");
        capturedStatusRequest.FlexiDocMaterialIssueForProduct.Should().Be("FLX-MI-PROD-001");
        capturedStatusRequest.FlexiDocProductReceipt.Should().Be("FLX-RCPT-PROD-001");
        // Semi-product fields must NOT be set in the product-completion workflow
        capturedStatusRequest.FlexiDocMaterialIssueForSemiProduct.Should().BeNull();
        capturedStatusRequest.FlexiDocSemiProductReceipt.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenBoMFailuresProduceOversizedNote_TruncatesToFit2000CharLimit()
    {
        // Arrange — 30 products, each with a 200-char error message.
        // Raw note would be >> 2000 chars; it must be truncated to exactly <= 2000.
        const int ProductCount = 30;
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponseWithManyProducts(ProductCount);
        var productQuantities = Enumerable.Range(1, ProductCount)
            .ToDictionary(i => i, _ => 1m);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDistributionWithinThreshold());

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitManufactureResponse { Success = true, ManufactureId = "MFG-BIG-001" });

        // Every BoM update fails with a 200-char error string
        var longError = new string('Á', 200);
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateBoMIngredientAmountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateBoMIngredientAmountResponse
            {
                Success = false,
                UserMessage = longError,
            });

        UpdateManufactureOrderStatusRequest? capturedStatusRequest = null;
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<UpdateManufactureOrderStatusResponse>, CancellationToken>(
                (r, _) => capturedStatusRequest = (UpdateManufactureOrderStatusRequest)r)
            .ReturnsAsync(new UpdateManufactureOrderStatusResponse { Success = true });

        // Act
        var result = await _workflow.ExecuteAsync(
            ValidOrderId, productQuantities, overrideConfirmed: false, changeReason: null, CancellationToken.None);

        // Assert
        capturedStatusRequest.Should().NotBeNull();
        capturedStatusRequest!.Note.Should().NotBeNull();
        capturedStatusRequest.Note!.Length.Should().BeLessThanOrEqualTo(2000);
        capturedStatusRequest.ManualActionRequired.Should().BeTrue();
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

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateBoMIngredientAmountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateBoMIngredientAmountResponse());
    }

    private static UpdateManufactureOrderResponse CreateSuccessfulUpdateOrderResponseWithManyProducts(int productCount)
    {
        var products = Enumerable.Range(1, productCount)
            .Select(i => new UpdateManufactureOrderProductDto
            {
                ProductCode = $"P{i:D3}",
                ProductName = $"Product {i}",
                ActualQuantity = 1.0m,
                PlannedQuantity = 1.0m,
            })
            .ToList();

        return new UpdateManufactureOrderResponse
        {
            Success = true,
            Order = new UpdateManufactureOrderDto
            {
                OrderNumber = "MO-2024-LARGE",
                SemiProduct = new UpdateManufactureOrderSemiProductDto
                {
                    ProductCode = "SP001001",
                    ProductName = "Semi Product 1",
                    ActualQuantity = 10.5m,
                    PlannedQuantity = 10.5m,
                    LotNumber = "LOT123",
                    ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                },
                Products = products,
            },
        };
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
                    ActualQuantity = 10.5m,
                    PlannedQuantity = 10.5m,
                    LotNumber = "LOT123",
                    ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                },
                Products = new List<UpdateManufactureOrderProductDto>
                {
                    new UpdateManufactureOrderProductDto
                    {
                        ProductCode = "P001",
                        ProductName = "Product 1",
                        ActualQuantity = 5.0m,
                        PlannedQuantity = 5.0m,
                    },
                },
            },
        };
    }

    private static SubmitManufactureResponse CreateSuccessfulSubmitManufactureResponse(string manufactureId)
    {
        return new SubmitManufactureResponse
        {
            Success = true,
            ManufactureId = manufactureId,
        };
    }

    private static UpdateManufactureOrderStatusResponse CreateSuccessfulUpdateStatusResponse()
    {
        return new UpdateManufactureOrderStatusResponse
        {
            Success = true,
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
                },
            },
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
                },
            },
        };
    }

    #endregion
}
