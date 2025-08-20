using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.FinancialOverview;

public class StockValueServiceTests
{
    private readonly Mock<IErpStockClient> _stockClientMock;
    private readonly Mock<IProductPriceErpClient> _priceClientMock;
    private readonly Mock<ILogger<StockValueService>> _loggerMock;
    private readonly StockValueService _service;

    public StockValueServiceTests()
    {
        _stockClientMock = new Mock<IErpStockClient>();
        _priceClientMock = new Mock<IProductPriceErpClient>();
        _loggerMock = new Mock<ILogger<StockValueService>>();
        
        _service = new StockValueService(
            _stockClientMock.Object,
            _priceClientMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetStockValueChangesAsync_CalculatesCorrectStockValueChanges()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 2, 29); // 2 months
        
        // Setup price data
        var prices = new List<ProductPriceErp>
        {
            new() { ProductCode = "MAT001", PurchasePrice = 100m },
            new() { ProductCode = "SEMI001", PurchasePrice = 200m },
            new() { ProductCode = "PROD001", PurchasePrice = 300m }
        };
        _priceClientMock.Setup(x => x.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prices);

        // Setup stock data - January start vs end
        SetupStockDataForJanuary();
        
        // Setup stock data - February start vs end  
        SetupStockDataForFebruary();

        // Act
        var result = await _service.GetStockValueChangesAsync(startDate, endDate, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);
        
        // January changes
        var january = result.First(x => x.Month == 1 && x.Year == 2024);
        Assert.Equal(1000m, january.StockChanges.Materials); // (20-10) * 100 = 1000
        Assert.Equal(600m, january.StockChanges.SemiProducts); // (8-5) * 200 = 600
        Assert.Equal(900m, january.StockChanges.Products); // (7-4) * 300 = 900
        Assert.Equal(2500m, january.TotalStockValueChange);
        
        // February changes
        var february = result.First(x => x.Month == 2 && x.Year == 2024);
        Assert.Equal(-500m, february.StockChanges.Materials); // (15-20) * 100 = -500
        Assert.Equal(-400m, february.StockChanges.SemiProducts); // (6-8) * 200 = -400
        Assert.Equal(-300m, february.StockChanges.Products); // (6-7) * 300 = -300
        Assert.Equal(-1200m, february.TotalStockValueChange);
    }

    [Fact]
    public async Task GetStockValueChangesAsync_HandlesEmptyStockData()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 31);
        
        _priceClientMock.Setup(x => x.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>());
            
        _stockClientMock.Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>());

        // Act
        var result = await _service.GetStockValueChangesAsync(startDate, endDate, CancellationToken.None);

        // Assert
        Assert.Single(result);
        var january = result.First();
        Assert.Equal(0m, january.TotalStockValueChange);
    }

    [Fact]
    public async Task GetStockValueChangesAsync_HandlesMissingPriceData()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 31);
        
        // Only partial price data
        var prices = new List<ProductPriceErp>
        {
            new() { ProductCode = "MAT001", PurchasePrice = 100m }
            // Missing prices for SEMI001 and PROD001
        };
        _priceClientMock.Setup(x => x.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prices);

        // Stock data with all products
        var stockData = new List<ErpStock>
        {
            new() { ProductCode = "MAT001", Stock = 10m },
            new() { ProductCode = "SEMI001", Stock = 5m }, // No price data
            new() { ProductCode = "PROD001", Stock = 3m }  // No price data
        };
        
        _stockClientMock.Setup(x => x.StockToDateAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stockData);

        // Act
        var result = await _service.GetStockValueChangesAsync(startDate, endDate, CancellationToken.None);

        // Assert
        Assert.Single(result);
        var january = result.First();
        // Should only calculate value for MAT001 (which has price data)
        // Since start and end stock are the same, change should be 0
        Assert.Equal(0m, january.TotalStockValueChange);
    }

    private void SetupStockDataForJanuary()
    {
        var januaryStart = new DateTime(2024, 1, 1);
        var januaryEnd = new DateTime(2024, 1, 31);
        
        // Materials warehouse (ID 5) - January start
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryStart, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "MAT001", Stock = 10m }
            });
            
        // Materials warehouse (ID 5) - January end  
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryEnd, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "MAT001", Stock = 20m }
            });

        // Semi-products warehouse (ID 20) - January start
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryStart, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "SEMI001", Stock = 5m }
            });
            
        // Semi-products warehouse (ID 20) - January end
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryEnd, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "SEMI001", Stock = 8m }
            });

        // Products warehouse (ID 4) - January start
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryStart, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "PROD001", Stock = 4m }
            });
            
        // Products warehouse (ID 4) - January end
        _stockClientMock.Setup(x => x.StockToDateAsync(januaryEnd, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "PROD001", Stock = 7m }
            });
    }

    private void SetupStockDataForFebruary()
    {
        var februaryStart = new DateTime(2024, 2, 1);
        var februaryEnd = new DateTime(2024, 2, 29);
        
        // Materials warehouse (ID 5) - February start (same as January end)
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryStart, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "MAT001", Stock = 20m }
            });
            
        // Materials warehouse (ID 5) - February end
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryEnd, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "MAT001", Stock = 15m }
            });

        // Semi-products warehouse (ID 20) - February start (same as January end)
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryStart, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "SEMI001", Stock = 8m }
            });
            
        // Semi-products warehouse (ID 20) - February end
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryEnd, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "SEMI001", Stock = 6m }
            });

        // Products warehouse (ID 4) - February start (same as January end)
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryStart, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "PROD001", Stock = 7m }
            });
            
        // Products warehouse (ID 4) - February end
        _stockClientMock.Setup(x => x.StockToDateAsync(februaryEnd, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new() { ProductCode = "PROD001", Stock = 6m }
            });
    }
}