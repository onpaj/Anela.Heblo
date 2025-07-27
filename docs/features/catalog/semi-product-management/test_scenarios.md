# Test Scenarios: Semi-Product Management

## Unit Tests

### SemiProductAppService Tests

#### Test: GetListAsync_Should_Return_Only_SemiProducts
```csharp
[Test]
public async Task GetListAsync_Should_Return_Only_SemiProducts()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    var mockMapper = new Mock<IMapper>();
    var mockLogger = new Mock<ILogger<SemiProductAppService>>();
    
    var catalogItems = new List<CatalogAggregate>
    {
        new CatalogAggregate("SEMI-001") { Type = ProductType.SemiProduct, ProductName = "Semi Product 1" },
        new CatalogAggregate("SEMI-002") { Type = ProductType.SemiProduct, ProductName = "Semi Product 2" },
        new CatalogAggregate("PROD-001") { Type = ProductType.Product, ProductName = "Regular Product" }
    };
    
    var semiProducts = catalogItems.Where(c => c.Type == ProductType.SemiProduct).ToList();
    var expectedDtos = new List<SemiProductDto>
    {
        new SemiProductDto { ProductCode = "SEMI-001", ProductName = "Semi Product 1" },
        new SemiProductDto { ProductCode = "SEMI-002", ProductName = "Semi Product 2" }
    };
    
    mockRepository.Setup(r => r.GetListAsync(It.IsAny<ISpecification<CatalogAggregate>>()))
                 .ReturnsAsync(semiProducts);
    
    mockMapper.Setup(m => m.Map<List<SemiProductDto>>(semiProducts))
             .Returns(expectedDtos);
    
    var service = new SemiProductAppService(mockRepository.Object, mockMapper.Object, mockLogger.Object);
    
    // Act
    var result = await service.GetListAsync();
    
    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual(2, result.Items.Count);
    Assert.IsTrue(result.Items.All(dto => dto.ProductCode.StartsWith("SEMI-")));
    
    // Verify repository was called with correct specification
    mockRepository.Verify(r => r.GetListAsync(
        It.Is<ISpecification<CatalogAggregate>>(spec => 
            spec is ProductTypeSpecification)), 
        Times.Once);
}
```

#### Test: GetListAsync_Should_Handle_Empty_Result
```csharp
[Test]
public async Task GetListAsync_Should_Handle_Empty_Result()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    var mockMapper = new Mock<IMapper>();
    var mockLogger = new Mock<ILogger<SemiProductAppService>>();
    
    mockRepository.Setup(r => r.GetListAsync(It.IsAny<ISpecification<CatalogAggregate>>()))
                 .ReturnsAsync(new List<CatalogAggregate>());
    
    mockMapper.Setup(m => m.Map<List<SemiProductDto>>(It.IsAny<List<CatalogAggregate>>()))
             .Returns(new List<SemiProductDto>());
    
    var service = new SemiProductAppService(mockRepository.Object, mockMapper.Object, mockLogger.Object);
    
    // Act
    var result = await service.GetListAsync();
    
    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual(0, result.Items.Count);
    
    // Verify logging
    mockLogger.Verify(
        x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Retrieved 0 semi-products")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.Once);
}
```

#### Test: GetListAsync_Should_Handle_Repository_Exception
```csharp
[Test]
public async Task GetListAsync_Should_Handle_Repository_Exception()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    var mockMapper = new Mock<IMapper>();
    var mockLogger = new Mock<ILogger<SemiProductAppService>>();
    
    mockRepository.Setup(r => r.GetListAsync(It.IsAny<ISpecification<CatalogAggregate>>()))
                 .ThrowsAsync(new InvalidOperationException("Database connection failed"));
    
    var service = new SemiProductAppService(mockRepository.Object, mockMapper.Object, mockLogger.Object);
    
    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.GetListAsync());
    
    Assert.AreEqual("Database connection failed", exception.Message);
    
    // Verify error logging
    mockLogger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error retrieving semi-products")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.Once);
}
```

#### Test: GetListAsync_Should_Log_Successful_Retrieval
```csharp
[Test]
public async Task GetListAsync_Should_Log_Successful_Retrieval()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    var mockMapper = new Mock<IMapper>();
    var mockLogger = new Mock<ILogger<SemiProductAppService>>();
    
    var catalogItems = new List<CatalogAggregate>
    {
        new CatalogAggregate("SEMI-001") { Type = ProductType.SemiProduct }
    };
    
    var dtos = new List<SemiProductDto>
    {
        new SemiProductDto { ProductCode = "SEMI-001" }
    };
    
    mockRepository.Setup(r => r.GetListAsync(It.IsAny<ISpecification<CatalogAggregate>>()))
                 .ReturnsAsync(catalogItems);
    
    mockMapper.Setup(m => m.Map<List<SemiProductDto>>(catalogItems))
             .Returns(dtos);
    
    var service = new SemiProductAppService(mockRepository.Object, mockMapper.Object, mockLogger.Object);
    
    // Act
    await service.GetListAsync();
    
    // Assert
    mockLogger.Verify(
        x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Retrieving semi-products list")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.Once);
    
    mockLogger.Verify(
        x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Retrieved 1 semi-products")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.Once);
}
```

### ProductTypeSpecification Tests

#### Test: ProductTypeSpecification_Should_Filter_By_Type
```csharp
[Test]
public void ProductTypeSpecification_Should_Filter_By_Type()
{
    // Arrange
    var specification = new ProductTypeSpecification(ProductType.SemiProduct);
    var testData = new List<CatalogAggregate>
    {
        new CatalogAggregate("SEMI-001") { Type = ProductType.SemiProduct },
        new CatalogAggregate("PROD-001") { Type = ProductType.Product },
        new CatalogAggregate("SEMI-002") { Type = ProductType.SemiProduct },
        new CatalogAggregate("MAT-001") { Type = ProductType.Material }
    }.AsQueryable();
    
    // Act
    var expression = specification.ToExpression();
    var filteredData = testData.Where(expression.Compile()).ToList();
    
    // Assert
    Assert.AreEqual(2, filteredData.Count);
    Assert.IsTrue(filteredData.All(item => item.Type == ProductType.SemiProduct));
    Assert.Contains(filteredData, item => item.Id == "SEMI-001");
    Assert.Contains(filteredData, item => item.Id == "SEMI-002");
}
```

#### Test: ProductTypeSpecification_Should_Include_Related_Data
```csharp
[Test]
public void ProductTypeSpecification_Should_Include_Related_Data()
{
    // Arrange
    var specification = new ProductTypeSpecification(ProductType.SemiProduct);
    
    // Act
    var includes = specification.GetType()
                              .GetMethod("CreateIncludeArray", BindingFlags.NonPublic | BindingFlags.Instance)
                              ?.Invoke(specification, null) as string[];
    
    // Assert
    Assert.IsNotNull(includes);
    Assert.Contains(nameof(CatalogAggregate.Stock), includes);
    Assert.Contains(nameof(CatalogAggregate.Properties), includes);
    Assert.Contains(nameof(CatalogAggregate.Suppliers), includes);
}
```

### AutoMapper Profile Tests

#### Test: SemiProductMappingProfile_Should_Map_Basic_Properties
```csharp
[Test]
public void SemiProductMappingProfile_Should_Map_Basic_Properties()
{
    // Arrange
    var config = new MapperConfiguration(cfg => cfg.AddProfile<SemiProductMappingProfile>());
    var mapper = config.CreateMapper();
    
    var catalog = new CatalogAggregate("SEMI-001")
    {
        ProductName = "Test Semi Product",
        ErpId = "ERP-123",
        Location = "Warehouse A",
        Volume = 100.5m,
        Weight = 50.25m,
        HasExpiration = true,
        HasLots = false
    };
    
    // Act
    var dto = mapper.Map<SemiProductDto>(catalog);
    
    // Assert
    Assert.AreEqual("SEMI-001", dto.ProductCode);
    Assert.AreEqual("Test Semi Product", dto.ProductName);
    Assert.AreEqual("ERP-123", dto.ErpId);
    Assert.AreEqual("Warehouse A", dto.Location);
    Assert.AreEqual(100.5m, dto.Volume);
    Assert.AreEqual(50.25m, dto.Weight);
    Assert.IsTrue(dto.HasExpiration);
    Assert.IsFalse(dto.HasLots);
}
```

#### Test: SemiProductMappingProfile_Should_Map_Stock_Data
```csharp
[Test]
public void SemiProductMappingProfile_Should_Map_Stock_Data()
{
    // Arrange
    var config = new MapperConfiguration(cfg => cfg.AddProfile<SemiProductMappingProfile>());
    var mapper = config.CreateMapper();
    
    var catalog = new CatalogAggregate("SEMI-001")
    {
        Stock = new StockData
        {
            Erp = 100,
            Eshop = 80,
            Transport = 20,
            Reserve = 10,
            PrimaryStockSource = StockSource.ERP
        }
    };
    
    // Act
    var dto = mapper.Map<SemiProductDto>(catalog);
    
    // Assert
    Assert.AreEqual(120, dto.AvailableStock); // ERP (100) + Transport (20)
    Assert.AreEqual(100, dto.ErpStock);
    Assert.AreEqual(80, dto.EshopStock);
    Assert.AreEqual(20, dto.TransportStock);
    Assert.AreEqual(10, dto.ReserveStock);
}
```

#### Test: SemiProductMappingProfile_Should_Handle_Null_Stock
```csharp
[Test]
public void SemiProductMappingProfile_Should_Handle_Null_Stock()
{
    // Arrange
    var config = new MapperConfiguration(cfg => cfg.AddProfile<SemiProductMappingProfile>());
    var mapper = config.CreateMapper();
    
    var catalog = new CatalogAggregate("SEMI-001")
    {
        Stock = null
    };
    
    // Act
    var dto = mapper.Map<SemiProductDto>(catalog);
    
    // Assert
    Assert.AreEqual(0, dto.AvailableStock);
    Assert.AreEqual(0, dto.ErpStock);
    Assert.AreEqual(0, dto.EshopStock);
    Assert.AreEqual(0, dto.TransportStock);
    Assert.AreEqual(0, dto.ReserveStock);
}
```

#### Test: SemiProductMappingProfile_Should_Map_Properties_Data
```csharp
[Test]
public void SemiProductMappingProfile_Should_Map_Properties_Data()
{
    // Arrange
    var config = new MapperConfiguration(cfg => cfg.AddProfile<SemiProductMappingProfile>());
    var mapper = config.CreateMapper();
    
    var catalog = new CatalogAggregate("SEMI-001")
    {
        Properties = new CatalogProperties
        {
            BatchSize = 50,
            OptimalStockDaysSetup = 30,
            StockMinSetup = 10
        }
    };
    
    // Act
    var dto = mapper.Map<SemiProductDto>(catalog);
    
    // Assert
    Assert.AreEqual(50, dto.BatchSize);
    Assert.AreEqual(30, dto.OptimalStockDays);
    Assert.AreEqual(10, dto.StockMinSetup);
}
```

#### Test: SemiProductMappingProfile_Should_Map_Computed_Properties
```csharp
[Test]
public void SemiProductMappingProfile_Should_Map_Computed_Properties()
{
    // Arrange
    var config = new MapperConfiguration(cfg => cfg.AddProfile<SemiProductMappingProfile>());
    var mapper = config.CreateMapper();
    
    var catalog = new CatalogAggregate("COSM01-SM")
    {
        Stock = new StockData { Erp = 5, PrimaryStockSource = StockSource.ERP },
        Properties = new CatalogProperties 
        { 
            StockMinSetup = 10,
            SeasonMonths = new[] { DateTime.Now.Month }
        }
    };
    
    // Act
    var dto = mapper.Map<SemiProductDto>(catalog);
    
    // Assert
    Assert.AreEqual("COSM01", dto.ProductFamily);
    Assert.AreEqual("COS", dto.ProductTypeCode);
    Assert.AreEqual("-SM", dto.ProductSize);
    Assert.IsTrue(dto.IsUnderStocked);
    Assert.IsTrue(dto.IsInSeason);
    Assert.IsTrue(dto.LastUpdated > DateTime.UtcNow.AddMinutes(-1));
}
```

### Repository Extension Tests

#### Test: GetSemiProductsAsync_Should_Call_Repository_With_Correct_Specification
```csharp
[Test]
public async Task GetSemiProductsAsync_Should_Call_Repository_With_Correct_Specification()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    var expectedResult = new List<CatalogAggregate>
    {
        new CatalogAggregate("SEMI-001") { Type = ProductType.SemiProduct }
    };
    
    mockRepository.Setup(r => r.GetListAsync(It.IsAny<ISpecification<CatalogAggregate>>()))
                 .ReturnsAsync(expectedResult);
    
    // Act
    var result = await mockRepository.Object.GetSemiProductsAsync();
    
    // Assert
    Assert.AreEqual(expectedResult, result);
    mockRepository.Verify(r => r.GetListAsync(
        It.Is<ISpecification<CatalogAggregate>>(spec => 
            spec is ProductTypeSpecification)), 
        Times.Once);
}
```

#### Test: GetSemiProductsByFamilyAsync_Should_Combine_Specifications
```csharp
[Test]
public async Task GetSemiProductsByFamilyAsync_Should_Combine_Specifications()
{
    // Arrange
    var mockRepository = new Mock<ICatalogRepository>();
    var expectedResult = new List<CatalogAggregate>
    {
        new CatalogAggregate("COSM01-001") { Type = ProductType.SemiProduct }
    };
    
    mockRepository.Setup(r => r.GetListAsync(It.IsAny<ISpecification<CatalogAggregate>>()))
                 .ReturnsAsync(expectedResult);
    
    // Act
    var result = await mockRepository.Object.GetSemiProductsByFamilyAsync("COSM01");
    
    // Assert
    Assert.AreEqual(expectedResult, result);
    mockRepository.Verify(r => r.GetListAsync(It.IsAny<ISpecification<CatalogAggregate>>()), Times.Once);
}
```

## Integration Tests

### Full Service Integration Tests

#### Test: SemiProductAppService_Should_Integrate_With_Repository_And_Mapper
```csharp
[Test]
public async Task SemiProductAppService_Should_Integrate_With_Repository_And_Mapper()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddAutoMapper(typeof(SemiProductMappingProfile));
    
    // Setup in-memory catalog repository
    var catalogItems = new List<CatalogAggregate>
    {
        new CatalogAggregate("SEMI-001") 
        { 
            Type = ProductType.SemiProduct,
            ProductName = "Semi Product 1",
            Stock = new StockData { Erp = 100, PrimaryStockSource = StockSource.ERP },
            Properties = new CatalogProperties { BatchSize = 50 }
        },
        new CatalogAggregate("PROD-001") 
        { 
            Type = ProductType.Product,
            ProductName = "Regular Product"
        },
        new CatalogAggregate("SEMI-002") 
        { 
            Type = ProductType.SemiProduct,
            ProductName = "Semi Product 2",
            Stock = new StockData { Erp = 75, PrimaryStockSource = StockSource.ERP }
        }
    };
    
    services.AddSingleton<ICatalogRepository>(provider =>
    {
        var mock = new Mock<ICatalogRepository>();
        mock.Setup(r => r.GetListAsync(It.IsAny<ISpecification<CatalogAggregate>>()))
           .ReturnsAsync((ISpecification<CatalogAggregate> spec) =>
           {
               var expression = spec.ToExpression();
               return catalogItems.Where(expression.Compile()).ToList();
           });
        return mock.Object;
    });
    
    services.AddTransient<ISemiProductAppService, SemiProductAppService>();
    
    var serviceProvider = services.BuildServiceProvider();
    var semiProductService = serviceProvider.GetRequiredService<ISemiProductAppService>();
    
    // Act
    var result = await semiProductService.GetListAsync();
    
    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual(2, result.Items.Count);
    
    var firstSemi = result.Items.First(s => s.ProductCode == "SEMI-001");
    Assert.AreEqual("Semi Product 1", firstSemi.ProductName);
    Assert.AreEqual(100, firstSemi.AvailableStock);
    Assert.AreEqual(50, firstSemi.BatchSize);
    
    var secondSemi = result.Items.First(s => s.ProductCode == "SEMI-002");
    Assert.AreEqual("Semi Product 2", secondSemi.ProductName);
    Assert.AreEqual(75, secondSemi.AvailableStock);
}
```

### Controller Integration Tests

#### Test: SemiProductController_Should_Return_SemiProducts_List
```csharp
[Test]
public async Task SemiProductController_Should_Return_SemiProducts_List()
{
    // Arrange
    var mockAppService = new Mock<ISemiProductAppService>();
    var expectedResult = new ListResultDto<SemiProductDto>(new List<SemiProductDto>
    {
        new SemiProductDto { ProductCode = "SEMI-001", ProductName = "Semi Product 1" },
        new SemiProductDto { ProductCode = "SEMI-002", ProductName = "Semi Product 2" }
    });
    
    mockAppService.Setup(s => s.GetListAsync()).ReturnsAsync(expectedResult);
    
    var controller = new SemiProductController(mockAppService.Object);
    
    // Act
    var actionResult = await controller.GetListAsync();
    
    // Assert
    var okResult = actionResult.Result as OkObjectResult;
    Assert.IsNotNull(okResult);
    Assert.AreEqual(200, okResult.StatusCode);
    
    var returnedData = okResult.Value as ListResultDto<SemiProductDto>;
    Assert.IsNotNull(returnedData);
    Assert.AreEqual(2, returnedData.Items.Count);
}
```

#### Test: SemiProductController_Should_Handle_Service_Exception
```csharp
[Test]
public async Task SemiProductController_Should_Handle_Service_Exception()
{
    // Arrange
    var mockAppService = new Mock<ISemiProductAppService>();
    mockAppService.Setup(s => s.GetListAsync())
                 .ThrowsAsync(new InvalidOperationException("Service error"));
    
    var mockLogger = new Mock<ILogger<SemiProductController>>();
    var controller = new SemiProductController(mockAppService.Object);
    // Note: In real implementation, would need to inject logger
    
    // Act
    var actionResult = await controller.GetListAsync();
    
    // Assert
    var statusResult = actionResult.Result as ObjectResult;
    Assert.IsNotNull(statusResult);
    Assert.AreEqual(500, statusResult.StatusCode);
    Assert.AreEqual("Internal server error", statusResult.Value);
}
```

## Authorization Tests

### Permission Tests

#### Test: SemiProductAppService_Should_Require_Authorization
```csharp
[Test]
public void SemiProductAppService_Should_Have_Authorize_Attribute()
{
    // Arrange
    var serviceType = typeof(SemiProductAppService);
    
    // Act
    var authorizeAttribute = serviceType.GetCustomAttribute<AuthorizeAttribute>();
    
    // Assert
    Assert.IsNotNull(authorizeAttribute);
}
```

#### Test: GetListAsync_Should_Check_View_Permission()
{
    // This test would require setting up a test environment with ABP authorization
    // and would be more complex to implement. The actual permission checking
    // would be tested in integration tests with a full ABP application context.
}
```

## Performance Tests

### Load Testing

#### Test: GetListAsync_Should_Handle_Large_Result_Sets
```csharp
[Test]
public async Task GetListAsync_Should_Handle_Large_Result_Sets()
{
    // Arrange
    var largeCatalogList = Enumerable.Range(1, 10000)
        .Select(i => new CatalogAggregate($"SEMI-{i:0000}")
        {
            Type = ProductType.SemiProduct,
            ProductName = $"Semi Product {i}",
            Stock = new StockData { Erp = i % 100, PrimaryStockSource = StockSource.ERP }
        })
        .ToList();
    
    var mockRepository = new Mock<ICatalogRepository>();
    var mockMapper = new Mock<IMapper>();
    var mockLogger = new Mock<ILogger<SemiProductAppService>>();
    
    mockRepository.Setup(r => r.GetListAsync(It.IsAny<ISpecification<CatalogAggregate>>()))
                 .ReturnsAsync(largeCatalogList);
    
    var largeDtoList = largeCatalogList.Select(c => new SemiProductDto 
    { 
        ProductCode = c.Id, 
        ProductName = c.ProductName 
    }).ToList();
    
    mockMapper.Setup(m => m.Map<List<SemiProductDto>>(largeCatalogList))
             .Returns(largeDtoList);
    
    var service = new SemiProductAppService(mockRepository.Object, mockMapper.Object, mockLogger.Object);
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var result = await service.GetListAsync();
    stopwatch.Stop();
    
    // Assert
    Assert.AreEqual(10000, result.Items.Count);
    Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000); // Should complete within 1 second
}
```

### Memory Usage Tests

#### Test: Mapping_Should_Not_Cause_Memory_Leaks
```csharp
[Test]
public void Mapping_Should_Not_Cause_Memory_Leaks()
{
    // Arrange
    var config = new MapperConfiguration(cfg => cfg.AddProfile<SemiProductMappingProfile>());
    var mapper = config.CreateMapper();
    
    var initialMemory = GC.GetTotalMemory(true);
    
    // Act - Perform many mapping operations
    for (int i = 0; i < 10000; i++)
    {
        var catalog = new CatalogAggregate($"SEMI-{i}")
        {
            Type = ProductType.SemiProduct,
            ProductName = $"Product {i}",
            Stock = new StockData { Erp = i, PrimaryStockSource = StockSource.ERP }
        };
        
        var dto = mapper.Map<SemiProductDto>(catalog);
        
        // Don't retain references
    }
    
    var finalMemory = GC.GetTotalMemory(true);
    
    // Assert
    var memoryIncrease = finalMemory - initialMemory;
    Assert.IsTrue(memoryIncrease < 1024 * 1024, // Less than 1MB increase
                 $"Memory increased by {memoryIncrease} bytes");
}
```

## Caching Tests

### Cache Behavior Tests

#### Test: CachedSemiProductAppService_Should_Return_Cached_Result
```csharp
[Test]
public async Task CachedSemiProductAppService_Should_Return_Cached_Result()
{
    // Arrange
    var mockInnerService = new Mock<ISemiProductAppService>();
    var mockCache = new Mock<IDistributedCache>();
    var mockLogger = new Mock<ILogger<CachedSemiProductAppService>>();
    
    var expectedResult = new ListResultDto<SemiProductDto>(new List<SemiProductDto>
    {
        new SemiProductDto { ProductCode = "SEMI-001" }
    });
    
    var serializedResult = JsonSerializer.Serialize(expectedResult);
    var cachedBytes = Encoding.UTF8.GetBytes(serializedResult);
    
    mockCache.Setup(c => c.GetAsync("semi-products-list:user123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBytes);
    
    var cachedService = new CachedSemiProductAppService(
        mockInnerService.Object, 
        mockCache.Object, 
        mockLogger.Object);
    
    // Act
    var result = await cachedService.GetListAsync();
    
    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual(1, result.Items.Count);
    Assert.AreEqual("SEMI-001", result.Items.First().ProductCode);
    
    // Verify inner service was not called
    mockInnerService.Verify(s => s.GetListAsync(), Times.Never);
    
    // Verify cache was accessed
    mockCache.Verify(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

#### Test: CachedSemiProductAppService_Should_Call_Inner_Service_On_Cache_Miss
```csharp
[Test]
public async Task CachedSemiProductAppService_Should_Call_Inner_Service_On_Cache_Miss()
{
    // Arrange
    var mockInnerService = new Mock<ISemiProductAppService>();
    var mockCache = new Mock<IDistributedCache>();
    var mockLogger = new Mock<ILogger<CachedSemiProductAppService>>();
    
    var expectedResult = new ListResultDto<SemiProductDto>(new List<SemiProductDto>
    {
        new SemiProductDto { ProductCode = "SEMI-001" }
    });
    
    // Cache miss
    mockCache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[])null);
    
    mockInnerService.Setup(s => s.GetListAsync()).ReturnsAsync(expectedResult);
    
    var cachedService = new CachedSemiProductAppService(
        mockInnerService.Object, 
        mockCache.Object, 
        mockLogger.Object);
    
    // Act
    var result = await cachedService.GetListAsync();
    
    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual(1, result.Items.Count);
    
    // Verify inner service was called
    mockInnerService.Verify(s => s.GetListAsync(), Times.Once);
    
    // Verify result was cached
    mockCache.Verify(c => c.SetAsync(
        It.IsAny<string>(), 
        It.IsAny<byte[]>(), 
        It.IsAny<DistributedCacheEntryOptions>(), 
        It.IsAny<CancellationToken>()), 
        Times.Once);
}
```