using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Adapters.Flexi.Tests.Manufacture;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Response;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

/// <summary>
/// Unit tests for FlexiManufactureClient.SubmitManufactureAsync method.
/// All manufacture types (Product and SemiProduct) go through the consolidated
/// per-product path via SubmitManufacturePerProductAsync().
/// </summary>
public class FlexiManufactureClientTests
{
    private readonly Mock<IErpStockClient> _mockStockClient;
    private readonly Mock<IStockItemsMovementClient> _mockStockMovementClient;
    private readonly Mock<IBoMClient> _mockBomClient;
    private readonly Mock<IProductSetsClient> _mockProductSetsClient;
    private readonly Mock<ILotsClient> _mockLotsClient;
    private readonly Mock<ILogger<FlexiManufactureClient>> _mockLogger;
    private readonly Mock<IFlexiManufactureTemplateService> _mockTemplateService;
    private readonly FlexiManufactureClient _client;

    public FlexiManufactureClientTests()
    {
        _mockStockClient = new Mock<IErpStockClient>();
        _mockStockMovementClient = new Mock<IStockItemsMovementClient>();
        _mockBomClient = new Mock<IBoMClient>();
        _mockProductSetsClient = new Mock<IProductSetsClient>();
        _mockLotsClient = new Mock<ILotsClient>();
        _mockLogger = new Mock<ILogger<FlexiManufactureClient>>();
        _mockTemplateService = new Mock<IFlexiManufactureTemplateService>();

        var movementService = new FlexiManufactureMovementService(
            _mockStockClient.Object,
            _mockStockMovementClient.Object);

        _client = new FlexiManufactureClient(
            _mockBomClient.Object,
            _mockProductSetsClient.Object,
            _mockLogger.Object,
            _mockTemplateService.Object,
            new FefoConsumptionAllocator(),
            new FlexiIngredientRequirementAggregator(_mockTemplateService.Object),
            new FlexiIngredientStockValidator(_mockStockClient.Object, TimeProvider.System),
            new FlexiLotLoader(_mockLotsClient.Object),
            movementService);
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

        // Template service returns null — simulates missing BoM header
        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

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

        // Template contains both a valid ingredient and an UNDEFINED ingredient
        var template = new ManufactureTemplate
        {
            TemplateId = 1,
            ProductCode = ManufactureTestData.Products.ConfidentBar.Code,
            ProductName = ManufactureTestData.Products.ConfidentBar.Name,
            Amount = 10.0,
            OriginalAmount = 10.0,
            ManufactureType = ManufactureType.SinglePhase,
            Ingredients = new List<Ingredient>
            {
                new() { TemplateId = 2, ProductCode = ManufactureTestData.Materials.Bisabolol.Code, ProductName = ManufactureTestData.Materials.Bisabolol.Name, Amount = 5.0, ProductType = ProductType.SemiProduct, HasLots = false, HasExpiration = false },
                new() { TemplateId = 3, ProductCode = "UNDEFINED-001", ProductName = "Undefined Product", Amount = 3.0, ProductType = ProductType.UNDEFINED, HasLots = false, HasExpiration = false }
            }
        };

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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

        // Template is for 10 units, so scale factor = 20/10 = 2
        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false),  // Ingredient: 5 per 10 units
            (ManufactureTestData.Materials.Glycerol, 3.0, false));  // Ingredient: 3 per 10 units

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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

    [Fact]
    public async Task GetManufactureTemplateAsync_DelegatesToTemplateService()
    {
        // Arrange - template service returns null (e.g. missing BoM header)
        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        // Act
        var result = await _client.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code);

        // Assert
        Assert.Null(result);
        _mockTemplateService.Verify(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Stock Validation Tests

    [Fact]
    public async Task SubmitManufactureAsync_SufficientStock_Succeeds()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);
        request.ValidateIngredientStock = true;

        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false));

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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

        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false));

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Setup insufficient stock: required 5, available 2
        SetupStockDataForIngredients((ManufactureTestData.Materials.Bisabolol, 2m, 10m, hasLots: false));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<FlexiManufactureException>(
            () => _client.SubmitManufactureAsync(request));

        Assert.Equal(FlexiManufactureOperationKind.StockValidation, exception.OperationKind);
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

        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, true));

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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

        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 12.0, true)); // Requires 12 units

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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

        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 10.0, true)); // Requires 10 units

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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
        var exception = await Assert.ThrowsAsync<FlexiManufactureException>(
            () => _client.SubmitManufactureAsync(request));

        Assert.Equal(FlexiManufactureOperationKind.Allocation, exception.OperationKind);
        Assert.Contains("Cannot allocate full amount", exception.Message);
        Assert.Contains(ManufactureTestData.Materials.Bisabolol.Code, exception.Message);
    }

    [Fact]
    public async Task SubmitManufactureAsync_ProductWithoutLots_CreatesSingleConsumptionItem()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false));

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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

        // Verify consumption movement (Out) - SemiProduct ingredients use SemiProducts warehouse
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.WarehouseId == FlexiStockClient.SemiProductsWarehouseId.ToString()),
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

        // Template with ingredients from same warehouse (SemiProducts warehouse)
        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.SemiProducts.SilkBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 3.0, false), // SemiProduct warehouse
            (ManufactureTestData.Materials.Glycerol, 2.0, false)); // SemiProduct warehouse

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.SemiProducts.SilkBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        SetupStockDataForIngredients(
            (ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false),
            (ManufactureTestData.Materials.Glycerol, 100m, 15m, hasLots: false));

        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Verify single consumption movement with both ingredients from SemiProducts warehouse
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.WarehouseId == FlexiStockClient.SemiProductsWarehouseId.ToString() &&
                req.StockItems.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitManufactureAsync_WhenConsumptionMovementFails_ThrowsFlexiManufactureException()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);
        request.ManufactureType = ErpManufactureType.Product;

        SetupSuccessfulManufacture(ManufactureTestData.Products.ConfidentBar, ManufactureTestData.Materials.Bisabolol, 5.0);

        var failureResult = new OperationResult<OperationResultDetail>(
            System.Net.HttpStatusCode.InternalServerError,
            "Nelze vytvořit výdejku");

        _mockStockMovementClient
            .Setup(x => x.SaveAsync(
                It.Is<StockItemsMovementUpsertRequestFlexiDto>(r => r.StockMovementDirection == StockMovementDirection.Out),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<FlexiManufactureException>(
            () => _client.SubmitManufactureAsync(request));

        Assert.Equal(FlexiManufactureOperationKind.ConsumptionMovement, exception.OperationKind);
        Assert.Contains("consumption stock movement", exception.Message);
        Assert.Equal("Nelze vytvořit výdejku", exception.RawFlexiError);
        Assert.NotNull(exception.WarehouseId);
    }

    [Fact]
    public async Task SubmitManufactureAsync_WhenProductionMovementFails_ThrowsFlexiManufactureException()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);
        request.ManufactureType = ErpManufactureType.Product;

        SetupSuccessfulManufacture(ManufactureTestData.Products.ConfidentBar, ManufactureTestData.Materials.Bisabolol, 5.0);

        var failureResult = new OperationResult<OperationResultDetail>(
            System.Net.HttpStatusCode.InternalServerError,
            "Nelze vytvořit příjemku výrobku");

        _mockStockMovementClient
            .Setup(x => x.SaveAsync(
                It.Is<StockItemsMovementUpsertRequestFlexiDto>(r => r.StockMovementDirection == StockMovementDirection.In),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<FlexiManufactureException>(
            () => _client.SubmitManufactureAsync(request));

        Assert.Equal(FlexiManufactureOperationKind.ProductionMovement, exception.OperationKind);
        Assert.Contains("production stock movement", exception.Message);
        Assert.Equal("Nelze vytvořit příjemku výrobku", exception.RawFlexiError);
    }

    #endregion

    #region Warehouse Selection Tests

    [Fact]
    public async Task SubmitManufactureAsync_SemiProductIngredient_UsesSemiProductsWarehouseForConsumption()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false)); // SemiProduct type

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        SetupStockDataForIngredients((ManufactureTestData.Materials.Bisabolol, 100m, 10m, hasLots: false));
        SetupSuccessfulStockMovements();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert
        Assert.Equal("MO-001", result);

        // Verify semi-products warehouse used for consumption
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.WarehouseId == FlexiStockClient.SemiProductsWarehouseId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitManufactureAsync_SemiProductIngredient_UsesSemiProductWarehouse()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(ManufactureTestData.Products.ConfidentBar, 10m);

        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.SemiProducts.SilkBar, 2.0, false)); // SemiProduct type

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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

        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false),  // 5 units at 10 CZK = 50 CZK
            (ManufactureTestData.Materials.Glycerol, 3.0, false));  // 3 units at 15 CZK = 45 CZK

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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

        // ConfidentBar uses 5kg Bisabolol
        var templateA = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false));
        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateA);

        // GiftBox uses 2kg Glycerol
        var templateB = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.GiftBox, 5.0,
            (ManufactureTestData.Materials.Glycerol, 2.0, false));
        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.GiftBox.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateB);

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
        var templateA = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false));
        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateA);

        var templateB = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.GiftBox, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 10.0, false));
        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.GiftBox.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateB);

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
        var templateA = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false));
        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateA);

        // Product B uses Glycerol
        var templateB = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.GiftBox, 5.0,
            (ManufactureTestData.Materials.Glycerol, 3.0, false));
        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.GiftBox.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateB);

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
        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false),
            (ManufactureTestData.Materials.Glycerol, 3.0, false));

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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
        var template = ManufactureTestData.CreateTemplate(product, 10.0, (ingredient, ingredientAmount, false));

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(product.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

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
    /// Sets up template for Product A (ConfidentBar) using Bisabolol
    /// </summary>
    private void SetupProductABoM()
    {
        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.ConfidentBar, 10.0,
            (ManufactureTestData.Materials.Bisabolol, 5.0, false));

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.ConfidentBar.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
    }

    /// <summary>
    /// Sets up template for Product B (GiftBox) using Glycerol
    /// </summary>
    private void SetupProductBBoM()
    {
        var template = ManufactureTestData.CreateTemplate(
            ManufactureTestData.Products.GiftBox, 5.0,
            (ManufactureTestData.Materials.Glycerol, 2.0, false));

        _mockTemplateService
            .Setup(x => x.GetManufactureTemplateAsync(ManufactureTestData.Products.GiftBox.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
    }

    #endregion
}
