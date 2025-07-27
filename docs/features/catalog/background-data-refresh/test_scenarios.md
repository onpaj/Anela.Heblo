# Test Scenarios: Background Data Refresh Service

## Unit Tests

### CatalogDataRefresher Service Tests

#### Test: ExecuteAsync_Should_Start_All_Refresh_Tasks
```csharp
[Test]
public async Task ExecuteAsync_Should_Start_All_Refresh_Tasks()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    var mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
    var mockLogger = new Mock<ILogger<CatalogDataRefresher>>();
    var options = Microsoft.Extensions.Options.Options.Create(new CatalogRepositoryOptions
    {
        ErpStockRefreshInterval = TimeSpan.FromSeconds(1),
        EshopStockRefreshInterval = TimeSpan.FromSeconds(1)
    });
    
    var service = new CatalogDataRefresher(
        mockRepository.Object,
        mockServiceScopeFactory.Object,
        mockLogger.Object,
        options);
    
    var cancellationTokenSource = new CancellationTokenSource();
    
    // Act
    var executeTask = service.StartAsync(cancellationTokenSource.Token);
    await Task.Delay(TimeSpan.FromSeconds(2)); // Let it run briefly
    cancellationTokenSource.Cancel();
    
    // Assert
    await executeTask;
    
    // Verify that refresh methods were called
    mockLogger.Verify(
        x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Starting ERP stock refresh")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.AtLeastOnce);
}
```

#### Test: RefreshErpStockAsync_Should_Update_Repository_On_Success
```csharp
[Test]
public async Task RefreshErpStockAsync_Should_Update_Repository_On_Success()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    var mockServiceScope = new Mock<IServiceScope>();
    var mockServiceProvider = new Mock<IServiceProvider>();
    var mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
    var mockErpClient = new Mock<IErpStockClient>();
    var mockLogger = new Mock<ILogger<CatalogDataRefresher>>();
    
    var testStockData = new List<ErpStockData>
    {
        new ErpStockData { ProductCode = "TEST-001", Stock = 100 },
        new ErpStockData { ProductCode = "TEST-002", Stock = 50 }
    };
    
    mockErpClient.Setup(c => c.GetStockAsync()).ReturnsAsync(testStockData);
    mockServiceProvider.Setup(p => p.GetRequiredService<IErpStockClient>())
                      .Returns(mockErpClient.Object);
    mockServiceScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
    mockServiceScopeFactory.Setup(f => f.CreateScope()).Returns(mockServiceScope.Object);
    
    var options = Microsoft.Extensions.Options.Options.Create(new CatalogRepositoryOptions
    {
        ErpStockRefreshInterval = TimeSpan.FromMilliseconds(100)
    });
    
    var service = new CatalogDataRefresher(
        mockRepository.Object,
        mockServiceScopeFactory.Object,
        mockLogger.Object,
        options);
    
    var cancellationTokenSource = new CancellationTokenSource();
    
    // Act
    var refreshTask = service.RefreshErpStockAsync(cancellationTokenSource.Token);
    await Task.Delay(TimeSpan.FromMilliseconds(200)); // Let one refresh cycle complete
    cancellationTokenSource.Cancel();
    
    // Assert
    try { await refreshTask; } catch (OperationCanceledException) { }
    
    mockRepository.Verify(r => r.UpdateErpStockAsync(testStockData), Times.AtLeastOnce);
    mockLogger.Verify(
        x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Completed ERP stock refresh")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.AtLeastOnce);
}
```

#### Test: RefreshErpStockAsync_Should_Handle_Exception_And_Continue
```csharp
[Test]
public async Task RefreshErpStockAsync_Should_Handle_Exception_And_Continue()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    var mockServiceScope = new Mock<IServiceScope>();
    var mockServiceProvider = new Mock<IServiceProvider>();
    var mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
    var mockErpClient = new Mock<IErpStockClient>();
    var mockLogger = new Mock<ILogger<CatalogDataRefresher>>();
    
    // First call throws exception, second call succeeds
    mockErpClient.SetupSequence(c => c.GetStockAsync())
               .ThrowsAsync(new TimeoutException("ERP timeout"))
               .ReturnsAsync(new List<ErpStockData>());
    
    mockServiceProvider.Setup(p => p.GetRequiredService<IErpStockClient>())
                      .Returns(mockErpClient.Object);
    mockServiceScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
    mockServiceScopeFactory.Setup(f => f.CreateScope()).Returns(mockServiceScope.Object);
    
    var options = Microsoft.Extensions.Options.Options.Create(new CatalogRepositoryOptions
    {
        ErpStockRefreshInterval = TimeSpan.FromMilliseconds(100)
    });
    
    var service = new CatalogDataRefresher(
        mockRepository.Object,
        mockServiceScopeFactory.Object,
        mockLogger.Object,
        options);
    
    var cancellationTokenSource = new CancellationTokenSource();
    
    // Act
    var refreshTask = service.RefreshErpStockAsync(cancellationTokenSource.Token);
    await Task.Delay(TimeSpan.FromMilliseconds(300)); // Let multiple cycles run
    cancellationTokenSource.Cancel();
    
    // Assert
    try { await refreshTask; } catch (OperationCanceledException) { }
    
    // Verify error was logged
    mockLogger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error refreshing ERP stock data")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.AtLeastOnce);
    
    // Verify service continued after error
    mockErpClient.Verify(c => c.GetStockAsync(), Times.AtLeast(2));
}
```

#### Test: RefreshSalesDataAsync_Should_Use_Correct_Date_Range
```csharp
[Test]
public async Task RefreshSalesDataAsync_Should_Use_Correct_Date_Range()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    var mockServiceScope = new Mock<IServiceScope>();
    var mockServiceProvider = new Mock<IServiceProvider>();
    var mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
    var mockSalesClient = new Mock<ICatalogSalesClient>();
    var mockLogger = new Mock<ILogger<CatalogDataRefresher>>();
    
    mockSalesClient.Setup(c => c.GetSalesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                  .ReturnsAsync(new List<CatalogSales>());
    
    mockServiceProvider.Setup(p => p.GetRequiredService<ICatalogSalesClient>())
                      .Returns(mockSalesClient.Object);
    mockServiceScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
    mockServiceScopeFactory.Setup(f => f.CreateScope()).Returns(mockServiceScope.Object);
    
    var options = Microsoft.Extensions.Options.Options.Create(new CatalogRepositoryOptions
    {
        SalesDataRefreshInterval = TimeSpan.FromMilliseconds(100),
        SalesHistoryRetentionDays = 365
    });
    
    var service = new CatalogDataRefresher(
        mockRepository.Object,
        mockServiceScopeFactory.Object,
        mockLogger.Object,
        options);
    
    var cancellationTokenSource = new CancellationTokenSource();
    
    // Act
    var refreshTask = service.RefreshSalesDataAsync(cancellationTokenSource.Token);
    await Task.Delay(TimeSpan.FromMilliseconds(200));
    cancellationTokenSource.Cancel();
    
    // Assert
    try { await refreshTask; } catch (OperationCanceledException) { }
    
    mockSalesClient.Verify(
        c => c.GetSalesAsync(
            It.Is<DateTime>(d => d.Date == DateTime.Now.AddDays(-365).Date),
            It.Is<DateTime>(d => d.Date == DateTime.Now.Date)),
        Times.AtLeastOnce);
}
```

### Configuration Tests

#### Test: CatalogRepositoryOptions_Should_Have_Default_Values
```csharp
[Test]
public void CatalogRepositoryOptions_Should_Have_Default_Values()
{
    // Arrange & Act
    var options = new CatalogRepositoryOptions();
    
    // Assert
    Assert.AreEqual(TimeSpan.FromMinutes(5), options.ErpStockRefreshInterval);
    Assert.AreEqual(TimeSpan.FromMinutes(10), options.EshopStockRefreshInterval);
    Assert.AreEqual(TimeSpan.FromMinutes(30), options.SalesDataRefreshInterval);
    Assert.AreEqual(365, options.SalesHistoryRetentionDays);
    Assert.AreEqual(365, options.PurchaseHistoryRetentionDays);
    Assert.AreEqual(720, options.ConsumedMaterialsRetentionDays);
    Assert.AreEqual(3, options.MaxConcurrentRefreshes);
    Assert.AreEqual(TimeSpan.FromMinutes(10), options.RefreshTimeout);
    Assert.IsTrue(options.EnableMetrics);
}
```

#### Test: Options_Should_Be_Configurable_From_Configuration
```csharp
[Test]
public void Options_Should_Be_Configurable_From_Configuration()
{
    // Arrange
    var configData = new Dictionary<string, string>
    {
        ["CatalogRepository:ErpStockRefreshInterval"] = "00:02:00",
        ["CatalogRepository:SalesHistoryRetentionDays"] = "180",
        ["CatalogRepository:EnableMetrics"] = "false"
    };
    
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(configData)
        .Build();
    
    var services = new ServiceCollection();
    services.Configure<CatalogRepositoryOptions>(
        configuration.GetSection("CatalogRepository"));
    
    var serviceProvider = services.BuildServiceProvider();
    
    // Act
    var options = serviceProvider.GetRequiredService<IOptions<CatalogRepositoryOptions>>().Value;
    
    // Assert
    Assert.AreEqual(TimeSpan.FromMinutes(2), options.ErpStockRefreshInterval);
    Assert.AreEqual(180, options.SalesHistoryRetentionDays);
    Assert.IsFalse(options.EnableMetrics);
}
```

### Repository Bulk Update Tests

#### Test: UpdateErpStockAsync_Should_Update_Multiple_Products
```csharp
[Test]
public async Task UpdateErpStockAsync_Should_Update_Multiple_Products()
{
    // Arrange
    var mockCache = new Mock<IMemoryCache>();
    var repository = new CatalogRepository(mockCache.Object, null, null, null);
    
    var stockData = new List<ErpStockData>
    {
        new ErpStockData { ProductCode = "TEST-001", Stock = 100, Location = "A1" },
        new ErpStockData { ProductCode = "TEST-002", Stock = 50, Location = "B2" },
        new ErpStockData { ProductCode = "TEST-003", Stock = 75, Location = "C3" }
    };
    
    // Setup cached aggregates
    var cachedAggregates = stockData.ToDictionary(
        s => $"catalog_{s.ProductCode}",
        s => new CatalogAggregate(s.ProductCode) 
        { 
            Stock = new StockData { Erp = 0 } 
        });
    
    foreach (var kvp in cachedAggregates)
    {
        var aggregate = kvp.Value;
        mockCache.Setup(c => c.TryGetValue(kvp.Key, out aggregate)).Returns(true);
    }
    
    // Act
    await repository.UpdateErpStockAsync(stockData);
    
    // Assert
    Assert.AreEqual(100, cachedAggregates["catalog_TEST-001"].Stock.Erp);
    Assert.AreEqual(50, cachedAggregates["catalog_TEST-002"].Stock.Erp);
    Assert.AreEqual(75, cachedAggregates["catalog_TEST-003"].Stock.Erp);
    
    Assert.AreEqual("A1", cachedAggregates["catalog_TEST-001"].Location);
    Assert.AreEqual("B2", cachedAggregates["catalog_TEST-002"].Location);
    Assert.AreEqual("C3", cachedAggregates["catalog_TEST-003"].Location);
}
```

#### Test: UpdateSalesDataAsync_Should_Group_By_Product_And_Period
```csharp
[Test]
public async Task UpdateSalesDataAsync_Should_Group_By_Product_And_Period()
{
    // Arrange
    var mockCache = new Mock<IMemoryCache>();
    var repository = new CatalogRepository(mockCache.Object, null, null, null);
    
    var salesData = new List<CatalogSales>
    {
        new CatalogSales { ProductCode = "TEST-001", Period = "2024-01", B2BSold = 10, B2CSold = 5 },
        new CatalogSales { ProductCode = "TEST-001", Period = "2024-02", B2BSold = 15, B2CSold = 8 },
        new CatalogSales { ProductCode = "TEST-002", Period = "2024-01", B2BSold = 20, B2CSold = 12 }
    };
    
    var aggregate1 = new CatalogAggregate("TEST-001") { SalesHistory = new List<CatalogSales>() };
    var aggregate2 = new CatalogAggregate("TEST-002") { SalesHistory = new List<CatalogSales>() };
    
    mockCache.Setup(c => c.TryGetValue("catalog_TEST-001", out aggregate1)).Returns(true);
    mockCache.Setup(c => c.TryGetValue("catalog_TEST-002", out aggregate2)).Returns(true);
    
    // Act
    await repository.UpdateSalesDataAsync(salesData);
    
    // Assert
    Assert.AreEqual(2, aggregate1.SalesHistory.Count);
    Assert.AreEqual(1, aggregate2.SalesHistory.Count);
    
    var jan2024Sales = aggregate1.SalesHistory.First(s => s.Period == "2024-01");
    Assert.AreEqual(10, jan2024Sales.B2BSold);
    Assert.AreEqual(5, jan2024Sales.B2CSold);
}
```

## Integration Tests

### Full Service Integration Tests

#### Test: BackgroundService_Should_Integrate_With_All_Clients
```csharp
[Test]
public async Task BackgroundService_Should_Integrate_With_All_Clients()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMemoryCache();
    
    // Configure options for fast refresh intervals
    services.Configure<CatalogRepositoryOptions>(options =>
    {
        options.ErpStockRefreshInterval = TimeSpan.FromMilliseconds(500);
        options.EshopStockRefreshInterval = TimeSpan.FromMilliseconds(500);
        options.SalesDataRefreshInterval = TimeSpan.FromMilliseconds(500);
    });
    
    // Mock all external clients
    services.AddSingleton<IErpStockClient>(provider =>
    {
        var mock = new Mock<IErpStockClient>();
        mock.Setup(c => c.GetStockAsync()).ReturnsAsync(CreateTestErpData());
        return mock.Object;
    });
    
    services.AddSingleton<IEshopStockClient>(provider =>
    {
        var mock = new Mock<IEshopStockClient>();
        mock.Setup(c => c.GetStockAsync()).ReturnsAsync(CreateTestEshopData());
        return mock.Object;
    });
    
    services.AddSingleton<ICatalogSalesClient>(provider =>
    {
        var mock = new Mock<ICatalogSalesClient>();
        mock.Setup(c => c.GetSalesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                 .ReturnsAsync(CreateTestSalesData());
        return mock.Object;
    });
    
    services.AddSingleton<ICatalogRepository, CatalogRepository>();
    services.AddHostedService<CatalogDataRefresher>();
    
    var serviceProvider = services.BuildServiceProvider();
    
    // Act
    var hostedServices = serviceProvider.GetServices<IHostedService>();
    var refreshService = hostedServices.OfType<CatalogDataRefresher>().First();
    
    var cancellationTokenSource = new CancellationTokenSource();
    var startTask = refreshService.StartAsync(cancellationTokenSource.Token);
    
    // Let it run for multiple refresh cycles
    await Task.Delay(TimeSpan.FromSeconds(2));
    cancellationTokenSource.Cancel();
    
    // Assert
    await startTask;
    
    var repository = serviceProvider.GetRequiredService<ICatalogRepository>();
    var aggregate = await repository.GetAsync("TEST-001");
    
    Assert.IsNotNull(aggregate);
    Assert.IsTrue(aggregate.Stock.Erp > 0 || aggregate.Stock.Eshop > 0);
    Assert.IsNotEmpty(aggregate.SalesHistory);
}

private List<ErpStockData> CreateTestErpData()
{
    return new List<ErpStockData>
    {
        new ErpStockData { ProductCode = "TEST-001", Stock = 100, ProductName = "Test Product 1" },
        new ErpStockData { ProductCode = "TEST-002", Stock = 50, ProductName = "Test Product 2" }
    };
}

private List<EshopStockData> CreateTestEshopData()
{
    return new List<EshopStockData>
    {
        new EshopStockData { ProductCode = "TEST-001", Stock = 80 },
        new EshopStockData { ProductCode = "TEST-002", Stock = 30 }
    };
}

private List<CatalogSales> CreateTestSalesData()
{
    return new List<CatalogSales>
    {
        new CatalogSales { ProductCode = "TEST-001", Period = "2024-01", B2BSold = 10, B2CSold = 5 },
        new CatalogSales { ProductCode = "TEST-002", Period = "2024-01", B2BSold = 8, B2CSold = 3 }
    };
}
```

### Health Check Integration Tests

#### Test: HealthCheck_Should_Report_Healthy_When_Refreshing_Normally
```csharp
[Test]
public async Task HealthCheck_Should_Report_Healthy_When_Refreshing_Normally()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMemoryCache();
    services.AddHealthChecks().AddCheck<CatalogRefreshHealthCheck>("catalog-refresh");
    
    var mockRepository = new Mock<ICatalogRepository>();
    mockRepository.Setup(r => r.GetLastRefreshTimeAsync())
                 .ReturnsAsync(DateTime.UtcNow.AddMinutes(-5)); // Recent refresh
    
    services.AddSingleton(mockRepository.Object);
    services.AddSingleton<CatalogRefreshHealthCheck>();
    
    var serviceProvider = services.BuildServiceProvider();
    
    // Act
    var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();
    var result = await healthCheckService.CheckHealthAsync();
    
    // Assert
    Assert.AreEqual(HealthStatus.Healthy, result.Status);
    Assert.IsTrue(result.Entries.ContainsKey("catalog-refresh"));
    Assert.AreEqual(HealthStatus.Healthy, result.Entries["catalog-refresh"].Status);
}

[Test]
public async Task HealthCheck_Should_Report_Degraded_When_Refresh_Is_Stale()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddHealthChecks().AddCheck<CatalogRefreshHealthCheck>("catalog-refresh");
    
    var mockRepository = new Mock<ICatalogRepository>();
    mockRepository.Setup(r => r.GetLastRefreshTimeAsync())
                 .ReturnsAsync(DateTime.UtcNow.AddHours(-1)); // Stale refresh
    
    services.AddSingleton(mockRepository.Object);
    services.AddSingleton<CatalogRefreshHealthCheck>();
    
    var serviceProvider = services.BuildServiceProvider();
    
    // Act
    var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();
    var result = await healthCheckService.CheckHealthAsync();
    
    // Assert
    Assert.AreEqual(HealthStatus.Degraded, result.Status);
    Assert.IsTrue(result.Entries["catalog-refresh"].Description.Contains("minutes ago"));
}
```

## Performance Tests

### Load Testing

#### Test: RefreshService_Should_Handle_Large_Data_Sets
```csharp
[Test]
public async Task RefreshService_Should_Handle_Large_Data_Sets()
{
    // Arrange
    var largeStockData = Enumerable.Range(1, 10000)
        .Select(i => new ErpStockData 
        { 
            ProductCode = $"PROD-{i:0000}", 
            Stock = i % 100, 
            ProductName = $"Product {i}" 
        })
        .ToList();
    
    var mockErpClient = new Mock<IErpStockClient>();
    mockErpClient.Setup(c => c.GetStockAsync()).ReturnsAsync(largeStockData);
    
    var mockRepository = new Mock<ICatalogRepository>();
    mockRepository.Setup(r => r.UpdateErpStockAsync(It.IsAny<List<ErpStockData>>()))
                 .Returns(Task.CompletedTask);
    
    var options = Microsoft.Extensions.Options.Options.Create(new CatalogRepositoryOptions
    {
        ErpStockRefreshInterval = TimeSpan.FromMilliseconds(100)
    });
    
    var service = new CatalogDataRefresher(mockRepository.Object, null, null, options);
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var cancellationTokenSource = new CancellationTokenSource();
    
    var refreshTask = service.RefreshErpStockAsync(cancellationTokenSource.Token);
    await Task.Delay(TimeSpan.FromMilliseconds(500)); // Let one cycle complete
    cancellationTokenSource.Cancel();
    
    stopwatch.Stop();
    
    // Assert
    try { await refreshTask; } catch (OperationCanceledException) { }
    
    Assert.IsTrue(stopwatch.ElapsedMilliseconds < 5000); // Should complete within 5 seconds
    mockRepository.Verify(r => r.UpdateErpStockAsync(largeStockData), Times.AtLeastOnce);
}
```

#### Test: ConcurrentRefresh_Should_Not_Exceed_Configured_Limits
```csharp
[Test]
public async Task ConcurrentRefresh_Should_Not_Exceed_Configured_Limits()
{
    // Arrange
    var concurrentCallsCount = 0;
    var maxConcurrentCalls = 0;
    var lockObject = new object();
    
    var mockRepository = new Mock<ICatalogRepository>();
    mockRepository.Setup(r => r.UpdateErpStockAsync(It.IsAny<List<ErpStockData>>()))
                 .Returns(() =>
                 {
                     lock (lockObject)
                     {
                         concurrentCallsCount++;
                         maxConcurrentCalls = Math.Max(maxConcurrentCalls, concurrentCallsCount);
                     }
                     
                     return Task.Delay(100).ContinueWith(_ =>
                     {
                         lock (lockObject)
                         {
                             concurrentCallsCount--;
                         }
                     });
                 });
    
    var options = Microsoft.Extensions.Options.Options.Create(new CatalogRepositoryOptions
    {
        MaxConcurrentRefreshes = 2,
        ErpStockRefreshInterval = TimeSpan.FromMilliseconds(50)
    });
    
    // Act
    var service = new CatalogDataRefresher(mockRepository.Object, null, null, options);
    var cancellationTokenSource = new CancellationTokenSource();
    
    var refreshTask = service.RefreshErpStockAsync(cancellationTokenSource.Token);
    await Task.Delay(TimeSpan.FromSeconds(1)); // Let multiple cycles overlap
    cancellationTokenSource.Cancel();
    
    // Assert
    try { await refreshTask; } catch (OperationCanceledException) { }
    
    Assert.IsTrue(maxConcurrentCalls <= 2, 
                 $"Expected max 2 concurrent calls, but got {maxConcurrentCalls}");
}
```

### Memory Usage Tests

#### Test: LongRunningRefresh_Should_Not_Cause_Memory_Leaks
```csharp
[Test]
public async Task LongRunningRefresh_Should_Not_Cause_Memory_Leaks()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    mockRepository.Setup(r => r.UpdateErpStockAsync(It.IsAny<List<ErpStockData>>()))
                 .Returns(Task.CompletedTask);
    
    var options = Microsoft.Extensions.Options.Options.Create(new CatalogRepositoryOptions
    {
        ErpStockRefreshInterval = TimeSpan.FromMilliseconds(10)
    });
    
    var service = new CatalogDataRefresher(mockRepository.Object, null, null, options);
    
    // Measure initial memory
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var initialMemory = GC.GetTotalMemory(false);
    
    // Act - Run for many cycles
    var cancellationTokenSource = new CancellationTokenSource();
    var refreshTask = service.RefreshErpStockAsync(cancellationTokenSource.Token);
    
    await Task.Delay(TimeSpan.FromSeconds(2)); // Run for 2 seconds = ~200 cycles
    cancellationTokenSource.Cancel();
    
    // Clean up and measure final memory
    try { await refreshTask; } catch (OperationCanceledException) { }
    
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var finalMemory = GC.GetTotalMemory(false);
    
    // Assert
    var memoryIncrease = finalMemory - initialMemory;
    Assert.IsTrue(memoryIncrease < 1024 * 1024, // Less than 1MB increase
                 $"Memory increased by {memoryIncrease} bytes, which may indicate a leak");
}
```

## Error Handling Tests

### Resilience Tests

#### Test: Service_Should_Recover_From_Temporary_Failures
```csharp
[Test]
public async Task Service_Should_Recover_From_Temporary_Failures()
{
    // Arrange
    var callCount = 0;
    var mockErpClient = new Mock<IErpStockClient>();
    mockErpClient.Setup(c => c.GetStockAsync())
               .Returns(() =>
               {
                   callCount++;
                   if (callCount <= 2)
                   {
                       throw new TimeoutException("Temporary failure");
                   }
                   return Task.FromResult(new List<ErpStockData>
                   {
                       new ErpStockData { ProductCode = "TEST-001", Stock = 100 }
                   });
               });
    
    var mockRepository = new Mock<ICatalogRepository>();
    var mockServiceScope = new Mock<IServiceScope>();
    var mockServiceProvider = new Mock<IServiceProvider>();
    var mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
    var mockLogger = new Mock<ILogger<CatalogDataRefresher>>();
    
    mockServiceProvider.Setup(p => p.GetRequiredService<IErpStockClient>())
                      .Returns(mockErpClient.Object);
    mockServiceScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
    mockServiceScopeFactory.Setup(f => f.CreateScope()).Returns(mockServiceScope.Object);
    
    var options = Microsoft.Extensions.Options.Options.Create(new CatalogRepositoryOptions
    {
        ErpStockRefreshInterval = TimeSpan.FromMilliseconds(100)
    });
    
    var service = new CatalogDataRefresher(
        mockRepository.Object,
        mockServiceScopeFactory.Object,
        mockLogger.Object,
        options);
    
    // Act
    var cancellationTokenSource = new CancellationTokenSource();
    var refreshTask = service.RefreshErpStockAsync(cancellationTokenSource.Token);
    
    await Task.Delay(TimeSpan.FromMilliseconds(500)); // Wait for recovery
    cancellationTokenSource.Cancel();
    
    // Assert
    try { await refreshTask; } catch (OperationCanceledException) { }
    
    Assert.IsTrue(callCount >= 3, "Service should have retried and eventually succeeded");
    
    // Verify that successful update occurred
    mockRepository.Verify(r => r.UpdateErpStockAsync(It.IsAny<List<ErpStockData>>()), 
                         Times.AtLeastOnce);
    
    // Verify both error and success were logged
    mockLogger.Verify(
        x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                  It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.AtLeastOnce);
    
    mockLogger.Verify(
        x => x.Log(LogLevel.Information, It.IsAny<EventId>(), 
                  It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Completed ERP stock refresh")),
                  It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.AtLeastOnce);
}
```

#### Test: CircuitBreaker_Should_Open_After_Multiple_Failures
```csharp
[Test]
public async Task CircuitBreaker_Should_Open_After_Multiple_Failures()
{
    // Arrange
    var circuitBreaker = new RefreshCircuitBreaker(
        openDuration: TimeSpan.FromSeconds(1),
        failureThreshold: 3);
    
    var operation = new Func<Task<string>>(() => 
        throw new InvalidOperationException("Test failure"));
    
    // Act & Assert
    // First 3 calls should throw the original exception
    for (int i = 0; i < 3; i++)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => circuitBreaker.ExecuteAsync(operation));
        Assert.AreEqual("Test failure", ex.Message);
    }
    
    // 4th call should throw circuit breaker exception
    var circuitEx = await Assert.ThrowsAsync<CircuitBreakerOpenException>(
        () => circuitBreaker.ExecuteAsync(operation));
    
    Assert.IsNotNull(circuitEx);
}

[Test]
public async Task CircuitBreaker_Should_Reset_After_Success()
{
    // Arrange
    var circuitBreaker = new RefreshCircuitBreaker(
        openDuration: TimeSpan.FromMilliseconds(100),
        failureThreshold: 2);
    
    var callCount = 0;
    var operation = new Func<Task<string>>(() =>
    {
        callCount++;
        if (callCount <= 2)
            throw new InvalidOperationException("Failure");
        return Task.FromResult("Success");
    });
    
    // Act
    // Trigger circuit breaker to open
    await Assert.ThrowsAsync<InvalidOperationException>(() => circuitBreaker.ExecuteAsync(operation));
    await Assert.ThrowsAsync<InvalidOperationException>(() => circuitBreaker.ExecuteAsync(operation));
    await Assert.ThrowsAsync<CircuitBreakerOpenException>(() => circuitBreaker.ExecuteAsync(operation));
    
    // Wait for circuit to allow retry
    await Task.Delay(TimeSpan.FromMilliseconds(150));
    
    // Next call should succeed and reset circuit
    var result = await circuitBreaker.ExecuteAsync(operation);
    
    // Assert
    Assert.AreEqual("Success", result);
    
    // Subsequent calls should work normally
    var result2 = await circuitBreaker.ExecuteAsync(() => Task.FromResult("Success2"));
    Assert.AreEqual("Success2", result2);
}
```