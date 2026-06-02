using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Catalog;

public class CatalogRepositoryTests
{
    private readonly Mock<ICatalogSalesClient> _salesClientMock = new();
    private readonly Mock<ICatalogAttributesClient> _attributesClientMock = new();
    private readonly Mock<IEshopStockClient> _eshopStockClientMock = new();
    private readonly Mock<IConsumedMaterialsClient> _consumedMaterialClientMock = new();
    private readonly Mock<IPurchaseHistoryClient> _purchaseHistoryClientMock = new();
    private readonly Mock<IErpStockClient> _erpStockClientMock = new();
    private readonly Mock<ILotsClient> _lotsClientMock = new();
    private readonly Mock<IProductPriceEshopClient> _productPriceEshopClientMock = new();
    private readonly Mock<IProductPriceErpClient> _productPriceErpClientMock = new();
    private readonly Mock<IProductEshopUrlClient> _productEshopUrlClientMock = new();
    private readonly Mock<ITransportBoxRepository> _transportBoxRepositoryMock = new();
    private readonly Mock<IStockTakingRepository> _stockTakingRepositoryMock = new();
    private readonly Mock<IPurchaseOrderRepository> _purchaseOrderRepositoryMock = new();
    private readonly Mock<IManufactureOrderRepository> _manufactureOrderRepositoryMock = new();
    private readonly Mock<IManufactureHistoryClient> _manufactureHistoryClientMock = new();
    private readonly Mock<IManufactureDifficultyRepository> _manufactureDifficultyRepositoryMock = new();
    private readonly Mock<IManufacturedProductInventoryRepository> _manufacturedInventoryRepositoryMock = new();
    private readonly Mock<ICatalogResilienceService> _resilienceServiceMock = new();
    private readonly Mock<ICatalogMergeScheduler> _mergeSchedulerMock = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<TimeProvider> _timeProviderMock = new();

    private readonly CatalogRepository _repository;

    public CatalogRepositoryTests()
    {
        _productEshopUrlClientMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductEshopUrl>());
        _manufacturedInventoryRepositoryMock.Setup(x => x.GetTotalAmountByProductCodeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());

        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(DateTimeOffset.UtcNow);

        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IEnumerable<CatalogSaleRecord>>>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IEnumerable<CatalogSaleRecord>>>, string, CancellationToken>((op, _, ct) => op(ct));
        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IEnumerable<CatalogAttributes>>>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IEnumerable<CatalogAttributes>>>, string, CancellationToken>((op, _, ct) => op(ct));
        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<List<ErpStock>>>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<ErpStock>>>, string, CancellationToken>((op, _, ct) => op(ct));
        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<List<EshopStock>>>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<EshopStock>>>, string, CancellationToken>((op, _, ct) => op(ct));

        var cacheStore = new CatalogCacheStore(
            _cache,
            _timeProviderMock.Object,
            Options.Create(new CatalogCacheOptions { EnableBackgroundMerge = false }),
            _mergeSchedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());

        var mergeService = new CatalogMergeService(
            cacheStore,
            _mergeSchedulerMock.Object,
            _timeProviderMock.Object,
            Mock.Of<ILogger<CatalogMergeService>>());

        var refreshService = new CatalogDataRefreshService(
            _salesClientMock.Object,
            _attributesClientMock.Object,
            _eshopStockClientMock.Object,
            _consumedMaterialClientMock.Object,
            _purchaseHistoryClientMock.Object,
            _erpStockClientMock.Object,
            _lotsClientMock.Object,
            _productPriceEshopClientMock.Object,
            _productPriceErpClientMock.Object,
            _productEshopUrlClientMock.Object,
            _transportBoxRepositoryMock.Object,
            _stockTakingRepositoryMock.Object,
            _purchaseOrderRepositoryMock.Object,
            _manufactureOrderRepositoryMock.Object,
            _manufactureHistoryClientMock.Object,
            _manufactureDifficultyRepositoryMock.Object,
            _manufacturedInventoryRepositoryMock.Object,
            _resilienceServiceMock.Object,
            _timeProviderMock.Object,
            Options.Create(new DataSourceOptions
            {
                SalesHistoryDays = 30,
                PurchaseHistoryDays = 30,
                ConsumedHistoryDays = 30,
                ManufactureHistoryDays = 30,
            }),
            cacheStore,
            Mock.Of<ILogger<CatalogDataRefreshService>>());

        _repository = new CatalogRepository(
            cacheStore,
            mergeService,
            refreshService,
            _mergeSchedulerMock.Object);
    }

    [Fact]
    public async Task Merge_WithMatchingProductCodes_MapsErpPricesCorrectly()
    {
        // Arrange
        var erpStockData = new List<ErpStock>
        {
            new ErpStock { ProductCode = "PRODUCT001", ProductName = "Product 1", ProductId = 1, Stock = 10 },
            new ErpStock { ProductCode = "PRODUCT002", ProductName = "Product 2", ProductId = 2, Stock = 20 }
        };

        var erpPriceData = new List<ProductPriceErp>
        {
            new ProductPriceErp
            {
                ProductCode = "PRODUCT001",
                PriceWithoutVat = 100m,
                PriceWithVat = 121m,
                PurchasePrice = 80m,
                PurchasePriceWithVat = 96.8m
            },
            new ProductPriceErp
            {
                ProductCode = "PRODUCT002",
                PriceWithoutVat = 200m,
                PriceWithVat = 242m,
                PurchasePrice = 160m,
                PurchasePriceWithVat = 193.6m
            }
        };

        // Setup mocks to return empty data for everything except ERP stock and prices
        SetupEmptyMocks();
        _erpStockClientMock.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(erpStockData);
        _productPriceErpClientMock.Setup(x => x.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(erpPriceData);

        // Refresh data to populate cache
        await _repository.RefreshErpStockData(CancellationToken.None);
        await _repository.RefreshErpPricesData(CancellationToken.None);

        // Act
        var result = await _repository.GetAllAsync(CancellationToken.None);
        var resultList = result.ToList();

        // Assert
        resultList.Should().HaveCount(2);

        var product1 = resultList.First(p => p.ProductCode == "PRODUCT001");
        var product2 = resultList.First(p => p.ProductCode == "PRODUCT002");

        // Verify ERP prices are correctly mapped
        product1.ErpPrice.Should().NotBeNull();
        product1.ErpPrice!.ProductCode.Should().Be("PRODUCT001");
        product1.ErpPrice.PriceWithoutVat.Should().Be(100m);
        product1.ErpPrice.PriceWithVat.Should().Be(121m);
        product1.ErpPrice.PurchasePrice.Should().Be(80m);
        product1.ErpPrice.PurchasePriceWithVat.Should().Be(96.8m);

        product2.ErpPrice.Should().NotBeNull();
        product2.ErpPrice!.ProductCode.Should().Be("PRODUCT002");
        product2.ErpPrice.PriceWithoutVat.Should().Be(200m);
        product2.ErpPrice.PriceWithVat.Should().Be(242m);
        product2.ErpPrice.PurchasePrice.Should().Be(160m);
        product2.ErpPrice.PurchasePriceWithVat.Should().Be(193.6m);
    }

    [Fact]
    public async Task Merge_WithMatchingProductCodes_MapsEshopPricesCorrectly()
    {
        // Arrange
        var erpStockData = new List<ErpStock>
        {
            new ErpStock { ProductCode = "PRODUCT001", ProductName = "Product 1", ProductId = 1, Stock = 10 },
            new ErpStock { ProductCode = "PRODUCT002", ProductName = "Product 2", ProductId = 2, Stock = 20 }
        };

        var eshopPriceData = new List<ProductPriceEshop>
        {
            new ProductPriceEshop
            {
                ProductCode = "PRODUCT001",
                PriceWithVat = 125m,
                PurchasePrice = 85m
            },
            new ProductPriceEshop
            {
                ProductCode = "PRODUCT002",
                PriceWithVat = 250m,
                PurchasePrice = 170m
            }
        };

        // Setup mocks
        SetupEmptyMocks();
        _erpStockClientMock.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(erpStockData);
        _productPriceEshopClientMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(eshopPriceData);

        // Refresh data
        await _repository.RefreshErpStockData(CancellationToken.None);
        await _repository.RefreshEshopPricesData(CancellationToken.None);

        // Act
        var result = await _repository.GetAllAsync(CancellationToken.None);
        var resultList = result.ToList();

        // Assert
        resultList.Should().HaveCount(2);

        var product1 = resultList.First(p => p.ProductCode == "PRODUCT001");
        var product2 = resultList.First(p => p.ProductCode == "PRODUCT002");

        // Verify Eshop prices are correctly mapped
        product1.EshopPrice.Should().NotBeNull();
        product1.EshopPrice!.ProductCode.Should().Be("PRODUCT001");
        product1.EshopPrice.PriceWithVat.Should().Be(125m);
        product1.EshopPrice.PurchasePrice.Should().Be(85m);

        product2.EshopPrice.Should().NotBeNull();
        product2.EshopPrice!.ProductCode.Should().Be("PRODUCT002");
        product2.EshopPrice.PriceWithVat.Should().Be(250m);
        product2.EshopPrice.PurchasePrice.Should().Be(170m);
    }

    [Fact]
    public async Task Merge_WithNonMatchingProductCodes_DoesNotAssignPrices()
    {
        // Arrange
        var erpStockData = new List<ErpStock>
        {
            new ErpStock { ProductCode = "PRODUCT001", ProductName = "Product 1", ProductId = 1, Stock = 10 },
            new ErpStock { ProductCode = "PRODUCT002", ProductName = "Product 2", ProductId = 2, Stock = 20 }
        };

        var erpPriceData = new List<ProductPriceErp>
        {
            new ProductPriceErp
            {
                ProductCode = "DIFFERENT_PRODUCT",
                PriceWithoutVat = 100m,
                PriceWithVat = 121m,
                PurchasePrice = 80m,
                PurchasePriceWithVat = 96.8m
            }
        };

        var eshopPriceData = new List<ProductPriceEshop>
        {
            new ProductPriceEshop
            {
                ProductCode = "ANOTHER_DIFFERENT_PRODUCT",
                PriceWithVat = 125m,
                PurchasePrice = 85m
            }
        };

        // Setup mocks
        SetupEmptyMocks();
        _erpStockClientMock.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(erpStockData);
        _productPriceErpClientMock.Setup(x => x.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(erpPriceData);
        _productPriceEshopClientMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(eshopPriceData);

        // Refresh data
        await _repository.RefreshErpStockData(CancellationToken.None);
        await _repository.RefreshErpPricesData(CancellationToken.None);
        await _repository.RefreshEshopPricesData(CancellationToken.None);

        // Act
        var result = await _repository.GetAllAsync(CancellationToken.None);
        var resultList = result.ToList();

        // Assert
        resultList.Should().HaveCount(2);

        var product1 = resultList.First(p => p.ProductCode == "PRODUCT001");
        var product2 = resultList.First(p => p.ProductCode == "PRODUCT002");

        // Verify no prices are assigned due to non-matching ProductCodes
        product1.ErpPrice.Should().BeNull("No ERP price should be assigned for non-matching ProductCode");
        product1.EshopPrice.Should().BeNull("No Eshop price should be assigned for non-matching ProductCode");

        product2.ErpPrice.Should().BeNull("No ERP price should be assigned for non-matching ProductCode");
        product2.EshopPrice.Should().BeNull("No Eshop price should be assigned for non-matching ProductCode");
    }

    [Fact]
    public async Task Merge_WithPartialMatches_AssignsOnlyMatchingPrices()
    {
        // Arrange
        var erpStockData = new List<ErpStock>
        {
            new ErpStock { ProductCode = "PRODUCT001", ProductName = "Product 1", ProductId = 1, Stock = 10 },
            new ErpStock { ProductCode = "PRODUCT002", ProductName = "Product 2", ProductId = 2, Stock = 20 }
        };

        var erpPriceData = new List<ProductPriceErp>
        {
            new ProductPriceErp
            {
                ProductCode = "PRODUCT001", // Only matches first product
                PriceWithoutVat = 100m,
                PriceWithVat = 121m,
                PurchasePrice = 80m,
                PurchasePriceWithVat = 96.8m
            }
        };

        var eshopPriceData = new List<ProductPriceEshop>
        {
            new ProductPriceEshop
            {
                ProductCode = "PRODUCT002", // Only matches second product
                PriceWithVat = 250m,
                PurchasePrice = 170m
            }
        };

        // Setup mocks
        SetupEmptyMocks();
        _erpStockClientMock.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(erpStockData);
        _productPriceErpClientMock.Setup(x => x.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(erpPriceData);
        _productPriceEshopClientMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(eshopPriceData);

        // Refresh data
        await _repository.RefreshErpStockData(CancellationToken.None);
        await _repository.RefreshErpPricesData(CancellationToken.None);
        await _repository.RefreshEshopPricesData(CancellationToken.None);

        // Act
        var result = await _repository.GetAllAsync(CancellationToken.None);
        var resultList = result.ToList();

        // Assert
        resultList.Should().HaveCount(2);

        var product1 = resultList.First(p => p.ProductCode == "PRODUCT001");
        var product2 = resultList.First(p => p.ProductCode == "PRODUCT002");

        // Product1 should have ERP price but no Eshop price
        product1.ErpPrice.Should().NotBeNull("Product1 should have matching ERP price");
        product1.ErpPrice!.ProductCode.Should().Be("PRODUCT001");
        product1.EshopPrice.Should().BeNull("Product1 should not have matching Eshop price");

        // Product2 should have Eshop price but no ERP price
        product2.EshopPrice.Should().NotBeNull("Product2 should have matching Eshop price");
        product2.EshopPrice!.ProductCode.Should().Be("PRODUCT002");
        product2.ErpPrice.Should().BeNull("Product2 should not have matching ERP price");
    }

    [Fact]
    public async Task Merge_WithEmptyPriceData_DoesNotAssignAnyPrices()
    {
        // Arrange
        var erpStockData = new List<ErpStock>
        {
            new ErpStock { ProductCode = "PRODUCT001", ProductName = "Product 1", ProductId = 1, Stock = 10 }
        };

        var emptyErpPriceData = new List<ProductPriceErp>();
        var emptyEshopPriceData = new List<ProductPriceEshop>();

        // Setup mocks
        SetupEmptyMocks();
        _erpStockClientMock.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(erpStockData);
        _productPriceErpClientMock.Setup(x => x.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyErpPriceData);
        _productPriceEshopClientMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyEshopPriceData);

        // Refresh data
        await _repository.RefreshErpStockData(CancellationToken.None);
        await _repository.RefreshErpPricesData(CancellationToken.None);
        await _repository.RefreshEshopPricesData(CancellationToken.None);

        // Act
        var result = await _repository.GetAllAsync(CancellationToken.None);
        var resultList = result.ToList();

        // Assert
        resultList.Should().HaveCount(1);

        var product = resultList.First();
        product.ErpPrice.Should().BeNull("No ERP prices available");
        product.EshopPrice.Should().BeNull("No Eshop prices available");
    }

    [Fact]
    public async Task RefreshReserveData_WithQuarantineBoxes_PopulatesQuarantineStock()
    {
        // Arrange
        SetupEmptyMocks();

        // Ensure the product exists in catalog
        _erpStockClientMock.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new ErpStock { ProductCode = "TEST001", ProductName = "Test Product", ProductId = 1, Stock = 0 }
            });
        await _repository.RefreshErpStockData(CancellationToken.None);

        // Create a transport box in Quarantine state with an item
        var quarantineBox = new TransportBox();
        quarantineBox.Open("B001", DateTime.UtcNow, "user");
        quarantineBox.ToQuarantine(DateTime.UtcNow, "user");
        var itemsField = typeof(TransportBox).GetField("_items",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var item = (TransportBoxItem)Activator.CreateInstance(
            typeof(TransportBoxItem), "TEST001", "Test Product", 15.0, DateTime.UtcNow, "user", null, null, null)!;
        ((List<TransportBoxItem>)itemsField.GetValue(quarantineBox)!).Add(item);

        // Use SetupSequence: 1st call (reserve) returns empty, 2nd call (quarantine) returns the box
        _transportBoxRepositoryMock
            .SetupSequence(x => x.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<TransportBox, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>())
            .ReturnsAsync(new List<TransportBox> { quarantineBox });

        // Act
        await _repository.RefreshReserveData(CancellationToken.None);
        var products = await _repository.GetAllAsync(CancellationToken.None);

        // Assert
        var product = products.FirstOrDefault(p => p.ProductCode == "TEST001");
        product.Should().NotBeNull();
        product!.Stock.Quarantine.Should().Be(15);
    }

    [Fact]
    public async Task QuarantineLoadDate_IsNullBeforeRefresh_AndSetAfterRefresh()
    {
        // Arrange
        SetupEmptyMocks();
        _repository.QuarantineLoadDate.Should().BeNull(); // before any refresh

        // Use SetupSequence to handle both FindAsync calls (reserve + quarantine)
        _transportBoxRepositoryMock
            .SetupSequence(x => x.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<TransportBox, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>())
            .ReturnsAsync(new List<TransportBox>());

        // Act
        await _repository.RefreshReserveData(CancellationToken.None);

        // Assert
        _repository.QuarantineLoadDate.Should().NotBeNull();
    }

    private void SetupEmptyMocks()
    {
        _salesClientMock.Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogSaleRecord>());
        _attributesClientMock.Setup(x => x.GetAttributesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAttributes>());
        _eshopStockClientMock.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock>());
        _consumedMaterialClientMock.Setup(x => x.GetConsumedAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsumedMaterialRecord>());
        _purchaseHistoryClientMock.Setup(x => x.GetHistoryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogPurchaseRecord>());
        _lotsClientMock.Setup(x => x.GetAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogLot>());
        _stockTakingRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>());
        _transportBoxRepositoryMock.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<TransportBox, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());
    }
}