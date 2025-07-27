# Invoice Import and Sync - Test Scenarios

## Unit Test Scenarios

### Domain Model Tests

#### IssuedInvoice Aggregate Tests

```csharp
[TestFixture]
public class IssuedInvoiceTests
{
    [Test]
    public void Create_WithValidData_ShouldCreateSuccessfully()
    {
        // Arrange
        var invoiceCode = "INV-2024-001";
        var invoiceDate = DateTime.Today;
        var dueDate = DateTime.Today.AddDays(14);
        var price = 1000.50m;
        var currency = "CZK";

        // Act
        var invoice = IssuedInvoice.Create(invoiceCode, invoiceDate, dueDate, price, currency);

        // Assert
        Assert.That(invoice.Id, Is.EqualTo(invoiceCode));
        Assert.That(invoice.InvoiceDate, Is.EqualTo(invoiceDate));
        Assert.That(invoice.DueDate, Is.EqualTo(dueDate));
        Assert.That(invoice.TaxDate, Is.EqualTo(invoiceDate));
        Assert.That(invoice.Price, Is.EqualTo(price));
        Assert.That(invoice.Currency, Is.EqualTo(currency));
        Assert.That(invoice.IsSynced, Is.False);
    }

    [Test]
    public void Create_WithEmptyInvoiceCode_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            IssuedInvoice.Create("", DateTime.Today, DateTime.Today.AddDays(14), 1000m));
    }

    [Test]
    public void Create_WithInvoiceDateAfterDueDate_ShouldThrowBusinessException()
    {
        // Arrange
        var invoiceDate = DateTime.Today;
        var dueDate = DateTime.Today.AddDays(-1);

        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            IssuedInvoice.Create("INV-001", invoiceDate, dueDate, 1000m));
    }

    [Test]
    public void Create_WithNegativePrice_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            IssuedInvoice.Create("INV-001", DateTime.Today, DateTime.Today.AddDays(14), -100m));
    }

    [Test]
    public void SyncSucceeded_ShouldUpdateSyncStatus()
    {
        // Arrange
        var invoice = CreateValidInvoice();
        var syncedObject = new { InvoiceId = "ERP-123", Status = "Synced" };

        // Act
        invoice.SyncSucceeded(syncedObject);

        // Assert
        Assert.That(invoice.IsSynced, Is.True);
        Assert.That(invoice.ErrorMessage, Is.Null);
        Assert.That(invoice.ErrorType, Is.Null);
        Assert.That(invoice.LastSyncTime, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
        Assert.That(invoice.SyncHistoryCount, Is.EqualTo(1));
        Assert.That(invoice.SyncHistory.First().IsSuccess, Is.True);
    }

    [Test]
    public void SyncFailed_ShouldUpdateSyncStatusWithError()
    {
        // Arrange
        var invoice = CreateValidInvoice();
        var syncedObject = new { InvoiceId = "ERP-123", Status = "Failed" };
        var error = new IssuedInvoiceError
        {
            Message = "Product not found",
            ErrorType = IssuedInvoiceErrorType.ProductNotFound
        };

        // Act
        invoice.SyncFailed(syncedObject, error);

        // Assert
        Assert.That(invoice.IsSynced, Is.False);
        Assert.That(invoice.ErrorMessage, Is.EqualTo("Product not found"));
        Assert.That(invoice.ErrorType, Is.EqualTo(IssuedInvoiceErrorType.ProductNotFound));
        Assert.That(invoice.IsCriticalError, Is.True);
        Assert.That(invoice.SyncHistoryCount, Is.EqualTo(1));
        Assert.That(invoice.SyncHistory.First().IsSuccess, Is.False);
    }

    [Test]
    public void IsCriticalError_WithInvoicePairedError_ShouldReturnFalse()
    {
        // Arrange
        var invoice = CreateValidInvoice();
        var error = new IssuedInvoiceError
        {
            Message = "Invoice already exists",
            ErrorType = IssuedInvoiceErrorType.InvoicePaired
        };

        // Act
        invoice.SyncFailed(new { }, error);

        // Assert
        Assert.That(invoice.IsCriticalError, Is.False);
    }

    [Test]
    public void MultipleSyncAttempts_ShouldMaintainHistory()
    {
        // Arrange
        var invoice = CreateValidInvoice();

        // Act
        invoice.SyncFailed(new { }, new IssuedInvoiceError { Message = "First failure" });
        invoice.SyncFailed(new { }, new IssuedInvoiceError { Message = "Second failure" });
        invoice.SyncSucceeded(new { InvoiceId = "ERP-123" });

        // Assert
        Assert.That(invoice.SyncHistoryCount, Is.EqualTo(3));
        Assert.That(invoice.IsSynced, Is.True);
        Assert.That(invoice.SyncHistory.Count(s => !s.IsSuccess), Is.EqualTo(2));
        Assert.That(invoice.SyncHistory.Count(s => s.IsSuccess), Is.EqualTo(1));
    }

    private IssuedInvoice CreateValidInvoice()
    {
        return IssuedInvoice.Create("INV-2024-001", DateTime.Today, DateTime.Today.AddDays(14), 1000m);
    }
}
```

#### IssuedInvoiceDetail Entity Tests

```csharp
[TestFixture]
public class IssuedInvoiceDetailTests
{
    [Test]
    public void Create_WithValidData_ShouldCreateSuccessfully()
    {
        // Arrange
        var detailId = "DETAIL-001";
        var customer = new Customer { Code = "CUST-001", Name = "Test Customer" };

        // Act
        var detail = IssuedInvoiceDetail.Create(detailId, customer);

        // Assert
        Assert.That(detail.DetailId, Is.EqualTo(detailId));
        Assert.That(detail.Customer, Is.EqualTo(customer));
        Assert.That(detail.Items.Count, Is.EqualTo(0));
    }

    [Test]
    public void Create_WithEmptyDetailId_ShouldThrowBusinessException()
    {
        // Arrange
        var customer = new Customer { Code = "CUST-001", Name = "Test Customer" };

        // Act & Assert
        Assert.Throws<BusinessException>(() => IssuedInvoiceDetail.Create("", customer));
    }

    [Test]
    public void Create_WithNullCustomer_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => IssuedInvoiceDetail.Create("DETAIL-001", null));
    }

    [Test]
    public void AddItem_WithValidItem_ShouldAddToCollection()
    {
        // Arrange
        var detail = CreateValidDetail();
        var item = CreateValidItem("ITEM-001", "Test Product", 2, 100m, 0.21m);

        // Act
        detail.AddItem(item);

        // Assert
        Assert.That(detail.Items.Count, Is.EqualTo(1));
        Assert.That(detail.Items.First(), Is.EqualTo(item));
    }

    [Test]
    public void AddItem_WithNullItem_ShouldThrowBusinessException()
    {
        // Arrange
        var detail = CreateValidDetail();

        // Act & Assert
        Assert.Throws<BusinessException>(() => detail.AddItem(null));
    }

    [Test]
    public void CalculateTotalPrice_WithMultipleItems_ShouldReturnCorrectSum()
    {
        // Arrange
        var detail = CreateValidDetail();
        detail.AddItem(CreateValidItem("ITEM-001", "Product 1", 2, 100m, 0.21m)); // 200
        detail.AddItem(CreateValidItem("ITEM-002", "Product 2", 1, 150m, 0.21m)); // 150

        // Act
        var total = detail.CalculateTotalPrice();

        // Assert
        Assert.That(total, Is.EqualTo(350m));
    }

    private IssuedInvoiceDetail CreateValidDetail()
    {
        var customer = new Customer { Code = "CUST-001", Name = "Test Customer" };
        return IssuedInvoiceDetail.Create("DETAIL-001", customer);
    }

    private IssuedInvoiceDetailItem CreateValidItem(string itemId, string name, decimal quantity, decimal price, decimal tax)
    {
        return IssuedInvoiceDetailItem.Create(itemId, name, quantity, price, tax);
    }
}
```

#### IssuedInvoiceDetailItem Entity Tests

```csharp
[TestFixture]
public class IssuedInvoiceDetailItemTests
{
    [Test]
    public void Create_WithValidData_ShouldCreateAndCalculatePrices()
    {
        // Arrange
        var itemId = "ITEM-001";
        var name = "Test Product";
        var quantity = 2m;
        var price = 100m;
        var tax = 0.21m;

        // Act
        var item = IssuedInvoiceDetailItem.Create(itemId, name, quantity, price, tax);

        // Assert
        Assert.That(item.ItemId, Is.EqualTo(itemId));
        Assert.That(item.Name, Is.EqualTo(name));
        Assert.That(item.Quantity, Is.EqualTo(quantity));
        Assert.That(item.Price, Is.EqualTo(price));
        Assert.That(item.Tax, Is.EqualTo(tax));
        Assert.That(item.Unit, Is.EqualTo("ks"));
        Assert.That(item.PriceSum, Is.EqualTo(200m)); // 2 * 100
        Assert.That(item.PriceSumVat, Is.EqualTo(242m)); // 200 * 1.21
    }

    [Test]
    public void Create_WithEmptyItemId_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            IssuedInvoiceDetailItem.Create("", "Test Product", 1, 100m, 0.21m));
    }

    [Test]
    public void Create_WithEmptyName_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            IssuedInvoiceDetailItem.Create("ITEM-001", "", 1, 100m, 0.21m));
    }

    [Test]
    public void Create_WithZeroQuantity_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            IssuedInvoiceDetailItem.Create("ITEM-001", "Test Product", 0, 100m, 0.21m));
    }

    [Test]
    public void Create_WithNegativePrice_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            IssuedInvoiceDetailItem.Create("ITEM-001", "Test Product", 1, -100m, 0.21m));
    }

    [Test]
    public void Create_WithZeroTax_ShouldCalculateCorrectly()
    {
        // Act
        var item = IssuedInvoiceDetailItem.Create("ITEM-001", "Gift Product", 1, 100m, 0m);

        // Assert
        Assert.That(item.PriceSum, Is.EqualTo(100m));
        Assert.That(item.PriceSumVat, Is.EqualTo(100m)); // No VAT added
    }
}
```

### Application Service Tests

#### IssuedInvoiceAppService Tests

```csharp
[TestFixture]
public class IssuedInvoiceAppServiceTests
{
    private Mock<IRepository<IssuedInvoice, string>> _mockRepository;
    private Mock<IIssuedInvoiceSource> _mockInvoiceSource;
    private Mock<IIssuedInvoiceClient> _mockErpClient;
    private Mock<IEnumerable<IIssuedInvoiceImportTransformation>> _mockTransformations;
    private Mock<IBackgroundJobManager> _mockJobManager;
    private Mock<ILogger<IssuedInvoiceAppService>> _mockLogger;
    private IssuedInvoiceAppService _service;

    [SetUp]
    public void SetUp()
    {
        _mockRepository = new Mock<IRepository<IssuedInvoice, string>>();
        _mockInvoiceSource = new Mock<IIssuedInvoiceSource>();
        _mockErpClient = new Mock<IIssuedInvoiceClient>();
        _mockTransformations = new Mock<IEnumerable<IIssuedInvoiceImportTransformation>>();
        _mockJobManager = new Mock<IBackgroundJobManager>();
        _mockLogger = new Mock<ILogger<IssuedInvoiceAppService>>();

        _service = new IssuedInvoiceAppService(
            _mockRepository.Object,
            _mockInvoiceSource.Object,
            _mockErpClient.Object,
            null, // cash register source
            null, // bank client
            _mockTransformations.Object,
            _mockJobManager.Object,
            null, // uow manager
            _mockLogger.Object);
    }

    [Test]
    public async Task ImportInvoiceAsync_WithValidInvoices_ShouldImportSuccessfully()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery
        {
            FromDate = DateTime.Today,
            ToDate = DateTime.Today,
            RequestId = "REQ-001"
        };

        var invoices = new List<IssuedInvoiceDetail>
        {
            CreateTestInvoiceDetail("INV-001"),
            CreateTestInvoiceDetail("INV-002")
        };

        var batch = new IssuedInvoiceDetailBatch
        {
            BatchId = "BATCH-001",
            Invoices = invoices
        };

        _mockInvoiceSource
            .Setup(x => x.GetAllAsync(query))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch });

        _mockErpClient
            .Setup(x => x.CreateAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult<OperationResultDetail>(HttpStatusCode.OK));

        // Act
        var result = await _service.ImportInvoiceAsync(query);

        // Assert
        Assert.That(result.RequestId, Is.EqualTo("REQ-001"));
        Assert.That(result.Succeeded.Count, Is.EqualTo(2));
        Assert.That(result.Failed.Count, Is.EqualTo(0));
        Assert.That(result.SuccessRate, Is.EqualTo(1.0));

        _mockInvoiceSource.Verify(x => x.CommitAsync(batch), Times.Once);
        _mockInvoiceSource.Verify(x => x.FailAsync(batch), Times.Never);
    }

    [Test]
    public async Task ImportInvoiceAsync_WithErpErrors_ShouldHandleFailures()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery { RequestId = "REQ-002" };
        var invoices = new List<IssuedInvoiceDetail> { CreateTestInvoiceDetail("INV-003") };
        var batch = new IssuedInvoiceDetailBatch { BatchId = "BATCH-002", Invoices = invoices };

        _mockInvoiceSource
            .Setup(x => x.GetAllAsync(query))
            .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch });

        _mockErpClient
            .Setup(x => x.CreateAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult<OperationResultDetail>(HttpStatusCode.BadRequest)
            {
                ErrorMessage = "Product not found"
            });

        // Act
        var result = await _service.ImportInvoiceAsync(query);

        // Assert
        Assert.That(result.Succeeded.Count, Is.EqualTo(0));
        Assert.That(result.Failed.Count, Is.EqualTo(1));
        Assert.That(result.Failed.First(), Is.EqualTo("INV-003"));

        _mockInvoiceSource.Verify(x => x.FailAsync(batch), Times.Once);
        _mockInvoiceSource.Verify(x => x.CommitAsync(batch), Times.Never);
    }

    [Test]
    public async Task EnqueueImportInvoiceAsync_WithSpecificInvoices_ShouldEnqueueJobs()
    {
        // Arrange
        var request = new ImportInvoiceRequestDto
        {
            InvoiceIds = new List<string> { "INV-001", "INV-002" },
            Currency = "CZK"
        };

        _mockJobManager
            .Setup(x => x.EnqueueAsync(It.IsAny<IssuedInvoiceSingleImportArgs>()))
            .ReturnsAsync("JOB-ID");

        // Act
        var jobIds = await _service.EnqueueImportInvoiceAsync(request);

        // Assert
        Assert.That(jobIds.Count, Is.EqualTo(2));
        _mockJobManager.Verify(
            x => x.EnqueueAsync(It.Is<IssuedInvoiceSingleImportArgs>(args => 
                request.InvoiceIds.Contains(args.InvoiceCode))), 
            Times.Exactly(2));
    }

    [Test]
    public async Task GetAsync_WithDetails_ShouldReturnInvoiceWithSyncHistory()
    {
        // Arrange
        var invoiceId = "INV-001";
        var invoice = CreateTestInvoice(invoiceId);
        invoice.SyncFailed(new { }, new IssuedInvoiceError { Message = "Test error" });
        invoice.SyncSucceeded(new { ErpId = "ERP-123" });

        _mockRepository
            .Setup(x => x.GetAsync(invoiceId, true))
            .ReturnsAsync(invoice);

        // Act
        var result = await _service.GetAsync(invoiceId, withDetails: true);

        // Assert
        Assert.That(result.Id, Is.EqualTo(invoiceId));
        Assert.That(result.SyncData, Is.Not.Null);
        Assert.That(result.SyncData.Count, Is.EqualTo(2));
        Assert.That(result.SyncData.Last().IsSuccess, Is.True);
    }

    private IssuedInvoice CreateTestInvoice(string invoiceCode)
    {
        return IssuedInvoice.Create(invoiceCode, DateTime.Today, DateTime.Today.AddDays(14), 1000m);
    }

    private IssuedInvoiceDetail CreateTestInvoiceDetail(string invoiceCode)
    {
        var customer = new Customer { Code = "CUST-001", Name = "Test Customer" };
        var detail = IssuedInvoiceDetail.Create($"DETAIL-{invoiceCode}", customer);
        detail.AddItem(IssuedInvoiceDetailItem.Create("ITEM-001", "Test Product", 1, 100m, 0.21m));
        return detail;
    }
}
```

### Data Source Tests

#### ShoptetIssuedInvoiceSource Tests

```csharp
[TestFixture]
public class ShoptetIssuedInvoiceSourceTests
{
    private Mock<IPlaywrightService> _mockPlaywrightService;
    private Mock<IDropboxService> _mockDropboxService;
    private Mock<ILogger<ShoptetIssuedInvoiceSource>> _mockLogger;
    private ShoptetIssuedInvoiceSource _source;

    [SetUp]
    public void SetUp()
    {
        _mockPlaywrightService = new Mock<IPlaywrightService>();
        _mockDropboxService = new Mock<IDropboxService>();
        _mockLogger = new Mock<ILogger<ShoptetIssuedInvoiceSource>>();

        _source = new ShoptetIssuedInvoiceSource(
            _mockPlaywrightService.Object,
            _mockDropboxService.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task GetAllAsync_WithPlaywrightEnabled_ShouldReturnBatch()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery
        {
            UsePlaywright = true,
            FromDate = DateTime.Today,
            ToDate = DateTime.Today
        };

        var mockBrowser = new Mock<IBrowser>();
        var mockPage = new Mock<IPage>();
        var mockDownload = new Mock<IDownload>();

        _mockPlaywrightService
            .Setup(x => x.LaunchBrowserAsync())
            .ReturnsAsync(mockBrowser.Object);

        mockBrowser
            .Setup(x => x.NewPageAsync())
            .ReturnsAsync(mockPage.Object);

        mockPage
            .Setup(x => x.WaitForDownloadAsync())
            .ReturnsAsync(mockDownload.Object);

        mockDownload
            .Setup(x => x.SaveAsAsync())
            .ReturnsAsync("/tmp/invoices.xml");

        // Act
        var batches = await _source.GetAllAsync(query);

        // Assert
        Assert.That(batches.Count, Is.EqualTo(1));
        
        mockPage.Verify(x => x.GotoAsync("https://admin.shoptet.cz/login"), Times.Once);
        mockPage.Verify(x => x.GotoAsync("https://admin.shoptet.cz/invoices/export"), Times.Once);
    }

    [Test]
    public async Task GetAllAsync_WithDropboxEnabled_ShouldProcessFiles()
    {
        // Arrange
        var query = new IssuedInvoiceSourceQuery { UseDropbox = true };

        var files = new List<DropboxFile>
        {
            new DropboxFile { Name = "invoices_001.xml", Path = "/invoices/new/invoices_001.xml" },
            new DropboxFile { Name = "invoices_002.xml", Path = "/invoices/new/invoices_002.xml" }
        };

        _mockDropboxService
            .Setup(x => x.ListFilesAsync("/invoices/new"))
            .ReturnsAsync(files);

        _mockDropboxService
            .Setup(x => x.DownloadAsync(It.IsAny<string>()))
            .ReturnsAsync("<xml>sample invoice data</xml>");

        // Act
        var batches = await _source.GetAllAsync(query);

        // Assert
        Assert.That(batches.Count, Is.EqualTo(2));

        _mockDropboxService.Verify(
            x => x.MoveAsync(It.IsAny<string>(), "/invoices/processing"), 
            Times.Exactly(2));
    }

    [Test]
    public async Task CommitAsync_ShouldMoveFileToSuccessFolder()
    {
        // Arrange
        var batch = new IssuedInvoiceDetailBatch
        {
            BatchId = "BATCH-001",
            SourcePath = "/invoices/processing/invoices.xml"
        };

        // Act
        await _source.CommitAsync(batch);

        // Assert
        _mockDropboxService.Verify(
            x => x.MoveAsync("/invoices/processing/invoices.xml", "/invoices/results"), 
            Times.Once);
    }

    [Test]
    public async Task FailAsync_ShouldMoveFileToFailureFolder()
    {
        // Arrange
        var batch = new IssuedInvoiceDetailBatch
        {
            BatchId = "BATCH-001",
            SourcePath = "/invoices/processing/invoices.xml"
        };

        // Act
        await _source.FailAsync(batch);

        // Assert
        _mockDropboxService.Verify(
            x => x.MoveAsync("/invoices/processing/invoices.xml", "/invoices/failures"), 
            Times.Once);
    }
}
```

### Transformation Tests

#### ProductMappingTransformation Tests

```csharp
[TestFixture]
public class ProductMappingTransformationTests
{
    private Mock<IProductMappingRepository> _mockMappingRepository;
    private ProductMappingTransformation _transformation;

    [SetUp]
    public void SetUp()
    {
        _mockMappingRepository = new Mock<IProductMappingRepository>();
        _transformation = new ProductMappingTransformation(_mockMappingRepository.Object);
    }

    [Test]
    public async Task Transform_WithMappedProducts_ShouldUpdateCodes()
    {
        // Arrange
        var invoice = CreateTestInvoice();
        var item = invoice.Detail.Items.First();
        item.SupplierCode = "SHOPTET-001";

        var mapping = new ProductMapping
        {
            EcommerceCode = "SHOPTET-001",
            ErpCode = "ERP-001",
            ErpName = "ERP Product Name"
        };

        _mockMappingRepository
            .Setup(x => x.GetMappingAsync("SHOPTET-001"))
            .ReturnsAsync(mapping);

        // Act
        var result = _transformation.Transform(invoice);

        // Assert
        Assert.That(result.Detail.Items.First().SupplierCode, Is.EqualTo("ERP-001"));
        Assert.That(result.Detail.Items.First().SupplierName, Is.EqualTo("ERP Product Name"));
    }

    [Test]
    public async Task Transform_WithUnmappedProducts_ShouldLeaveUnchanged()
    {
        // Arrange
        var invoice = CreateTestInvoice();
        var item = invoice.Detail.Items.First();
        item.SupplierCode = "UNKNOWN-001";

        _mockMappingRepository
            .Setup(x => x.GetMappingAsync("UNKNOWN-001"))
            .ReturnsAsync((ProductMapping)null);

        // Act
        var result = _transformation.Transform(invoice);

        // Assert
        Assert.That(result.Detail.Items.First().SupplierCode, Is.EqualTo("UNKNOWN-001"));
    }

    private IssuedInvoice CreateTestInvoice()
    {
        var invoice = IssuedInvoice.Create("INV-001", DateTime.Today, DateTime.Today.AddDays(14), 100m);
        var customer = new Customer { Code = "CUST-001", Name = "Test Customer" };
        var detail = IssuedInvoiceDetail.Create("DETAIL-001", customer);
        detail.AddItem(IssuedInvoiceDetailItem.Create("ITEM-001", "Test Product", 1, 100m, 0.21m));
        invoice.Detail = detail;
        return invoice;
    }
}
```

#### GiftWithoutVATTransformation Tests

```csharp
[TestFixture]
public class GiftWithoutVATTransformationTests
{
    private GiftWithoutVATTransformation _transformation;

    [SetUp]
    public void SetUp()
    {
        _transformation = new GiftWithoutVATTransformation();
    }

    [Test]
    public void Transform_WithGiftItem_ShouldRemoveVAT()
    {
        // Arrange
        var invoice = CreateTestInvoice();
        var giftItem = IssuedInvoiceDetailItem.Create("GIFT-001", "Gift Item", 1, 0m, 0.21m);
        invoice.Detail.AddItem(giftItem);

        // Act
        var result = _transformation.Transform(invoice);

        // Assert
        var transformedGift = result.Detail.Items.First(i => i.ItemId == "GIFT-001");
        Assert.That(transformedGift.Tax, Is.EqualTo(0));
        Assert.That(transformedGift.Type, Is.EqualTo("service"));
    }

    [Test]
    public void Transform_WithNameContainingGift_ShouldRemoveVAT()
    {
        // Arrange
        var invoice = CreateTestInvoice();
        var giftItem = IssuedInvoiceDetailItem.Create("ITEM-002", "Dárek pro zákazníka", 1, 100m, 0.21m);
        invoice.Detail.AddItem(giftItem);

        // Act
        var result = _transformation.Transform(invoice);

        // Assert
        var transformedItem = result.Detail.Items.First(i => i.ItemId == "ITEM-002");
        Assert.That(transformedItem.Tax, Is.EqualTo(0));
    }

    [Test]
    public void Transform_WithRegularItem_ShouldLeaveUnchanged()
    {
        // Arrange
        var invoice = CreateTestInvoice();

        // Act
        var result = _transformation.Transform(invoice);

        // Assert
        var regularItem = result.Detail.Items.First();
        Assert.That(regularItem.Tax, Is.EqualTo(0.21m));
        Assert.That(regularItem.Type, Is.Not.EqualTo("service"));
    }

    private IssuedInvoice CreateTestInvoice()
    {
        var invoice = IssuedInvoice.Create("INV-001", DateTime.Today, DateTime.Today.AddDays(14), 100m);
        var customer = new Customer { Code = "CUST-001", Name = "Test Customer" };
        var detail = IssuedInvoiceDetail.Create("DETAIL-001", customer);
        detail.AddItem(IssuedInvoiceDetailItem.Create("ITEM-001", "Regular Product", 1, 100m, 0.21m));
        invoice.Detail = detail;
        return invoice;
    }
}
```

## Integration Test Scenarios

### Background Job Integration Tests

```csharp
[TestFixture]
public class IssuedInvoiceDailyImportJobIntegrationTests
{
    private TestHost _testHost;
    private Mock<IIssuedInvoiceAppService> _mockAppService;

    [SetUp]
    public async Task SetUp()
    {
        _mockAppService = new Mock<IIssuedInvoiceAppService>();

        _testHost = new TestHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_mockAppService.Object);
                services.AddTransient<IssuedInvoiceDailyImportJob>();
            })
            .Build();

        await _testHost.StartAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _testHost.StopAsync();
        _testHost.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_WithValidArgs_ShouldCallImportService()
    {
        // Arrange
        var job = _testHost.Services.GetRequiredService<IssuedInvoiceDailyImportJob>();
        var args = new IssuedInvoiceDailyImportArgs
        {
            Date = DateTime.Today.AddDays(-1),
            Currency = "CZK"
        };

        _mockAppService
            .Setup(x => x.ImportInvoiceAsync(It.IsAny<IssuedInvoiceSourceQuery>(), default))
            .ReturnsAsync(new ImportResultDto { RequestId = "REQ-001" });

        // Act
        await job.ExecuteAsync(args);

        // Assert
        _mockAppService.Verify(
            x => x.ImportInvoiceAsync(
                It.Is<IssuedInvoiceSourceQuery>(q => 
                    q.FromDate == args.Date.Date && 
                    q.Currency == args.Currency), 
                default), 
            Times.Once);
    }
}
```

### Repository Integration Tests

```csharp
[TestFixture]
public class IssuedInvoiceRepositoryIntegrationTests
{
    private DbContextOptions<HebroDbContext> _dbContextOptions;
    private HebroDbContext _dbContext;
    private IRepository<IssuedInvoice, string> _repository;

    [SetUp]
    public void SetUp()
    {
        _dbContextOptions = new DbContextOptionsBuilder<HebroDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new HebroDbContext(_dbContextOptions);
        _repository = new EfCoreRepository<HebroDbContext, IssuedInvoice, string>(_dbContext);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [Test]
    public async Task InsertAsync_WithValidInvoice_ShouldPersist()
    {
        // Arrange
        var invoice = IssuedInvoice.Create("INV-001", DateTime.Today, DateTime.Today.AddDays(14), 1000m);

        // Act
        var result = await _repository.InsertAsync(invoice);
        await _dbContext.SaveChangesAsync();

        // Assert
        var retrieved = await _repository.GetAsync("INV-001");
        Assert.That(retrieved.Id, Is.EqualTo("INV-001"));
        Assert.That(retrieved.Price, Is.EqualTo(1000m));
    }

    [Test]
    public async Task GetListAsync_WithFiltering_ShouldReturnFilteredResults()
    {
        // Arrange
        var syncedInvoice = IssuedInvoice.Create("INV-SYNCED", DateTime.Today, DateTime.Today.AddDays(14), 1000m);
        syncedInvoice.SyncSucceeded(new { ErpId = "ERP-123" });

        var unsyncedInvoice = IssuedInvoice.Create("INV-UNSYNCED", DateTime.Today, DateTime.Today.AddDays(14), 500m);

        await _repository.InsertAsync(syncedInvoice);
        await _repository.InsertAsync(unsyncedInvoice);
        await _dbContext.SaveChangesAsync();

        // Act
        var queryable = await _repository.GetQueryableAsync();
        var syncedInvoices = queryable.Where(i => i.IsSynced).ToList();

        // Assert
        Assert.That(syncedInvoices.Count, Is.EqualTo(1));
        Assert.That(syncedInvoices.First().Id, Is.EqualTo("INV-SYNCED"));
    }
}
```

## Performance Test Scenarios

### Load Testing

```csharp
[TestFixture]
public class IssuedInvoicePerformanceTests
{
    [Test]
    public async Task ImportLargeBatch_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        var largeInvoiceBatch = GenerateInvoices(1000);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await _service.ImportInvoiceAsync(CreateQueryForBatch(largeInvoiceBatch));

        // Assert
        stopwatch.Stop();
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMinutes(10)));
        Assert.That(result.TotalProcessed, Is.EqualTo(1000));
    }

    [Test]
    public async Task ConcurrentImports_ShouldHandleMultipleSources()
    {
        // Arrange
        var importTasks = new List<Task<ImportResultDto>>();

        for (int i = 0; i < 5; i++)
        {
            var query = new IssuedInvoiceSourceQuery
            {
                FromDate = DateTime.Today.AddDays(-i),
                ToDate = DateTime.Today.AddDays(-i),
                RequestId = $"REQ-{i}"
            };
            importTasks.Add(_service.ImportInvoiceAsync(query));
        }

        // Act & Assert
        var results = await Task.WhenAll(importTasks);
        Assert.That(results.Length, Is.EqualTo(5));
        Assert.That(results.All(r => r.RequestId.StartsWith("REQ-")), Is.True);
    }

    private List<IssuedInvoiceDetail> GenerateInvoices(int count)
    {
        var invoices = new List<IssuedInvoiceDetail>();
        for (int i = 1; i <= count; i++)
        {
            var customer = new Customer { Code = $"CUST-{i:000}", Name = $"Customer {i}" };
            var detail = IssuedInvoiceDetail.Create($"DETAIL-{i:000}", customer);
            detail.AddItem(IssuedInvoiceDetailItem.Create($"ITEM-{i:000}", $"Product {i}", 1, 100m, 0.21m));
            invoices.Add(detail);
        }
        return invoices;
    }
}
```

## End-to-End Test Scenarios

### Complete Import Workflow

```csharp
[TestFixture]
public class IssuedInvoiceE2ETests
{
    [Test]
    public async Task CompleteImportWorkflow_FromSourceToERP_ShouldWork()
    {
        // Arrange
        SetupTestData();

        // Act
        // 1. Daily import job execution
        var dailyJobArgs = new IssuedInvoiceDailyImportArgs
        {
            Date = DateTime.Today.AddDays(-1),
            Currency = "CZK"
        };
        await _dailyImportJob.ExecuteAsync(dailyJobArgs);

        // 2. Verify invoices were created
        var invoices = await _repository.GetListAsync();

        // 3. Check synchronization status
        var syncedInvoices = invoices.Where(i => i.IsSynced).ToList();

        // Assert
        Assert.That(invoices.Count, Is.GreaterThan(0));
        Assert.That(syncedInvoices.Count, Is.EqualTo(invoices.Count));
        Assert.That(syncedInvoices.All(i => i.LastSyncTime.HasValue), Is.True);
    }

    [Test]
    public async Task ErrorRecoveryWorkflow_ShouldHandleAndRetry()
    {
        // Arrange
        SetupErrorScenario();

        // Act
        // 1. Import with errors
        var result = await _service.ImportInvoiceAsync(CreateTestQuery());

        // 2. Verify error handling
        Assert.That(result.Failed.Count, Is.GreaterThan(0));

        // 3. Fix underlying issue (e.g., add product mapping)
        await FixProductMapping();

        // 4. Retry failed invoices
        var retryResult = await RetryFailedInvoices(result.Failed);

        // Assert
        Assert.That(retryResult.Succeeded.Count, Is.EqualTo(result.Failed.Count));
    }
}
```

## Error Handling Test Scenarios

### Circuit Breaker Tests

```csharp
[TestFixture]
public class IssuedInvoiceCircuitBreakerTests
{
    [Test]
    public async Task RepeatedErpFailures_ShouldTriggerCircuitBreaker()
    {
        // Arrange
        _mockErpClient
            .Setup(x => x.CreateAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("ERP service unavailable"));

        // Act & Assert
        for (int i = 0; i < 5; i++)
        {
            var query = new IssuedInvoiceSourceQuery { RequestId = $"REQ-{i}" };
            var result = await _service.ImportInvoiceAsync(query);
            Assert.That(result.Failed.Count, Is.GreaterThan(0));
        }

        // Verify circuit breaker is open
        Assert.ThrowsAsync<CircuitBreakerOpenException>(() => 
            _service.ImportInvoiceAsync(new IssuedInvoiceSourceQuery()));
    }
}
```

### Data Validation Tests

```csharp
[TestFixture]
public class IssuedInvoiceValidationTests
{
    [Test]
    public async Task ImportInvalidXml_ShouldHandleGracefully()
    {
        // Arrange
        var invalidXmlContent = "<invalid>unclosed tag";

        // Act & Assert
        Assert.ThrowsAsync<XmlParsingException>(() => 
            _xmlParser.ParseAsync(invalidXmlContent));
    }

    [Test]
    public async Task ImportMissingRequiredFields_ShouldLogValidationErrors()
    {
        // Arrange
        var incompleteInvoice = new IssuedInvoiceDetail
        {
            // Missing required customer information
            Customer = null
        };

        // Act & Assert
        Assert.ThrowsAsync<ValidationException>(() => 
            _service.ProcessInvoiceAsync(incompleteInvoice));
    }
}
```

## Test Data Builders

### Invoice Test Builder

```csharp
public class IssuedInvoiceTestBuilder
{
    private string _invoiceCode = "INV-TEST-001";
    private DateTime _invoiceDate = DateTime.Today;
    private DateTime _dueDate = DateTime.Today.AddDays(14);
    private decimal _price = 1000m;
    private string _currency = "CZK";
    private bool _isSynced = false;
    private List<IssuedInvoiceDetailItem> _items = new();

    public IssuedInvoiceTestBuilder WithInvoiceCode(string code)
    {
        _invoiceCode = code;
        return this;
    }

    public IssuedInvoiceTestBuilder WithDates(DateTime invoiceDate, DateTime dueDate)
    {
        _invoiceDate = invoiceDate;
        _dueDate = dueDate;
        return this;
    }

    public IssuedInvoiceTestBuilder WithPrice(decimal price, string currency = "CZK")
    {
        _price = price;
        _currency = currency;
        return this;
    }

    public IssuedInvoiceTestBuilder WithSyncStatus(bool isSynced)
    {
        _isSynced = isSynced;
        return this;
    }

    public IssuedInvoiceTestBuilder WithItem(string itemId, string name, decimal quantity, decimal price)
    {
        _items.Add(IssuedInvoiceDetailItem.Create(itemId, name, quantity, price, 0.21m));
        return this;
    }

    public IssuedInvoice Build()
    {
        var invoice = IssuedInvoice.Create(_invoiceCode, _invoiceDate, _dueDate, _price, _currency);

        if (_items.Any())
        {
            var customer = new Customer { Code = "CUST-TEST", Name = "Test Customer" };
            var detail = IssuedInvoiceDetail.Create($"DETAIL-{_invoiceCode}", customer);
            
            foreach (var item in _items)
            {
                detail.AddItem(item);
            }
            
            invoice.Detail = detail;
        }

        if (_isSynced)
        {
            invoice.SyncSucceeded(new { ErpId = "ERP-123" });
        }

        return invoice;
    }
}
```

## Test Configuration

### Test Database Setup

```csharp
public class TestDatabaseFixture : IDisposable
{
    public HebroDbContext DbContext { get; private set; }

    public TestDatabaseFixture()
    {
        var options = new DbContextOptionsBuilder<HebroDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .EnableSensitiveDataLogging()
            .Options;

        DbContext = new HebroDbContext(options);
        DbContext.Database.EnsureCreated();
        SeedTestData();
    }

    private void SeedTestData()
    {
        // Add common test data
        var testCustomer = new Customer 
        { 
            Code = "TEST-CUSTOMER", 
            Name = "Test Customer Ltd." 
        };
        
        DbContext.Set<Customer>().Add(testCustomer);
        DbContext.SaveChanges();
    }

    public void Dispose()
    {
        DbContext.Dispose();
    }
}
```

## Continuous Integration Test Pipeline

### Test Categories

```csharp
// Test category attributes
[Category("Unit")]
[Category("Integration")]
[Category("Performance")]
[Category("E2E")]
```

### Test Execution Strategy

```bash
# Unit tests (fast feedback)
dotnet test --filter Category=Unit --no-build --logger "console;verbosity=normal"

# Integration tests (requires test database)
dotnet test --filter Category=Integration --no-build --logger "console;verbosity=normal"

# Performance tests (manual execution)
dotnet test --filter Category=Performance --no-build --logger "console;verbosity=normal"

# End-to-end tests (full system)
dotnet test --filter Category=E2E --no-build --logger "console;verbosity=normal"

# All tests
dotnet test --no-build --logger "console;verbosity=normal" --collect:"XPlat Code Coverage"
```

### Test Reporting

```xml
<!-- In test project file -->
<PropertyGroup>
    <CollectCoverage>true</CollectCoverage>
    <CoverletOutputFormat>opencover</CoverletOutputFormat>
    <CoverletOutput>./coverage.xml</CoverletOutput>
</PropertyGroup>
```