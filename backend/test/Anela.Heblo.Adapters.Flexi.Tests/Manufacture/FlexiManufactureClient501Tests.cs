using System.Net;
using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiManufactureClient501Tests
{
    private readonly Mock<IBoMClient> _mockBomClient;
    private readonly Mock<ILogger<FlexiManufactureClient>> _mockLogger;
    private readonly FlexiManufactureClient _client;

    public FlexiManufactureClient501Tests()
    {
        _mockBomClient = new Mock<IBoMClient>();
        _mockLogger = new Mock<ILogger<FlexiManufactureClient>>();
        var mockTemplateService = new Mock<IFlexiManufactureTemplateService>();
        var mockStockClient = new Mock<IErpStockClient>();
        var mockStockMovementClient = new Mock<IStockItemsMovementClient>();
        var mockProductSetsClient = new Mock<IProductSetsClient>();
        var mockLotsClient = new Mock<ILotsClient>();

        var movementService = new FlexiManufactureDocumentService(
            mockStockClient.Object,
            mockStockMovementClient.Object);

        _client = new FlexiManufactureClient(
            _mockBomClient.Object,
            mockProductSetsClient.Object,
            _mockLogger.Object,
            mockTemplateService.Object,
            new FefoConsumptionAllocator(),
            new FlexiIngredientRequirementAggregator(mockTemplateService.Object),
            new FlexiIngredientStockValidator(mockStockClient.Object, TimeProvider.System),
            new FlexiLotLoader(mockLotsClient.Object),
            movementService,
            mockStockMovementClient.Object,
            TimeProvider.System,
            new Mock<IManufactureHistoryCacheInvalidator>().Object);
    }

    [Fact]
    public async Task UpdateBoMIngredientAmountAsync_When501Returned_LogsErrorAndRethrows()
    {
        // Arrange
        var exception = new HttpRequestException("NotImplemented", null, HttpStatusCode.NotImplemented);
        _mockBomClient
            .Setup(x => x.UpdateIngredientAmountAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var act = async () => await _client.UpdateBoMIngredientAmountAsync("PROD-001", "ING-001", 5.0, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("501") && v.ToString()!.Contains("kusovnik")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task FindByIngredientAsync_When501Returned_LogsErrorAndRethrows()
    {
        // Arrange
        var exception = new HttpRequestException("NotImplemented", null, HttpStatusCode.NotImplemented);
        _mockBomClient
            .Setup(x => x.GetByIngredientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var act = async () => await _client.FindByIngredientAsync("ING-001", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("501") && v.ToString()!.Contains("kusovnik")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
