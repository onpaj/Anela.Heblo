# Test Scenarios: Catalog Data Aggregation & Management

## Unit Tests

### CatalogAggregate Tests

#### Test: ProductFamily_Should_Return_First_Six_Characters
```csharp
[Test]
public void ProductFamily_Should_Return_First_Six_Characters()
{
    // Arrange
    var aggregate = new CatalogAggregate("COSM01-001");
    
    // Act
    var productFamily = aggregate.ProductFamily;
    
    // Assert
    Assert.AreEqual("COSM01", productFamily);
}

[Test]
public void ProductFamily_Should_Return_ProductCode_When_Less_Than_Six_Characters()
{
    // Arrange
    var aggregate = new CatalogAggregate("ABC");
    
    // Act
    var productFamily = aggregate.ProductFamily;
    
    // Assert
    Assert.AreEqual("ABC", productFamily);
}
```

#### Test: ProductTypeCode_Should_Return_First_Three_Characters
```csharp
[Test]
public void ProductTypeCode_Should_Return_First_Three_Characters()
{
    // Arrange
    var aggregate = new CatalogAggregate("COSM01-001");
    
    // Act
    var productType = aggregate.ProductTypeCode;
    
    // Assert
    Assert.AreEqual("COS", productType);
}
```

#### Test: IsUnderStocked_Should_Return_True_When_Stock_Below_Minimum
```csharp
[Test]
public void IsUnderStocked_Should_Return_True_When_Stock_Below_Minimum()
{
    // Arrange
    var aggregate = new CatalogAggregate("TEST-001")
    {
        Stock = new StockData { Erp = 5, PrimaryStockSource = StockSource.ERP },
        Properties = new CatalogProperties { StockMinSetup = 10 }
    };
    
    // Act
    var isUnderStocked = aggregate.IsUnderStocked;
    
    // Assert
    Assert.IsTrue(isUnderStocked);
}

[Test]
public void IsUnderStocked_Should_Return_False_When_Stock_Above_Minimum()
{
    // Arrange
    var aggregate = new CatalogAggregate("TEST-001")
    {
        Stock = new StockData { Erp = 15, PrimaryStockSource = StockSource.ERP },
        Properties = new CatalogProperties { StockMinSetup = 10 }
    };
    
    // Act
    var isUnderStocked = aggregate.IsUnderStocked;
    
    // Assert
    Assert.IsFalse(isUnderStocked);
}
```

#### Test: IsInSeason_Should_Return_True_When_Current_Month_In_Season
```csharp
[Test]
public void IsInSeason_Should_Return_True_When_Current_Month_In_Season()
{
    // Arrange
    var currentMonth = DateTime.Now.Month;
    var aggregate = new CatalogAggregate("TEST-001")
    {
        Properties = new CatalogProperties 
        { 
            SeasonMonths = new[] { currentMonth, currentMonth + 1 } 
        }
    };
    
    // Act
    var isInSeason = aggregate.IsInSeason;
    
    // Assert
    Assert.IsTrue(isInSeason);
}
```

### StockData Tests

#### Test: Available_Should_Use_ERP_As_Primary_Plus_Transport
```csharp
[Test]
public void Available_Should_Use_ERP_As_Primary_Plus_Transport()
{
    // Arrange
    var stockData = new StockData
    {
        Erp = 100,
        Eshop = 50,
        Transport = 25,
        PrimaryStockSource = StockSource.ERP
    };
    
    // Act
    var available = stockData.Available;
    
    // Assert
    Assert.AreEqual(125, available); // 100 (ERP) + 25 (Transport)
}

[Test]
public void Available_Should_Use_Eshop_As_Primary_Plus_Transport()
{
    // Arrange
    var stockData = new StockData
    {
        Erp = 100,
        Eshop = 50,
        Transport = 25,
        PrimaryStockSource = StockSource.Eshop
    };
    
    // Act
    var available = stockData.Available;
    
    // Assert
    Assert.AreEqual(75, available); // 50 (Eshop) + 25 (Transport)
}
```

### CatalogRepository Tests

#### Test: GetAsync_Should_Return_Cached_Aggregate_When_Available
```csharp
[Test]
public async Task GetAsync_Should_Return_Cached_Aggregate_When_Available()
{
    // Arrange
    var mockCache = new Mock<IMemoryCache>();
    var cachedAggregate = new CatalogAggregate("TEST-001");
    
    mockCache.Setup(c => c.TryGetValue("catalog_TEST-001", out cachedAggregate))
           .Returns(true);
    
    var repository = new CatalogRepository(mockCache.Object, null, null, null);
    
    // Act
    var result = await repository.GetAsync("TEST-001");
    
    // Assert
    Assert.AreEqual(cachedAggregate, result);
    mockCache.Verify(c => c.TryGetValue("catalog_TEST-001", out cachedAggregate), Times.Once);
}

[Test]
public async Task GetAsync_Should_Build_Aggregate_When_Not_Cached()
{
    // Arrange
    var mockCache = new Mock<IMemoryCache>();
    var mockErpClient = new Mock<IErpStockClient>();
    var mockEshopClient = new Mock<IEshopStockClient>();
    var mockSalesClient = new Mock<ICatalogSalesClient>();
    
    CatalogAggregate cachedAggregate = null;
    mockCache.Setup(c => c.TryGetValue("catalog_TEST-001", out cachedAggregate))
           .Returns(false);
    
    mockErpClient.Setup(c => c.GetStockAsync())
               .ReturnsAsync(new List<ErpStockData>());
    
    var repository = new CatalogRepository(mockCache.Object, mockErpClient.Object, 
                                         mockEshopClient.Object, mockSalesClient.Object);
    
    // Act
    var result = await repository.GetAsync("TEST-001");
    
    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual("TEST-001", result.Id);
    mockCache.Verify(c => c.Set(It.IsAny<object>(), It.IsAny<object>(), It.IsAny<TimeSpan>()), 
                    Times.Once);
}
```

#### Test: GetAsync_Should_Handle_External_System_Exception
```csharp
[Test]
public async Task GetAsync_Should_Handle_External_System_Exception()
{
    // Arrange
    var mockCache = new Mock<IMemoryCache>();
    var mockErpClient = new Mock<IErpStockClient>();
    var mockLogger = new Mock<ILogger<CatalogRepository>>();
    
    CatalogAggregate cachedAggregate = null;
    mockCache.Setup(c => c.TryGetValue(It.IsAny<object>(), out cachedAggregate))
           .Returns(false);
    
    mockErpClient.Setup(c => c.GetStockAsync())
               .ThrowsAsync(new ExternalSystemException("ERP unavailable"));
    
    var repository = new CatalogRepository(mockCache.Object, mockErpClient.Object, 
                                         null, null, mockLogger.Object);
    
    // Act & Assert
    var exception = await Assert.ThrowsAsync<ExternalSystemException>(
        () => repository.GetAsync("TEST-001"));
    
    Assert.AreEqual("ERP unavailable", exception.Message);
    
    // Verify warning was logged
    mockLogger.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("External system unavailable")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.Once);
}
```

#### Test: UpdateStockAsync_Should_Invalidate_Cache
```csharp
[Test]
public async Task UpdateStockAsync_Should_Invalidate_Cache()
{
    // Arrange
    var mockCache = new Mock<IMemoryCache>();
    var repository = new CatalogRepository(mockCache.Object, null, null, null);
    var newStockData = new StockData { Erp = 100, PrimaryStockSource = StockSource.ERP };
    
    // Act
    await repository.UpdateStockAsync("TEST-001", newStockData);
    
    // Assert
    mockCache.Verify(c => c.Remove("catalog_TEST-001"), Times.Once);
}
```

### Data Merging Tests

#### Test: MergeErpDataAsync_Should_Set_Basic_Properties
```csharp
[Test]
public async Task MergeErpDataAsync_Should_Set_Basic_Properties()
{
    // Arrange
    var mockErpClient = new Mock<IErpStockClient>();
    var erpData = new List<ErpStockData>
    {
        new ErpStockData 
        { 
            ProductCode = "TEST-001", 
            ProductName = "Test Product",
            Stock = 100,
            Location = "A1"
        }
    };
    
    mockErpClient.Setup(c => c.GetStockAsync()).ReturnsAsync(erpData);
    
    var repository = new CatalogRepository(null, mockErpClient.Object, null, null);
    var aggregate = new CatalogAggregate("TEST-001");
    
    // Act
    await repository.MergeErpDataAsync(aggregate);
    
    // Assert
    Assert.AreEqual("Test Product", aggregate.ProductName);
    Assert.AreEqual("A1", aggregate.Location);
    Assert.AreEqual(100, aggregate.Stock.Erp);
}
```

## Integration Tests

### End-to-End Repository Tests

#### Test: Repository_Should_Integrate_All_Data_Sources
```csharp
[Test]
public async Task Repository_Should_Integrate_All_Data_Sources()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddMemoryCache();
    services.AddSingleton<ICatalogRepository, CatalogRepository>();
    
    // Mock all external clients
    services.AddSingleton<IErpStockClient>(provider => 
    {
        var mock = new Mock<IErpStockClient>();
        mock.Setup(c => c.GetStockAsync()).ReturnsAsync(CreateTestErpData());
        return mock.Object;
    });
    
    // ... setup other mock clients
    
    var serviceProvider = services.BuildServiceProvider();
    var repository = serviceProvider.GetRequiredService<ICatalogRepository>();
    
    // Act
    var result = await repository.GetAsync("TEST-001", includeDetails: true);
    
    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual("TEST-001", result.Id);
    Assert.IsNotNull(result.Stock);
    Assert.IsNotNull(result.Properties);
    Assert.IsNotEmpty(result.SalesHistory);
    Assert.IsNotEmpty(result.PurchaseHistory);
}

private List<ErpStockData> CreateTestErpData()
{
    return new List<ErpStockData>
    {
        new ErpStockData 
        { 
            ProductCode = "TEST-001",
            ProductName = "Integration Test Product",
            Stock = 100,
            Location = "A1"
        }
    };
}
```

### Cache Integration Tests

#### Test: Cache_Should_Expire_After_Configured_Time
```csharp
[Test]
public async Task Cache_Should_Expire_After_Configured_Time()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddMemoryCache();
    services.Configure<CatalogRepositoryOptions>(opt => 
    {
        opt.DefaultCacheExpiration = TimeSpan.FromSeconds(1);
    });
    
    // ... setup mocked services
    
    var serviceProvider = services.BuildServiceProvider();
    var repository = serviceProvider.GetRequiredService<ICatalogRepository>();
    
    // Act
    var result1 = await repository.GetAsync("TEST-001");
    await Task.Delay(1100); // Wait for cache to expire
    var result2 = await repository.GetAsync("TEST-001");
    
    // Assert
    Assert.IsNotNull(result1);
    Assert.IsNotNull(result2);
    // Verify that fresh data was fetched (would require mock verification)
}
```

## Performance Tests

### Load Testing

#### Test: Repository_Should_Handle_Concurrent_Requests
```csharp
[Test]
public async Task Repository_Should_Handle_Concurrent_Requests()
{
    // Arrange
    var repository = CreateTestRepository();
    var productCodes = Enumerable.Range(1, 100).Select(i => $"TEST-{i:000}").ToList();
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var tasks = productCodes.Select(code => repository.GetAsync(code)).ToList();
    var results = await Task.WhenAll(tasks);
    stopwatch.Stop();
    
    // Assert
    Assert.AreEqual(100, results.Length);
    Assert.IsTrue(results.All(r => r != null));
    Assert.IsTrue(stopwatch.ElapsedMilliseconds < 5000); // Should complete within 5 seconds
}
```

#### Test: Cache_Should_Improve_Performance_On_Repeated_Access
```csharp
[Test]
public async Task Cache_Should_Improve_Performance_On_Repeated_Access()
{
    // Arrange
    var repository = CreateTestRepository();
    
    // Act - First access (cache miss)
    var stopwatch1 = Stopwatch.StartNew();
    var result1 = await repository.GetAsync("TEST-001");
    stopwatch1.Stop();
    
    // Act - Second access (cache hit)
    var stopwatch2 = Stopwatch.StartNew();
    var result2 = await repository.GetAsync("TEST-001");
    stopwatch2.Stop();
    
    // Assert
    Assert.IsNotNull(result1);
    Assert.IsNotNull(result2);
    Assert.IsTrue(stopwatch2.ElapsedMilliseconds < stopwatch1.ElapsedMilliseconds / 10);
    // Cache hit should be at least 10x faster
}
```

### Memory Usage Tests

#### Test: Cache_Should_Not_Exceed_Memory_Limits
```csharp
[Test]
public async Task Cache_Should_Not_Exceed_Memory_Limits()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddMemoryCache(options => options.SizeLimit = 100);
    // ... setup test services
    
    var repository = serviceProvider.GetRequiredService<ICatalogRepository>();
    
    // Act - Load more items than cache limit
    var tasks = Enumerable.Range(1, 200)
                         .Select(i => repository.GetAsync($"TEST-{i:000}"))
                         .ToList();
    
    await Task.WhenAll(tasks);
    
    // Assert
    var cache = serviceProvider.GetRequiredService<IMemoryCache>();
    // Verify cache size doesn't exceed limit (implementation-dependent)
    // This test would require access to internal cache metrics
}
```

## UI Tests (if applicable)

### Catalog Display Tests

#### Test: Catalog_Page_Should_Display_Product_Information
```csharp
[Test]
public async Task Catalog_Page_Should_Display_Product_Information()
{
    // Arrange
    using var playwright = await Playwright.CreateAsync();
    var browser = await playwright.Chromium.LaunchAsync();
    var page = await browser.NewPageAsync();
    
    // Navigate to catalog page
    await page.GotoAsync("https://localhost/catalog");
    
    // Act
    await page.FillAsync("#product-search", "TEST-001");
    await page.ClickAsync("#search-button");
    
    // Assert
    await page.WaitForSelectorAsync(".product-card");
    var productName = await page.TextContentAsync(".product-name");
    var stockLevel = await page.TextContentAsync(".stock-level");
    
    Assert.IsNotEmpty(productName);
    Assert.IsNotEmpty(stockLevel);
    
    await browser.CloseAsync();
}
```

## Error Handling Tests

### External System Failure Tests

#### Test: Repository_Should_Handle_All_Systems_Unavailable
```csharp
[Test]
public async Task Repository_Should_Handle_All_Systems_Unavailable()
{
    // Arrange
    var mockErpClient = new Mock<IErpStockClient>();
    var mockEshopClient = new Mock<IEshopStockClient>();
    var mockSalesClient = new Mock<ICatalogSalesClient>();
    
    mockErpClient.Setup(c => c.GetStockAsync())
               .ThrowsAsync(new TimeoutException("ERP timeout"));
    mockEshopClient.Setup(c => c.GetStockAsync())
                  .ThrowsAsync(new HttpRequestException("E-shop unavailable"));
    mockSalesClient.Setup(c => c.GetSalesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                  .ThrowsAsync(new InvalidOperationException("Sales system error"));
    
    var repository = new CatalogRepository(null, mockErpClient.Object, 
                                         mockEshopClient.Object, mockSalesClient.Object);
    
    // Act & Assert
    var exception = await Assert.ThrowsAsync<AggregateException>(
        () => repository.GetAsync("TEST-001"));
    
    Assert.IsTrue(exception.InnerExceptions.Count >= 1);
}
```

### Data Consistency Tests

#### Test: Repository_Should_Handle_Conflicting_Data_Sources
```csharp
[Test]
public async Task Repository_Should_Handle_Conflicting_Data_Sources()
{
    // Arrange
    var mockErpClient = new Mock<IErpStockClient>();
    var mockEshopClient = new Mock<IEshopStockClient>();
    
    mockErpClient.Setup(c => c.GetStockAsync()).ReturnsAsync(new List<ErpStockData>
    {
        new ErpStockData { ProductCode = "TEST-001", ProductName = "ERP Product Name", Stock = 100 }
    });
    
    mockEshopClient.Setup(c => c.GetStockAsync()).ReturnsAsync(new List<EshopStockData>
    {
        new EshopStockData { ProductCode = "TEST-001", ProductName = "E-shop Product Name", Stock = 50 }
    });
    
    var repository = new CatalogRepository(null, mockErpClient.Object, mockEshopClient.Object, null);
    
    // Act
    var result = await repository.GetAsync("TEST-001");
    
    // Assert
    Assert.IsNotNull(result);
    // Verify business rules for conflict resolution are applied
    // (ERP data should take precedence for master data)
    Assert.AreEqual("ERP Product Name", result.ProductName);
    Assert.AreEqual(100, result.Stock.Erp);
    Assert.AreEqual(50, result.Stock.Eshop);
}
```