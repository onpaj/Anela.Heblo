# Bank Statement Import - Test Scenarios

## Unit Test Scenarios

### Domain Model Tests

#### BankStatementImport Aggregate Tests

```csharp
[TestFixture]
public class BankStatementImportTests
{
    [Test]
    public void Create_WithValidData_ShouldCreateSuccessfully()
    {
        // Arrange
        var statementDate = DateTime.Today;
        var providerName = "Comgate";
        var transactionId = "TXN-12345";
        var items = new List<BankStatementItem>
        {
            BankStatementItem.Create("TRF-001", DateTime.Now, "ACCOUNT-001", "ACCOUNT-002", "VS123", 100.50m, "CZK")
        };

        // Act
        var statement = BankStatementImport.Create(statementDate, providerName, transactionId, items);

        // Assert
        Assert.That(statement.StatementDate, Is.EqualTo(statementDate));
        Assert.That(statement.ProviderName, Is.EqualTo(providerName));
        Assert.That(statement.ProviderTransactionId, Is.EqualTo(transactionId));
        Assert.That(statement.Status, Is.EqualTo(BankStatementStatus.Pending));
        Assert.That(statement.Items.Count, Is.EqualTo(1));
        Assert.That(statement.ImportDate, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Create_WithEmptyProviderName_ShouldThrowBusinessException()
    {
        // Arrange
        var statementDate = DateTime.Today;
        var items = new List<BankStatementItem>
        {
            BankStatementItem.Create("TRF-001", DateTime.Now, "ACCOUNT-001", "ACCOUNT-002", "VS123", 100.50m, "CZK")
        };

        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            BankStatementImport.Create(statementDate, "", "TXN-123", items));
    }

    [Test]
    public void Create_WithEmptyItems_ShouldThrowBusinessException()
    {
        // Arrange
        var statementDate = DateTime.Today;
        var providerName = "Comgate";
        var transactionId = "TXN-12345";
        var items = new List<BankStatementItem>();

        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            BankStatementImport.Create(statementDate, providerName, transactionId, items));
    }

    [Test]
    public void MarkAsProcessed_WhenPending_ShouldUpdateStatus()
    {
        // Arrange
        var statement = CreateValidStatement();

        // Act
        statement.MarkAsProcessed();

        // Assert
        Assert.That(statement.Status, Is.EqualTo(BankStatementStatus.Processed));
    }

    [Test]
    public void MarkAsProcessed_WhenNotPending_ShouldThrowBusinessException()
    {
        // Arrange
        var statement = CreateValidStatement();
        statement.MarkAsProcessed(); // Already processed

        // Act & Assert
        Assert.Throws<BusinessException>(() => statement.MarkAsProcessed());
    }

    [Test]
    public void MarkAsFailed_WithErrorMessage_ShouldUpdateStatusAndError()
    {
        // Arrange
        var statement = CreateValidStatement();
        var errorMessage = "External service unavailable";

        // Act
        statement.MarkAsFailed(errorMessage);

        // Assert
        Assert.That(statement.Status, Is.EqualTo(BankStatementStatus.Failed));
        Assert.That(statement.ErrorMessage, Is.EqualTo(errorMessage));
    }

    private BankStatementImport CreateValidStatement()
    {
        var items = new List<BankStatementItem>
        {
            BankStatementItem.Create("TRF-001", DateTime.Now, "ACCOUNT-001", "ACCOUNT-002", "VS123", 100.50m, "CZK")
        };
        return BankStatementImport.Create(DateTime.Today, "Comgate", "TXN-12345", items);
    }
}
```

#### BankStatementItem Entity Tests

```csharp
[TestFixture]
public class BankStatementItemTests
{
    [Test]
    public void Create_WithValidData_ShouldCreateSuccessfully()
    {
        // Arrange
        var transferId = "TRF-001";
        var transferDate = DateTime.Now;
        var counterParty = "ACCOUNT-001";
        var outgoing = "ACCOUNT-002";
        var variableSymbol = "VS123";
        var amount = 150.75m;
        var currency = "CZK";

        // Act
        var item = BankStatementItem.Create(transferId, transferDate, counterParty, outgoing, variableSymbol, amount, currency);

        // Assert
        Assert.That(item.TransferId, Is.EqualTo(transferId));
        Assert.That(item.TransferDate, Is.EqualTo(transferDate));
        Assert.That(item.AccountCounterParty, Is.EqualTo(counterParty));
        Assert.That(item.AccountOutgoing, Is.EqualTo(outgoing));
        Assert.That(item.VariableSymbol, Is.EqualTo(variableSymbol));
        Assert.That(item.Amount, Is.EqualTo(amount));
        Assert.That(item.Currency, Is.EqualTo(currency));
    }

    [Test]
    public void Create_WithEmptyTransferId_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            BankStatementItem.Create("", DateTime.Now, "ACCOUNT-001", "ACCOUNT-002", "VS123", 100m, "CZK"));
    }

    [Test]
    public void Create_WithZeroAmount_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            BankStatementItem.Create("TRF-001", DateTime.Now, "ACCOUNT-001", "ACCOUNT-002", "VS123", 0m, "CZK"));
    }

    [Test]
    public void Create_WithNegativeAmount_ShouldThrowBusinessException()
    {
        // Act & Assert
        Assert.Throws<BusinessException>(() => 
            BankStatementItem.Create("TRF-001", DateTime.Now, "ACCOUNT-001", "ACCOUNT-002", "VS123", -50m, "CZK"));
    }
}
```

### Application Service Tests

#### BankStatementImportAppService Tests

```csharp
[TestFixture]
public class BankStatementImportAppServiceTests
{
    private Mock<IBankStatementImportRepository> _mockRepository;
    private Mock<IComgateAdapter> _mockComgateAdapter;
    private Mock<ILogger<BankStatementImportAppService>> _mockLogger;
    private BankStatementImportAppService _service;

    [SetUp]
    public void SetUp()
    {
        _mockRepository = new Mock<IBankStatementImportRepository>();
        _mockComgateAdapter = new Mock<IComgateAdapter>();
        _mockLogger = new Mock<ILogger<BankStatementImportAppService>>();
        
        _service = new BankStatementImportAppService(
            _mockRepository.Object,
            _mockComgateAdapter.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task ImportFromComgateAsync_WithNewStatements_ShouldImportSuccessfully()
    {
        // Arrange
        var comgateStatements = new List<ComgateStatementHeader>
        {
            new ComgateStatementHeader
            {
                TransferId = "TXN-001",
                TransferDate = DateTime.Today.ToString("yyyy-MM-dd"),
                AccountCounterParty = "ACCOUNT-001",
                AccountOutgoing = "ACCOUNT-002",
                VariableSymbol = "VS123"
            }
        };

        _mockComgateAdapter
            .Setup(x => x.GetBankStatementsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(comgateStatements);

        _mockRepository
            .Setup(x => x.ExistsByProviderTransactionIdAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _service.ImportFromComgateAsync();

        // Assert
        Assert.That(result.Statements.Count, Is.EqualTo(1));
        Assert.That(result.ImportedCount, Is.EqualTo(1));
        Assert.That(result.DuplicateCount, Is.EqualTo(0));
        Assert.That(result.ErrorCount, Is.EqualTo(0));

        _mockRepository.Verify(x => x.InsertAsync(It.IsAny<BankStatementImport>(), true), Times.Once);
    }

    [Test]
    public async Task ImportFromComgateAsync_WithDuplicateStatements_ShouldSkipDuplicates()
    {
        // Arrange
        var comgateStatements = new List<ComgateStatementHeader>
        {
            new ComgateStatementHeader
            {
                TransferId = "TXN-001",
                TransferDate = DateTime.Today.ToString("yyyy-MM-dd"),
                AccountCounterParty = "ACCOUNT-001",
                AccountOutgoing = "ACCOUNT-002",
                VariableSymbol = "VS123"
            }
        };

        _mockComgateAdapter
            .Setup(x => x.GetBankStatementsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(comgateStatements);

        _mockRepository
            .Setup(x => x.ExistsByProviderTransactionIdAsync("TXN-001"))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ImportFromComgateAsync();

        // Assert
        Assert.That(result.Statements.Count, Is.EqualTo(0));
        Assert.That(result.ImportedCount, Is.EqualTo(0));
        Assert.That(result.DuplicateCount, Is.EqualTo(1));
        Assert.That(result.ErrorCount, Is.EqualTo(0));

        _mockRepository.Verify(x => x.InsertAsync(It.IsAny<BankStatementImport>(), true), Times.Never);
    }

    [Test]
    public async Task ImportFromComgateAsync_WhenComgateServiceFails_ShouldThrowException()
    {
        // Arrange
        _mockComgateAdapter
            .Setup(x => x.GetBankStatementsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new ExternalServiceException("Comgate API unavailable"));

        // Act & Assert
        Assert.ThrowsAsync<ExternalServiceException>(() => _service.ImportFromComgateAsync());
    }

    [Test]
    public async Task ProcessPendingStatementsAsync_WithPendingStatements_ShouldProcessAll()
    {
        // Arrange
        var pendingStatements = new List<BankStatementImport>
        {
            CreateTestStatement(BankStatementStatus.Pending),
            CreateTestStatement(BankStatementStatus.Pending)
        };

        _mockRepository
            .Setup(x => x.GetPendingStatementsAsync())
            .ReturnsAsync(pendingStatements);

        // Act
        await _service.ProcessPendingStatementsAsync();

        // Assert
        Assert.That(pendingStatements.All(s => s.Status == BankStatementStatus.Processed), Is.True);
        _mockRepository.Verify(x => x.UpdateAsync(It.IsAny<BankStatementImport>(), true), Times.Exactly(2));
    }

    [Test]
    public async Task GetListAsync_WithValidQuery_ShouldReturnPagedResult()
    {
        // Arrange
        var query = new BankStatementImportQueryDto
        {
            SkipCount = 0,
            MaxResultCount = 10,
            StatementDate = DateTime.Today.ToString("yyyy-MM-dd")
        };

        var statements = new List<BankStatementImport>
        {
            CreateTestStatement(BankStatementStatus.Processed),
            CreateTestStatement(BankStatementStatus.Failed)
        };

        _mockRepository
            .Setup(x => x.GetPagedListAsync(0, 10, null, It.IsAny<DateTime?>(), null, null))
            .ReturnsAsync(new PagedResultDto<BankStatementImport>
            {
                Items = statements,
                TotalCount = 2
            });

        // Act
        var result = await _service.GetListAsync(query);

        // Assert
        Assert.That(result.Items.Count, Is.EqualTo(2));
        Assert.That(result.TotalCount, Is.EqualTo(2));
    }

    private BankStatementImport CreateTestStatement(BankStatementStatus status)
    {
        var items = new List<BankStatementItem>
        {
            BankStatementItem.Create("TRF-001", DateTime.Now, "ACCOUNT-001", "ACCOUNT-002", "VS123", 100m, "CZK")
        };
        var statement = BankStatementImport.Create(DateTime.Today, "Comgate", Guid.NewGuid().ToString(), items);
        
        if (status == BankStatementStatus.Processed)
            statement.MarkAsProcessed();
        else if (status == BankStatementStatus.Failed)
            statement.MarkAsFailed("Test error");
            
        return statement;
    }
}
```

### Repository Tests

#### BankStatementImportRepository Tests

```csharp
[TestFixture]
public class BankStatementImportRepositoryTests
{
    private DbContextOptions<HebroDbContext> _dbContextOptions;
    private HebroDbContext _dbContext;
    private BankStatementImportRepository _repository;

    [SetUp]
    public void SetUp()
    {
        _dbContextOptions = new DbContextOptionsBuilder<HebroDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new HebroDbContext(_dbContextOptions);
        _repository = new BankStatementImportRepository(_dbContext);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [Test]
    public async Task ExistsByProviderTransactionIdAsync_WhenExists_ShouldReturnTrue()
    {
        // Arrange
        var transactionId = "TXN-12345";
        var statement = CreateTestStatement(transactionId);
        await _repository.InsertAsync(statement);

        // Act
        var exists = await _repository.ExistsByProviderTransactionIdAsync(transactionId);

        // Assert
        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task ExistsByProviderTransactionIdAsync_WhenNotExists_ShouldReturnFalse()
    {
        // Act
        var exists = await _repository.ExistsByProviderTransactionIdAsync("NONEXISTENT");

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public async Task GetPendingStatementsAsync_ShouldReturnOnlyPendingStatements()
    {
        // Arrange
        var pendingStatement = CreateTestStatement("TXN-001");
        var processedStatement = CreateTestStatement("TXN-002");
        processedStatement.MarkAsProcessed();

        await _repository.InsertAsync(pendingStatement);
        await _repository.InsertAsync(processedStatement);

        // Act
        var pendingStatements = await _repository.GetPendingStatementsAsync();

        // Assert
        Assert.That(pendingStatements.Count, Is.EqualTo(1));
        Assert.That(pendingStatements.First().Status, Is.EqualTo(BankStatementStatus.Pending));
    }

    [Test]
    public async Task GetPagedListAsync_WithDateFilter_ShouldFilterCorrectly()
    {
        // Arrange
        var todayStatement = CreateTestStatement("TXN-TODAY", DateTime.Today);
        var yesterdayStatement = CreateTestStatement("TXN-YESTERDAY", DateTime.Today.AddDays(-1));

        await _repository.InsertAsync(todayStatement);
        await _repository.InsertAsync(yesterdayStatement);

        // Act
        var result = await _repository.GetPagedListAsync(0, 10, null, DateTime.Today, DateTime.Today);

        // Assert
        Assert.That(result.Items.Count, Is.EqualTo(1));
        Assert.That(result.Items.First().ProviderTransactionId, Is.EqualTo("TXN-TODAY"));
    }

    private BankStatementImport CreateTestStatement(string transactionId, DateTime? statementDate = null)
    {
        var items = new List<BankStatementItem>
        {
            BankStatementItem.Create("TRF-001", DateTime.Now, "ACCOUNT-001", "ACCOUNT-002", "VS123", 100m, "CZK")
        };
        return BankStatementImport.Create(statementDate ?? DateTime.Today, "Comgate", transactionId, items);
    }
}
```

### External Service Integration Tests

#### ComgateAdapter Integration Tests

```csharp
[TestFixture]
public class ComgateAdapterIntegrationTests
{
    private Mock<HttpMessageHandler> _mockHttpHandler;
    private HttpClient _httpClient;
    private ComgateAdapter _adapter;

    [SetUp]
    public void SetUp()
    {
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpHandler.Object)
        {
            BaseAddress = new Uri("https://api.comgate.test/")
        };

        var settings = new ComgateSettings { ApiKey = "test-key" };
        var logger = new Mock<ILogger<ComgateAdapter>>();
        
        _adapter = new ComgateAdapter(_httpClient, logger.Object, settings);
    }

    [Test]
    public async Task GetBankStatementsAsync_WithValidResponse_ShouldReturnStatements()
    {
        // Arrange
        var jsonResponse = @"[
            {
                ""TransferId"": ""TXN-001"",
                ""TransferDate"": ""2024-01-15"",
                ""AccountCounterParty"": ""ACCOUNT-001"",
                ""AccountOutgoing"": ""ACCOUNT-002"",
                ""VariableSymbol"": ""VS123""
            }
        ]";

        _mockHttpHandler
            .Setup(handler => handler.SendAsync(
                It.IsAny<HttpRequestMessage>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            });

        // Act
        var statements = await _adapter.GetBankStatementsAsync(DateTime.Today.AddDays(-7), DateTime.Today);

        // Assert
        Assert.That(statements.Count, Is.EqualTo(1));
        Assert.That(statements.First().TransferId, Is.EqualTo("TXN-001"));
    }

    [Test]
    public void GetBankStatementsAsync_WithErrorResponse_ShouldThrowException()
    {
        // Arrange
        _mockHttpHandler
            .Setup(handler => handler.SendAsync(
                It.IsAny<HttpRequestMessage>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        // Act & Assert
        Assert.ThrowsAsync<ExternalServiceException>(() => 
            _adapter.GetBankStatementsAsync(DateTime.Today.AddDays(-7), DateTime.Today));
    }
}
```

## Integration Test Scenarios

### Background Service Integration Tests

```csharp
[TestFixture]
public class BankStatementImportHostedServiceIntegrationTests
{
    private TestHost _testHost;
    private Mock<IComgateAdapter> _mockComgateAdapter;

    [SetUp]
    public async Task SetUp()
    {
        _mockComgateAdapter = new Mock<IComgateAdapter>();
        
        _testHost = new TestHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_mockComgateAdapter.Object);
                services.AddHostedService<BankStatementImportHostedService>();
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
    public async Task BackgroundService_ShouldCallImportPeriodically()
    {
        // Arrange
        _mockComgateAdapter
            .Setup(x => x.GetBankStatementsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<ComgateStatementHeader>());

        // Act
        await Task.Delay(TimeSpan.FromSeconds(2)); // Wait for service execution

        // Assert
        _mockComgateAdapter.Verify(
            x => x.GetBankStatementsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()),
            Times.AtLeastOnce);
    }
}
```

## Performance Test Scenarios

### Load Testing

```csharp
[TestFixture]
public class BankStatementImportPerformanceTests
{
    [Test]
    public async Task ImportLargeStatementBatch_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        var largeStatementBatch = GenerateStatements(10000);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await _service.ImportFromComgateAsync();

        // Assert
        stopwatch.Stop();
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMinutes(5)));
        Assert.That(result.ImportedCount, Is.EqualTo(10000));
    }

    [Test]
    public async Task ConcurrentImports_ShouldHandleMultipleProviders()
    {
        // Arrange
        var tasks = new List<Task>();
        
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_service.ImportFromComgateAsync());
        }

        // Act & Assert
        Assert.DoesNotThrowAsync(() => Task.WhenAll(tasks));
    }
}
```

## End-to-End Test Scenarios

### Complete Import Workflow

```csharp
[TestFixture]
public class BankStatementImportE2ETests
{
    [Test]
    public async Task CompleteImportWorkflow_FromComgateToDatabase_ShouldWork()
    {
        // Arrange
        var testStatements = SetupComgateTestData();

        // Act
        // 1. Import from Comgate
        var importResult = await _importService.ImportFromComgateAsync();
        
        // 2. Process pending statements
        await _importService.ProcessPendingStatementsAsync();
        
        // 3. Query imported statements
        var queryResult = await _importService.GetListAsync(new BankStatementImportQueryDto
        {
            SkipCount = 0,
            MaxResultCount = 100
        });

        // Assert
        Assert.That(importResult.ImportedCount, Is.GreaterThan(0));
        Assert.That(queryResult.Items.All(s => s.Status == BankStatementStatus.Processed), Is.True);
    }
}
```

## Error Handling Test Scenarios

### Circuit Breaker Tests

```csharp
[TestFixture]
public class BankStatementImportCircuitBreakerTests
{
    [Test]
    public async Task RepeatedFailures_ShouldTriggerCircuitBreaker()
    {
        // Arrange
        _mockComgateAdapter
            .Setup(x => x.GetBankStatementsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        // Act & Assert
        for (int i = 0; i < 5; i++)
        {
            Assert.ThrowsAsync<HttpRequestException>(() => _service.ImportFromComgateAsync());
        }

        // Verify circuit breaker is open
        Assert.ThrowsAsync<CircuitBreakerOpenException>(() => _service.ImportFromComgateAsync());
    }
}
```

## Test Data Builders

### Test Statement Builder

```csharp
public class BankStatementImportTestBuilder
{
    private DateTime _statementDate = DateTime.Today;
    private string _providerName = "Comgate";
    private string _transactionId = Guid.NewGuid().ToString();
    private List<BankStatementItem> _items = new();

    public BankStatementImportTestBuilder WithStatementDate(DateTime date)
    {
        _statementDate = date;
        return this;
    }

    public BankStatementImportTestBuilder WithProvider(string provider)
    {
        _providerName = provider;
        return this;
    }

    public BankStatementImportTestBuilder WithTransactionId(string id)
    {
        _transactionId = id;
        return this;
    }

    public BankStatementImportTestBuilder WithItem(string transferId, decimal amount)
    {
        _items.Add(BankStatementItem.Create(
            transferId, 
            DateTime.Now, 
            "ACCOUNT-001", 
            "ACCOUNT-002", 
            "VS123", 
            amount, 
            "CZK"));
        return this;
    }

    public BankStatementImport Build()
    {
        if (!_items.Any())
        {
            WithItem("DEFAULT-TRF", 100m);
        }

        return BankStatementImport.Create(_statementDate, _providerName, _transactionId, _items);
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
            .Options;

        DbContext = new HebroDbContext(options);
        DbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        DbContext.Dispose();
    }
}
```

## Continuous Integration Test Pipeline

### Test Categories

- **Unit Tests**: Fast, isolated tests for domain logic
- **Integration Tests**: Database and external service integration
- **Performance Tests**: Load and stress testing
- **E2E Tests**: Complete workflow validation

### Test Execution Strategy

```bash
# Unit tests (fast feedback)
dotnet test --filter Category=Unit --no-build

# Integration tests (slower, requires test database)
dotnet test --filter Category=Integration --no-build

# Performance tests (manual execution)
dotnet test --filter Category=Performance --no-build

# All tests
dotnet test --no-build --verbosity normal
```