using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiManufactureClientOrderTests
{
    private readonly Mock<IBoMClient> _bomClientMock = new();
    private readonly Mock<IFlexiManufactureTemplateService> _templateServiceMock = new();
    private readonly FlexiManufactureClient _client;

    public FlexiManufactureClientOrderTests()
    {
        var stockMovementClient = new Mock<IStockItemsMovementClient>();
        var stockClient = new Mock<IErpStockClient>();
        var movementService = new FlexiManufactureDocumentService(
            stockClient.Object,
            stockMovementClient.Object);

        _client = new FlexiManufactureClient(
            _bomClientMock.Object,
            new Mock<IProductSetsClient>().Object,
            new Mock<ILogger<FlexiManufactureClient>>().Object,
            _templateServiceMock.Object,
            new FefoConsumptionAllocator(),
            new FlexiIngredientRequirementAggregator(_templateServiceMock.Object),
            new FlexiIngredientStockValidator(stockClient.Object, TimeProvider.System),
            new FlexiLotLoader(new Mock<ILotsClient>().Object),
            movementService,
            stockMovementClient.Object,
            TimeProvider.System);
    }

    [Fact]
    public async Task SetBomItemsOrderAsync_DelegatesToBomClientAndInvalidatesCache()
    {
        // Arrange
        var items = new List<(int BoMItemId, int Order)>
        {
            (100, 1),
            (200, 2),
        };

        _bomClientMock
            .Setup(x => x.SetItemsOrderAsync(
                It.IsAny<IEnumerable<(int, int)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _client.SetBomItemsOrderAsync("PRD1", items, CancellationToken.None);

        // Assert: SDK called with the right items
        _bomClientMock.Verify(
            x => x.SetItemsOrderAsync(
                It.Is<IEnumerable<(int, int)>>(seq =>
                    seq.Any(t => t.Item1 == 100 && t.Item2 == 1) &&
                    seq.Any(t => t.Item1 == 200 && t.Item2 == 2)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: cache invalidated
        _templateServiceMock.Verify(x => x.InvalidateTemplate("PRD1"), Times.Once);
    }

    [Fact]
    public async Task SetBomItemsOrderAsync_EmptyList_StillCallsBomClientAndInvalidatesCache()
    {
        // Arrange
        _bomClientMock
            .Setup(x => x.SetItemsOrderAsync(
                It.IsAny<IEnumerable<(int, int)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _client.SetBomItemsOrderAsync("PRD1", Array.Empty<(int, int)>(), CancellationToken.None);

        // Assert
        _bomClientMock.Verify(
            x => x.SetItemsOrderAsync(It.IsAny<IEnumerable<(int, int)>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _templateServiceMock.Verify(x => x.InvalidateTemplate("PRD1"), Times.Once);
    }

    [Fact]
    public async Task SetBomItemsOrderAndPhaseAsync_CallsUpdateBoMItemAsyncPerItemAndInvalidatesCache()
    {
        // Arrange
        var items = new List<(int BoMItemId, int Order, string? PhaseLabel)>
        {
            (100, 1, "A"),
            (200, 2, "A"),
            (300, 3, null),
        };

        _bomClientMock
            .Setup(x => x.UpdateBoMItemAsync(
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _client.SetBomItemsOrderAndPhaseAsync("PRD1", items, CancellationToken.None);

        // Assert: called 3 times (once per item)
        _bomClientMock.Verify(
            x => x.UpdateBoMItemAsync(
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // Assert: item 100 called with nameC = "A"
        _bomClientMock.Verify(
            x => x.UpdateBoMItemAsync(
                100,
                1,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                "A",
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: item 300 (null phase) called with nameC = "" (empty = clear field in Flexi)
        _bomClientMock.Verify(
            x => x.UpdateBoMItemAsync(
                300,
                3,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                string.Empty,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: cache invalidated once
        _templateServiceMock.Verify(x => x.InvalidateTemplate("PRD1"), Times.Once);
    }

    [Fact]
    public async Task SetBomItemsOrderAndPhaseAsync_EmptyList_StillInvalidatesCache()
    {
        // Arrange
        _bomClientMock
            .Setup(x => x.UpdateBoMItemAsync(
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _client.SetBomItemsOrderAndPhaseAsync("PRD1", Array.Empty<(int, int, string?)>(), CancellationToken.None);

        // Assert: no UpdateBoMItemAsync calls (empty list)
        _bomClientMock.Verify(
            x => x.UpdateBoMItemAsync(
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert: cache still invalidated
        _templateServiceMock.Verify(x => x.InvalidateTemplate("PRD1"), Times.Once);
    }
}
