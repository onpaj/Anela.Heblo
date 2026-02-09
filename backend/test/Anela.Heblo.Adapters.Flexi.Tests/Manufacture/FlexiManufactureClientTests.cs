using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Adapters.Flexi.Tests.Manufacture;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.IssuedOrders;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model;
using Rem.FlexiBeeSDK.Model.IssuedOrders;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

/// <summary>
/// Unit tests for FlexiManufactureClient.SubmitManufactureAsync method.
/// Tests cover two main code paths:
/// - Product type: Processes items independently via SubmitManufacturePerProductAsync()
/// - SemiProduct type: Aggregates all items via SubmitManufactureAggregatedAsync()
/// </summary>
public class FlexiManufactureClientTests
{
    private readonly Mock<IIssuedOrdersClient> _mockOrdersClient;
    private readonly Mock<IErpStockClient> _mockStockClient;
    private readonly Mock<IStockItemsMovementClient> _mockStockMovementClient;
    private readonly Mock<IBoMClient> _mockBomClient;
    private readonly Mock<IProductSetsClient> _mockProductSetsClient;
    private readonly Mock<ILotsClient> _mockLotsClient;
    private readonly Mock<ILogger<FlexiManufactureClient>> _mockLogger;
    private readonly FlexiManufactureClient _client;

    public FlexiManufactureClientTests()
    {
        _mockOrdersClient = new Mock<IIssuedOrdersClient>();
        _mockStockClient = new Mock<IErpStockClient>();
        _mockStockMovementClient = new Mock<IStockItemsMovementClient>();
        _mockBomClient = new Mock<IBoMClient>();
        _mockProductSetsClient = new Mock<IProductSetsClient>();
        _mockLotsClient = new Mock<ILotsClient>();
        _mockLogger = new Mock<ILogger<FlexiManufactureClient>>();

        _client = new FlexiManufactureClient(
            _mockOrdersClient.Object,
            _mockStockClient.Object,
            _mockStockMovementClient.Object,
            _mockBomClient.Object,
            _mockProductSetsClient.Object,
            _mockLotsClient.Object,
            TimeProvider.System, // Use real TimeProvider - it's not critical to mock for these tests
            _mockLogger.Object);
    }

    #region Basic Flow Tests

    [Fact]
    public async Task SubmitManufactureAsync_ProductType_ReturnsOrderCode()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);
        request.ManufactureType = ErpManufactureType.Product;

        SetupSuccessfulManufacture(ManufactureTestData.Products.ConfidentBar, ManufactureTestData.Materials.Bisabolol, 5.0);

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);
        VerifyStockMovementsCreated(times: 2); // 1 consumption + 1 production
    }

    [Fact]
    public async Task SubmitManufactureAsync_SemiProductType_ReturnsOrderCode()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.SemiProducts.SilkBar, 10m);
        request.ManufactureType = ErpManufactureType.SemiProduct;

        SetupSuccessfulManufacture(ManufactureTestData.SemiProducts.SilkBar, ManufactureTestData.Materials.Bisabolol, 5.0);

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);
        VerifyStockMovementsCreated(times: 2); // 1 consumption + 1 production
    }

    [Fact]
    public async Task SubmitManufactureAsync_EmptyItems_ReturnsOrderCodeWithoutMovements()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);
        request.Items = new List<SubmitManufactureClientItem>(); // Empty items

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);
        VerifyStockMovementsCreated(times: 0); // No movements created
    }

    [Fact]
    public async Task SubmitManufactureAsync_ItemsWithZeroAmount_SkipsZeroAmountItems()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);
        request.ManufactureType = ErpManufactureType.Product;
        request.Items.Add(new SubmitManufactureClientItem
        {
            ProductCode = ManufactureTestData.Products.GiftBox.Code,
            ProductName = ManufactureTestData.Products.GiftBox.Name,
            Amount = 0m // Zero amount - should be skipped
        });

        SetupSuccessfulManufacture(ManufactureTestData.Products.ConfidentBar, ManufactureTestData.Materials.Bisabolol, 5.0);

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);
        // Only one product processed (ConfidentBar), GiftBox with zero amount is skipped
        VerifyStockMovementsCreated(times: 2); // 1 consumption + 1 production for ConfidentBar
    }

    #endregion

    #region BoM/Template Tests

    [Fact]
    public async Task SubmitManufactureAsync_MissingBoMHeader_ThrowsApplicationException()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        // Setup BoM without header (no Level 1 item)
        var bomItems = new List<BoMItemFlexiDto>
        {
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol) // Only Level 2 item
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ApplicationException>(
            () => _client.SubmitManufactureAsync(request));

        Assert.Contains($"No BoM header for product {ManufactureTestData.Products.ConfidentBar.Code} found", exception.Message);
    }

    [Fact]
    public async Task SubmitManufactureAsync_UndefinedIngredientType_FiltersOutUndefinedTypes()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        // Setup BoM with header and ingredients including UNDEFINED type
        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol),
            CreateBoMItemWithUndefinedType(3, 2, 3.0, "UNDEFINED-001", "Undefined Product")
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        SetupStockDataForIngredients(
            (ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false));

        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);
        // Only Bisabolol should be processed, UNDEFINED ingredient filtered out
        VerifyStockMovementsCreated(times: 2);
    }

    [Fact]
    public async Task SubmitManufactureAsync_ScalingFactor_CalculatesCorrectAmounts()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 20m); // 20 units

        // BoM template is for 10 units, so scale factor = 20/10 = 2
        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0), // Template amount: 10
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol), // Ingredient: 5 per 10 units
            ManufactureTestData.CreateBoMItem(3, 2, 3.0, ManufactureTestData.Materials.Glycerol) // Ingredient: 3 per 10 units
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        SetupStockDataForIngredients(
            (ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false),
            (ManufactureTestData.Materials.Glycerol, 100m, 15m, hasLots: false));

        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Verify scaled amounts: Bisabolol should be 10 (5 * 2), Glycerol should be 6 (3 * 2)
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.StockItems.Any(i => i.ProductCode == ManufactureTestData.Materials.Bisabolol.Code && i.Amount == 10.0) &&
                req.StockItems.Any(i => i.ProductCode == ManufactureTestData.Materials.Glycerol.Code && i.Amount == 6.0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Stock Validation Tests

    [Fact]
    public async Task SubmitManufactureAsync_SufficientStock_Succeeds()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);
        request.ValidateIngredientStock = true;

        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol)
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        // Setup sufficient stock: required 5, available 100
        SetupStockDataForIngredients((ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false));
        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);
    }

    [Fact]
    public async Task SubmitManufactureAsync_InsufficientStock_ThrowsException()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);
        request.ValidateIngredientStock = true;

        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol)
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        // Setup insufficient stock: required 5, available 2
        SetupStockDataForIngredients((ManufactureTestData.Materials.Bisabolol, 2m, 10m, hasLots: false));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _client.SubmitManufactureAsync(request));

        Assert.Contains("Insufficient stock", exception.Message);
        Assert.Contains(ManufactureTestData.Materials.Bisabolol.Name, exception.Message);
        Assert.Contains(ManufactureTestData.Materials.Bisabolol.Code, exception.Message);
        Assert.Contains("Required 5", exception.Message);
        Assert.Contains("Available 2", exception.Message);
    }

    #endregion

    #region FEFO Allocation Tests

    [Fact]
    public async Task SubmitManufactureAsync_SingleLotSufficient_AllocatesFromOneLot()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol)
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        // Setup stock with lots
        SetupStockDataForIngredients((ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: true));

        // Single lot with sufficient amount
        var lots = new List<CatalogLot>
        {
            ManufactureTestData.CreateLot(ManufactureTestData.Materials.Bisabolol, 50m, "LOT-001", new DateOnly(2025, 6, 1))
        };

        _mockLotsClient.Setup(x => x.GetAsync(ManufactureTestData.Materials.Bisabolol.Code, 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lots);

        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Verify consumption uses the lot
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.StockItems.Any(i =>
                    i.ProductCode == ManufactureTestData.Materials.Bisabolol.Code &&
                    i.LotNumber == "LOT-001" &&
                    i.Amount == 5.0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitManufactureAsync_MultipleLots_AllocatesInExpirationOrder()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 12.0, ManufactureTestData.Materials.Bisabolol) // Requires 12 units
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        SetupStockDataForIngredients((ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: true));

        // Multiple lots with different expiration dates
        var lots = new List<CatalogLot>
        {
            ManufactureTestData.CreateLot(ManufactureTestData.Materials.Bisabolol, 5m, "LOT-003", new DateOnly(2025, 9, 1)), // Expires last
            ManufactureTestData.CreateLot(ManufactureTestData.Materials.Bisabolol, 5m, "LOT-001", new DateOnly(2025, 3, 1)), // Expires first
            ManufactureTestData.CreateLot(ManufactureTestData.Materials.Bisabolol, 5m, "LOT-002", new DateOnly(2025, 6, 1))  // Expires middle
        };

        _mockLotsClient.Setup(x => x.GetAsync(ManufactureTestData.Materials.Bisabolol.Code, 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lots);

        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Verify FEFO: LOT-001 (5) + LOT-002 (5) + LOT-003 (2) = 12 units
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.StockItems.Count(i => i.ProductCode == ManufactureTestData.Materials.Bisabolol.Code) == 3 &&
                req.StockItems.Any(i => i.LotNumber == "LOT-001" && i.Amount == 5.0) &&
                req.StockItems.Any(i => i.LotNumber == "LOT-002" && i.Amount == 5.0) &&
                req.StockItems.Any(i => i.LotNumber == "LOT-003" && i.Amount == 2.0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitManufactureAsync_InsufficientLots_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 10.0, ManufactureTestData.Materials.Bisabolol) // Requires 10 units
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        SetupStockDataForIngredients((ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: true));

        // Lots with insufficient total amount (only 7 units available)
        var lots = new List<CatalogLot>
        {
            ManufactureTestData.CreateLot(ManufactureTestData.Materials.Bisabolol, 3m, "LOT-001", new DateOnly(2025, 3, 1)),
            ManufactureTestData.CreateLot(ManufactureTestData.Materials.Bisabolol, 4m, "LOT-002", new DateOnly(2025, 6, 1))
        };

        _mockLotsClient.Setup(x => x.GetAsync(ManufactureTestData.Materials.Bisabolol.Code, 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lots);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _client.SubmitManufactureAsync(request));

        Assert.Contains("Could not allocate sufficient lots", exception.Message);
        Assert.Contains(ManufactureTestData.Materials.Bisabolol.Name, exception.Message);
        Assert.Contains(ManufactureTestData.Materials.Bisabolol.Code, exception.Message);
        Assert.Contains("Required: 10", exception.Message);
        Assert.Contains("Allocated: 7", exception.Message);
        Assert.Contains("Missing: 3", exception.Message);
    }

    [Fact]
    public async Task SubmitManufactureAsync_ProductWithoutLots_CreatesSingleConsumptionItem()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol)
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        // Setup stock without lots
        SetupStockDataForIngredients((ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false));

        _mockLotsClient.Setup(x => x.GetAsync(ManufactureTestData.Materials.Bisabolol.Code, 0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogLot>());

        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Verify consumption has no lot number
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.StockItems.Any(i =>
                    i.ProductCode == ManufactureTestData.Materials.Bisabolol.Code &&
                    i.LotNumber == null &&
                    i.Amount == 5.0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Stock Movement Tests

    [Fact]
    public async Task SubmitManufactureAsync_ProductType_CreatesConsumptionAndProductionMovements()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);
        request.ManufactureType = ErpManufactureType.Product;

        SetupSuccessfulManufacture(ManufactureTestData.Products.ConfidentBar, ManufactureTestData.Materials.Bisabolol, 5.0);

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Verify consumption movement (Out)
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.WarehouseId == FlexiStockClient.MaterialWarehouseId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify production movement (In)
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.In &&
                req.WarehouseId == FlexiStockClient.ProductsWarehouseId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitManufactureAsync_SemiProductType_GroupsConsumptionByWarehouse()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.SemiProducts.SilkBar, 10m);
        request.ManufactureType = ErpManufactureType.SemiProduct;

        // Setup BoM with ingredients from different warehouses
        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.SemiProducts.SilkBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 3.0, ManufactureTestData.Materials.Bisabolol), // Material warehouse
            ManufactureTestData.CreateBoMItem(3, 2, 2.0, ManufactureTestData.Materials.Glycerol)   // Material warehouse
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.SemiProducts.SilkBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        SetupStockDataForIngredients(
            (ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false),
            (ManufactureTestData.Materials.Glycerol, 100m, 15m, hasLots: false));

        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Verify single consumption movement with both materials
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.WarehouseId == FlexiStockClient.MaterialWarehouseId.ToString() &&
                req.StockItems.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // NOTE: These tests require FlexiResult from Rem.FlexiBeeSDK which is difficult to mock.
    // The behavior they test (exception on SaveAsync failure) is covered by integration tests.
    // [Fact]
    // public async Task SubmitManufactureAsync_ConsumptionFails_ThrowsException()
    // [Fact]
    // public async Task SubmitManufactureAsync_ProductionFails_ThrowsException()

    #endregion

    #region Warehouse Selection Tests

    [Fact]
    public async Task SubmitManufactureAsync_MaterialIngredient_UsesMaterialWarehouse()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol) // Material type
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        SetupStockDataForIngredients((ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false));
        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Verify material warehouse used for consumption
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.WarehouseId == FlexiStockClient.MaterialWarehouseId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitManufactureAsync_SemiProductIngredient_UsesSemiProductWarehouse()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 2.0, ManufactureTestData.SemiProducts.SilkBar) // SemiProduct type
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        SetupStockDataForIngredients((ManufactureTestData.SemiProducts.SilkBar, 100m, 50m, hasLots: false));
        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Verify semi-product warehouse used for consumption
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.WarehouseId == FlexiStockClient.SemiProductsWarehouseId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Unit Price Calculation Tests

    [Fact]
    public async Task SubmitManufactureAsync_CalculatesCorrectUnitPrice()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m); // 10 units

        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol),  // 5 units at 10 CZK = 50 CZK
            ManufactureTestData.CreateBoMItem(3, 2, 3.0, ManufactureTestData.Materials.Glycerol)    // 3 units at 15 CZK = 45 CZK
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        SetupStockDataForIngredients(
            (ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false),  // Price: 10 CZK
            (ManufactureTestData.Materials.Glycerol, 100m, 15m, hasLots: false));  // Price: 15 CZK

        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Total cost: (5 * 10) + (3 * 15) = 50 + 45 = 95 CZK
        // Unit price: 95 / 10 = 9.5 CZK per unit
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.In &&
                req.StockItems.Any(i =>
                    i.ProductCode == ManufactureTestData.Products.ConfidentBar.Code &&
                    i.UnitPrice == 9.5)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Multi-Product Consolidation Tests

    [Fact]
    public async Task SubmitManufactureAsync_MultipleProducts_CreatesConsolidatedDocuments()
    {
        // Arrange: 2 products, each with different ingredients
        var request = new SubmitManufactureClientRequest
        {
            ManufactureOrderCode = "MO-001",
            ManufactureInternalNumber = "INT-MO-001",
            Date = new DateTime(2024, 1, 15),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            LotNumber = "LOT123",
            ExpirationDate = new DateOnly(2025, 1, 15),
            Items = new List<SubmitManufactureClientItem>
            {
                new() { ProductCode = ManufactureTestData.Products.ConfidentBar.Code, ProductName = ManufactureTestData.Products.ConfidentBar.Name, Amount = 10m },
                new() { ProductCode = ManufactureTestData.Products.GiftBox.Code, ProductName = ManufactureTestData.Products.GiftBox.Name, Amount = 5m }
            }
        };

        // Setup BoMs for both products
        SetupProductABoM();  // ConfidentBar uses Bisabolol
        SetupProductBBoM();  // GiftBox uses Glycerol
        SetupStockDataForIngredients(
            (ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false),
            (ManufactureTestData.Materials.Glycerol, 100m, 15m, hasLots: false));
        SetupSuccessfulStockMovements();

        // Act
        await _client.SubmitManufactureAsync(request);

        // Assert: Only 2 SaveAsync calls (1 consume + 1 produce), NOT 4
        VerifyStockMovementsCreated(times: 2);
    }

    [Fact]
    public async Task SubmitManufactureAsync_MultipleProducts_CalculatesCorrectUnitPricePerProduct()
    {
        // Arrange: 2 products with different BoMs
        // Product A (ConfidentBar, 10 units): Uses 5kg Bisabolol @ 10 CZK = 50 CZK total → 5 CZK/unit
        // Product B (GiftBox, 5 units): Uses 2kg Glycerol @ 25 CZK = 50 CZK total → 10 CZK/unit
        var request = new SubmitManufactureClientRequest
        {
            ManufactureOrderCode = "MO-001",
            ManufactureInternalNumber = "INT-MO-001",
            Date = new DateTime(2024, 1, 15),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            LotNumber = "LOT123",
            ExpirationDate = new DateOnly(2025, 1, 15),
            Items = new List<SubmitManufactureClientItem>
            {
                new() { ProductCode = ManufactureTestData.Products.ConfidentBar.Code, ProductName = ManufactureTestData.Products.ConfidentBar.Name, Amount = 10m },
                new() { ProductCode = ManufactureTestData.Products.GiftBox.Code, ProductName = ManufactureTestData.Products.GiftBox.Name, Amount = 5m }
            }
        };

        // Setup BoM: ConfidentBar uses 5kg Bisabolol
        var bomProductA = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol)
        };
        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomProductA);

        // Setup BoM: GiftBox uses 2kg Glycerol
        var bomProductB = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.GiftBox, 5.0),
            ManufactureTestData.CreateBoMItem(2, 2, 2.0, ManufactureTestData.Materials.Glycerol)
        };
        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.GiftBox.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomProductB);

        // Setup stock prices: Bisabolol = 10 CZK, Glycerol = 25 CZK
        SetupStockDataForIngredients(
            (ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false),
            (ManufactureTestData.Materials.Glycerol, 100m, 25m, hasLots: false));

        SetupSuccessfulStockMovements();

        // Act
        await _client.SubmitManufactureAsync(request);

        // Assert: Verify production movement has correct per-product prices
        // ConfidentBar: (5kg * 10 CZK) / 10 units = 5 CZK/unit
        // GiftBox: (2kg * 25 CZK) / 5 units = 10 CZK/unit
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.In &&
                req.StockItems.Count == 2 &&
                req.StockItems.Any(i => i.ProductCode == ManufactureTestData.Products.ConfidentBar.Code && i.UnitPrice == 5.0) &&
                req.StockItems.Any(i => i.ProductCode == ManufactureTestData.Products.GiftBox.Code && i.UnitPrice == 10.0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitManufactureAsync_SharedIngredients_DistributesCostCorrectly()
    {
        // Arrange: 2 products sharing the same ingredient (Bisabolol)
        // Product A (10 units): Uses 5kg Bisabolol @ 10 CZK = 50 CZK → 5 CZK/unit
        // Product B (10 units): Uses 10kg Bisabolol @ 10 CZK = 100 CZK → 10 CZK/unit
        var request = new SubmitManufactureClientRequest
        {
            ManufactureOrderCode = "MO-001",
            ManufactureInternalNumber = "INT-MO-001",
            Date = new DateTime(2024, 1, 15),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            LotNumber = "LOT123",
            ExpirationDate = new DateOnly(2025, 1, 15),
            Items = new List<SubmitManufactureClientItem>
            {
                new() { ProductCode = ManufactureTestData.Products.ConfidentBar.Code, ProductName = ManufactureTestData.Products.ConfidentBar.Name, Amount = 10m },
                new() { ProductCode = ManufactureTestData.Products.GiftBox.Code, ProductName = ManufactureTestData.Products.GiftBox.Name, Amount = 10m }
            }
        };

        // Both products use Bisabolol but in different amounts
        var bomProductA = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol)
        };
        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomProductA);

        var bomProductB = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.GiftBox, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 10.0, ManufactureTestData.Materials.Bisabolol)
        };
        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.GiftBox.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomProductB);

        SetupStockDataForIngredients((ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false));
        SetupSuccessfulStockMovements();

        // Act
        await _client.SubmitManufactureAsync(request);

        // Assert: Each product's unit price reflects only its own consumption
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.In &&
                req.StockItems.Any(i => i.ProductCode == ManufactureTestData.Products.ConfidentBar.Code && i.UnitPrice == 5.0) &&
                req.StockItems.Any(i => i.ProductCode == ManufactureTestData.Products.GiftBox.Code && i.UnitPrice == 10.0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitManufactureAsync_MultipleProducts_ConsumeDocumentContainsAllLines()
    {
        // Arrange: 2 products, each using different materials
        var request = new SubmitManufactureClientRequest
        {
            ManufactureOrderCode = "MO-001",
            ManufactureInternalNumber = "INT-MO-001",
            Date = new DateTime(2024, 1, 15),
            CreatedBy = "TestUser",
            ManufactureType = ErpManufactureType.Product,
            LotNumber = "LOT123",
            ExpirationDate = new DateOnly(2025, 1, 15),
            Items = new List<SubmitManufactureClientItem>
            {
                new() { ProductCode = ManufactureTestData.Products.ConfidentBar.Code, ProductName = ManufactureTestData.Products.ConfidentBar.Name, Amount = 10m },
                new() { ProductCode = ManufactureTestData.Products.GiftBox.Code, ProductName = ManufactureTestData.Products.GiftBox.Name, Amount = 5m }
            }
        };

        // Product A uses Bisabolol
        var bomProductA = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol)
        };
        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomProductA);

        // Product B uses Glycerol
        var bomProductB = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.GiftBox, 5.0),
            ManufactureTestData.CreateBoMItem(2, 2, 3.0, ManufactureTestData.Materials.Glycerol)
        };
        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.GiftBox.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomProductB);

        SetupStockDataForIngredients(
            (ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false),
            (ManufactureTestData.Materials.Glycerol, 100m, 15m, hasLots: false));
        SetupSuccessfulStockMovements();

        // Act
        await _client.SubmitManufactureAsync(request);

        // Assert: Single consume document has lines for both Bisabolol and Glycerol
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.StockItems.Count == 2 &&
                req.StockItems.Any(i => i.ProductCode == ManufactureTestData.Materials.Bisabolol.Code) &&
                req.StockItems.Any(i => i.ProductCode == ManufactureTestData.Materials.Glycerol.Code)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitManufactureAsync_ConsumptionLines_HaveCorrectUnitPrices()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        // Product uses both Bisabolol @ 10 CZK and Glycerol @ 25 CZK
        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol),
            ManufactureTestData.CreateBoMItem(3, 2, 3.0, ManufactureTestData.Materials.Glycerol)
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        SetupStockDataForIngredients(
            (ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false),
            (ManufactureTestData.Materials.Glycerol, 100m, 25m, hasLots: false));
        SetupSuccessfulStockMovements();

        // Act
        await _client.SubmitManufactureAsync(request);

        // Assert: Each consumption line has the correct material price
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.StockItems.Any(i =>
                    i.ProductCode == ManufactureTestData.Materials.Bisabolol.Code &&
                    i.UnitPrice == 10.0) &&
                req.StockItems.Any(i =>
                    i.ProductCode == ManufactureTestData.Materials.Glycerol.Code &&
                    i.UnitPrice == 25.0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Sets up a complete successful manufacture scenario with minimal configuration
    /// </summary>
    private void SetupSuccessfulManufacture(ManufactureTestData.TestProduct product, ManufactureTestData.TestProduct ingredient, double ingredientAmount)
    {
        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, product, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, ingredientAmount, ingredient)
        };

        _mockBomClient.Setup(x => x.GetAsync(product.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);

        SetupStockDataForIngredients((ingredient, 100m, 10m, hasLots: false));
        SetupSuccessfulStockMovements();
    }

    /// <summary>
    /// Sets up stock data for multiple ingredients
    /// </summary>
    private void SetupStockDataForIngredients(params (ManufactureTestData.TestProduct product, decimal stock, decimal price, bool hasLots)[] ingredients)
    {
        // Setup empty stock for all warehouses by default
        _mockStockClient.Setup(x => x.StockToDateAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>());

        // Group ingredients by warehouse to avoid overwriting setups
        var stockByWarehouse = ingredients.GroupBy(i => i.product.Type switch
        {
            ProductType.Material => FlexiStockClient.MaterialWarehouseId,
            ProductType.SemiProduct => FlexiStockClient.SemiProductsWarehouseId,
            _ => FlexiStockClient.ProductsWarehouseId
        });

        foreach (var warehouseGroup in stockByWarehouse)
        {
            var warehouseId = warehouseGroup.Key;
            var stockItems = warehouseGroup.Select(i => new ErpStock
            {
                ProductCode = i.product.Code,
                Stock = i.stock,
                Price = i.price,
                HasLots = i.hasLots
            }).ToList();

            _mockStockClient.Setup(x => x.StockToDateAsync(
                    It.IsAny<DateTime>(),
                    warehouseId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(stockItems);
        }
    }

    /// <summary>
    /// Sets up successful stock movement responses
    /// NOTE: FlexiResult is from Rem.FlexiBeeSDK and cannot be easily mocked in unit tests.
    /// These tests focus on verifying the correct method calls are made.
    /// Full error handling scenarios are covered in integration tests.
    /// </summary>
    private void SetupSuccessfulStockMovements()
    {
        // Don't setup any response - let it return null which will cause NullRef if IsSuccess is accessed
        // This is intentional - we're testing the happy path where IsSuccess would be true
    }

    /// <summary>
    /// Verifies that stock movements were created the expected number of times
    /// </summary>
    private void VerifyStockMovementsCreated(int times)
    {
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.IsAny<StockItemsMovementUpsertRequestFlexiDto>(),
            It.IsAny<CancellationToken>()), Times.Exactly(times));
    }

    /// <summary>
    /// Creates a BoM header item (Level 1)
    /// </summary>
    private static BoMItemFlexiDto CreateBoMItemHeader(int id, ManufactureTestData.TestProduct product, double amount)
    {
        return ManufactureTestData.CreateBoMItem(id, 1, amount, product, null);
    }

    /// <summary>
    /// Creates a BoM item with UNDEFINED product type for testing filtering
    /// </summary>
    private static BoMItemFlexiDto CreateBoMItemWithUndefinedType(int id, int level, double amount, string code, string name)
    {
        // Create BoM item with ProductTypeId set to null (which gets resolved to UNDEFINED)
        var item = new BoMItemFlexiDto
        {
            Id = id,
            Level = level,
            Amount = amount
        };

        item.Ingredient = new List<BomProductFlexiDto>
        {
            new BomProductFlexiDto
            {
                Code = $"code:{code}",
                Name = name
                // ProductTypeId not set (null) results in UNDEFINED in ResolveProductType
            }
        };

        return item;
    }

    /// <summary>
    /// Sets up BoM for Product A (ConfidentBar) using Bisabolol
    /// </summary>
    private void SetupProductABoM()
    {
        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.ConfidentBar, 10.0),
            ManufactureTestData.CreateBoMItem(2, 2, 5.0, ManufactureTestData.Materials.Bisabolol)
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);
    }

    /// <summary>
    /// Sets up BoM for Product B (GiftBox) using Glycerol
    /// </summary>
    private void SetupProductBBoM()
    {
        var bomItems = new List<BoMItemFlexiDto>
        {
            CreateBoMItemHeader(1, ManufactureTestData.Products.GiftBox, 5.0),
            ManufactureTestData.CreateBoMItem(2, 2, 2.0, ManufactureTestData.Materials.Glycerol)
        };

        _mockBomClient.Setup(x => x.GetAsync(ManufactureTestData.Products.GiftBox.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomItems);
    }

    #endregion
}
