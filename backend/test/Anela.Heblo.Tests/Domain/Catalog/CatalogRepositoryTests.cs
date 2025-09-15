using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
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
    private readonly Mock<ICatalogSalesClient> _salesClientMock;
    private readonly Mock<ICatalogAttributesClient> _attributesClientMock;
    private readonly Mock<IEshopStockClient> _eshopStockClientMock;
    private readonly Mock<IConsumedMaterialsClient> _consumedMaterialClientMock;
    private readonly Mock<IPurchaseHistoryClient> _purchaseHistoryClientMock;
    private readonly Mock<IErpStockClient> _erpStockClientMock;
    private readonly Mock<ILotsClient> _lotsClientMock;
    private readonly Mock<IProductPriceEshopClient> _productPriceEshopClientMock;
    private readonly Mock<IProductPriceErpClient> _productPriceErpClientMock;
    private readonly Mock<ITransportBoxRepository> _transportBoxRepositoryMock;
    private readonly Mock<IStockTakingRepository> _stockTakingRepositoryMock;
    private readonly Mock<IManufactureRepository> _manufactureRepositoryMock;
    private readonly Mock<IPurchaseOrderRepository> _purchaseOrderRepositoryMock;
    private readonly Mock<IManufactureHistoryClient> _manufactureHistoryClientMock;
    private readonly Mock<IManufactureCostCalculationService> _manufactureCostCalculationServiceMock;
    private readonly Mock<IManufactureDifficultyRepository> _manufactureDifficultyRepositoryMock;
    private readonly Mock<ICatalogResilienceService> _resilienceServiceMock;
    private readonly Mock<ICatalogMergeScheduler> _mergeSchedulerMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<IOptions<DataSourceOptions>> _optionsMock;
    private readonly Mock<IOptions<CatalogCacheOptions>> _cacheOptionsMock;
    private readonly Mock<ILogger<CatalogRepository>> _loggerMock;

    private readonly CatalogRepository _repository;

    public CatalogRepositoryTests()
    {
        _salesClientMock = new Mock<ICatalogSalesClient>();
        _attributesClientMock = new Mock<ICatalogAttributesClient>();
        _eshopStockClientMock = new Mock<IEshopStockClient>();
        _consumedMaterialClientMock = new Mock<IConsumedMaterialsClient>();
        _purchaseHistoryClientMock = new Mock<IPurchaseHistoryClient>();
        _erpStockClientMock = new Mock<IErpStockClient>();
        _lotsClientMock = new Mock<ILotsClient>();
        _productPriceEshopClientMock = new Mock<IProductPriceEshopClient>();
        _productPriceErpClientMock = new Mock<IProductPriceErpClient>();
        _transportBoxRepositoryMock = new Mock<ITransportBoxRepository>();
        _stockTakingRepositoryMock = new Mock<IStockTakingRepository>();
        _manufactureRepositoryMock = new Mock<IManufactureRepository>();
        _purchaseOrderRepositoryMock = new Mock<IPurchaseOrderRepository>();
        _manufactureHistoryClientMock = new Mock<IManufactureHistoryClient>();
        _manufactureCostCalculationServiceMock = new Mock<IManufactureCostCalculationService>();
        _manufactureDifficultyRepositoryMock = new Mock<IManufactureDifficultyRepository>();
        _resilienceServiceMock = new Mock<ICatalogResilienceService>();
        _mergeSchedulerMock = new Mock<ICatalogMergeScheduler>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _timeProviderMock = new Mock<TimeProvider>();
        _optionsMock = new Mock<IOptions<DataSourceOptions>>();
        _cacheOptionsMock = new Mock<IOptions<CatalogCacheOptions>>();
        _loggerMock = new Mock<ILogger<CatalogRepository>>();

        var options = new DataSourceOptions
        {
            SalesHistoryDays = 30,
            PurchaseHistoryDays = 30,
            ConsumedHistoryDays = 30,
            ManufactureHistoryDays = 30
        };
        _optionsMock.Setup(x => x.Value).Returns(options);

        var cacheOptions = new CatalogCacheOptions
        {
            EnableBackgroundMerge = false // Disable for tests to use old behavior
        };
        _cacheOptionsMock.Setup(x => x.Value).Returns(cacheOptions);

        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(DateTimeOffset.UtcNow);

        // Setup resilience service to pass through operations without resilience patterns for testing
        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(It.IsAny<Func<CancellationToken, Task<IEnumerable<CatalogSaleRecord>>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IEnumerable<CatalogSaleRecord>>>, string, CancellationToken>((operation, name, ct) => operation(ct));

        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(It.IsAny<Func<CancellationToken, Task<IEnumerable<CatalogAttributes>>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IEnumerable<CatalogAttributes>>>, string, CancellationToken>((operation, name, ct) => operation(ct));

        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(It.IsAny<Func<CancellationToken, Task<List<ErpStock>>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<ErpStock>>>, string, CancellationToken>((operation, name, ct) => operation(ct));

        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(It.IsAny<Func<CancellationToken, Task<List<EshopStock>>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<EshopStock>>>, string, CancellationToken>((operation, name, ct) => operation(ct));

        _repository = new CatalogRepository(
            _salesClientMock.Object,
            _attributesClientMock.Object,
            _eshopStockClientMock.Object,
            _consumedMaterialClientMock.Object,
            _purchaseHistoryClientMock.Object,
            _erpStockClientMock.Object,
            _lotsClientMock.Object,
            _productPriceEshopClientMock.Object,
            _productPriceErpClientMock.Object,
            _transportBoxRepositoryMock.Object,
            _stockTakingRepositoryMock.Object,
            _manufactureRepositoryMock.Object,
            _purchaseOrderRepositoryMock.Object,
            _manufactureHistoryClientMock.Object,
            _manufactureCostCalculationServiceMock.Object,
            _manufactureDifficultyRepositoryMock.Object,
            _resilienceServiceMock.Object,
            _mergeSchedulerMock.Object,
            _cache,
            _timeProviderMock.Object,
            _optionsMock.Object,
            _cacheOptionsMock.Object,
            _loggerMock.Object);
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