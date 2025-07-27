# Bank Statement Import User Story

## Feature Overview
The Bank Statement Import feature enables automated import and processing of bank statements from payment gateway providers (primarily Comgate) into the system. This feature provides scheduled synchronization, statement validation, duplicate detection, and audit trail functionality.

## Business Requirements

### Primary Use Case
As a financial administrator, I want to automatically import bank statements from external payment providers so that I can reconcile payments and maintain accurate financial records without manual data entry.

### Acceptance Criteria
1. The system shall automatically import bank statements from Comgate on a scheduled basis
2. The system shall validate imported statement data for completeness and format compliance
3. The system shall detect and prevent duplicate statement imports
4. The system shall maintain an audit trail of all import operations
5. The system shall handle import failures gracefully with proper error logging
6. The system shall provide manual import capabilities for administrators

## Technical Contracts

### Domain Model

```csharp
// Primary aggregate root
public class BankStatementImport : AggregateRoot<int>
{
    public DateTime StatementDate { get; private set; }
    public DateTime ImportDate { get; private set; }
    public string ProviderName { get; private set; } // e.g., "Comgate"
    public string ProviderTransactionId { get; private set; }
    public BankStatementStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public List<BankStatementItem> Items { get; private set; }
    
    private BankStatementImport() { } // EF Constructor
    
    public static BankStatementImport Create(
        DateTime statementDate,
        string providerName,
        string providerTransactionId,
        List<BankStatementItem> items)
    {
        // Business rule validation
        if (string.IsNullOrEmpty(providerName))
            throw new BusinessException("Provider name is required");
        
        if (items == null || !items.Any())
            throw new BusinessException("Statement must contain at least one item");
        
        return new BankStatementImport
        {
            StatementDate = statementDate,
            ImportDate = DateTime.UtcNow,
            ProviderName = providerName,
            ProviderTransactionId = providerTransactionId,
            Status = BankStatementStatus.Pending,
            Items = items
        };
    }
    
    public void MarkAsProcessed()
    {
        if (Status != BankStatementStatus.Pending)
            throw new BusinessException("Only pending statements can be marked as processed");
        
        Status = BankStatementStatus.Processed;
    }
    
    public void MarkAsFailed(string errorMessage)
    {
        Status = BankStatementStatus.Failed;
        ErrorMessage = errorMessage;
    }
}

// Value object for statement items
public class BankStatementItem : Entity<int>
{
    public string TransferId { get; private set; }
    public DateTime TransferDate { get; private set; }
    public string AccountCounterParty { get; private set; }
    public string AccountOutgoing { get; private set; }
    public string VariableSymbol { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    
    private BankStatementItem() { } // EF Constructor
    
    public static BankStatementItem Create(
        string transferId,
        DateTime transferDate,
        string accountCounterParty,
        string accountOutgoing,
        string variableSymbol,
        decimal amount,
        string currency)
    {
        if (string.IsNullOrEmpty(transferId))
            throw new BusinessException("Transfer ID is required");
        
        if (amount <= 0)
            throw new BusinessException("Amount must be positive");
        
        return new BankStatementItem
        {
            TransferId = transferId,
            TransferDate = transferDate,
            AccountCounterParty = accountCounterParty,
            AccountOutgoing = accountOutgoing,
            VariableSymbol = variableSymbol,
            Amount = amount,
            Currency = currency
        };
    }
}

// Status enumeration
public enum BankStatementStatus
{
    Pending = 0,
    Processed = 1,
    Failed = 2,
    Duplicate = 3
}
```

### Application Layer Contracts

```csharp
// Application service interface
public interface IBankStatementImportAppService : IApplicationService
{
    Task<BankStatementImportResultDto> ImportFromComgateAsync();
    Task<PagedResultDto<BankStatementImportDto>> GetListAsync(BankStatementImportQueryDto input);
    Task<BankStatementImportDto> GetAsync(int id);
    Task ProcessPendingStatementsAsync();
}

// DTOs
public class BankStatementImportDto
{
    public int Id { get; set; }
    public DateTime StatementDate { get; set; }
    public DateTime ImportDate { get; set; }
    public string ProviderName { get; set; }
    public string ProviderTransactionId { get; set; }
    public BankStatementStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public List<BankStatementItemDto> Items { get; set; }
}

public class BankStatementItemDto
{
    public string TransferId { get; set; }
    public DateTime TransferDate { get; set; }
    public string AccountCounterParty { get; set; }
    public string AccountOutgoing { get; set; }
    public string VariableSymbol { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
}

public class BankStatementImportResultDto
{
    public List<BankStatementImportDto> Statements { get; set; } = new();
    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int ErrorCount { get; set; }
}
```

### Repository Pattern

```csharp
public interface IBankStatementImportRepository : IRepository<BankStatementImport, int>
{
    Task<bool> ExistsByProviderTransactionIdAsync(string providerTransactionId);
    Task<List<BankStatementImport>> GetPendingStatementsAsync();
    Task<PagedResultDto<BankStatementImport>> GetPagedListAsync(
        int skipCount, 
        int maxResultCount, 
        string sorting = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        BankStatementStatus? status = null);
}
```

## Implementation Details

### Background Service Implementation

```csharp
public class BankStatementImportHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BankStatementImportHostedService> _logger;
    private readonly TimeSpan _importInterval = TimeSpan.FromHours(1); // Configurable
    
    public BankStatementImportHostedService(
        IServiceProvider serviceProvider,
        ILogger<BankStatementImportHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var importService = scope.ServiceProvider.GetRequiredService<IBankStatementImportAppService>();
                
                await importService.ImportFromComgateAsync();
                await importService.ProcessPendingStatementsAsync();
                
                _logger.LogInformation("Bank statement import completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bank statement import");
            }
            
            await Task.Delay(_importInterval, stoppingToken);
        }
    }
}
```

### External Provider Integration

```csharp
public interface IComgateAdapter
{
    Task<List<ComgateStatementHeader>> GetBankStatementsAsync(DateTime fromDate, DateTime toDate);
}

public class ComgateAdapter : IComgateAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ComgateAdapter> _logger;
    private readonly ComgateSettings _settings;
    
    public async Task<List<ComgateStatementHeader>> GetBankStatementsAsync(DateTime fromDate, DateTime toDate)
    {
        // Implementation with circuit breaker pattern
        var response = await _httpClient.GetAsync($"/api/statements?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}");
        
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalServiceException($"Comgate API returned {response.StatusCode}");
        }
        
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<ComgateStatementHeader>>(content);
    }
}
```

## Happy Day Scenario

1. **Scheduled Trigger**: Background service triggers import process every hour
2. **Data Retrieval**: System connects to Comgate API and retrieves new statements
3. **Data Validation**: Each statement is validated for required fields and format
4. **Duplicate Check**: System checks if statement already exists using ProviderTransactionId
5. **Domain Object Creation**: Valid statements are converted to domain aggregates
6. **Persistence**: Statements are saved to database with Pending status
7. **Processing**: Background processor validates business rules and marks as Processed
8. **Audit Log**: All operations are logged for audit trail

## Error Handling

### External Service Failures
- **Comgate API Unavailable**: Log error, retry with exponential backoff
- **Network Timeout**: Implement circuit breaker pattern
- **Authentication Failure**: Alert administrators, stop import process

### Data Validation Errors
- **Missing Required Fields**: Log validation errors, mark statement as Failed
- **Invalid Date Format**: Transform or reject with detailed error message
- **Duplicate Detection**: Mark as Duplicate status, log for review

### System Errors
- **Database Connection Issues**: Retry mechanism with dead letter queue
- **Memory Issues**: Implement batch processing for large statement sets
- **Concurrency Issues**: Use pessimistic locking for critical sections

## Persistence Layer Requirements

### Database Schema
```sql
CREATE TABLE BankStatementImports (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    StatementDate DATETIME2 NOT NULL,
    ImportDate DATETIME2 NOT NULL,
    ProviderName NVARCHAR(100) NOT NULL,
    ProviderTransactionId NVARCHAR(255) NOT NULL,
    Status INT NOT NULL,
    ErrorMessage NVARCHAR(MAX) NULL,
    CreationTime DATETIME2 NOT NULL,
    CreatorId UNIQUEIDENTIFIER NULL
);

CREATE TABLE BankStatementItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BankStatementImportId INT NOT NULL,
    TransferId NVARCHAR(255) NOT NULL,
    TransferDate DATETIME2 NOT NULL,
    AccountCounterParty NVARCHAR(255) NULL,
    AccountOutgoing NVARCHAR(255) NULL,
    VariableSymbol NVARCHAR(50) NULL,
    Amount DECIMAL(18,2) NOT NULL,
    Currency NVARCHAR(3) NOT NULL,
    FOREIGN KEY (BankStatementImportId) REFERENCES BankStatementImports(Id)
);

CREATE UNIQUE INDEX IX_BankStatementImports_ProviderTransactionId 
    ON BankStatementImports(ProviderTransactionId);
```

### Caching Strategy
- **Read Cache**: Cache frequently accessed statements (last 30 days) using Redis
- **Write-Through**: Update cache on successful import
- **Cache Invalidation**: TTL of 24 hours for statement data
- **Cache Keys**: `bankstatement:provider:{providerId}:date:{date}`

## Integration Requirements

### Job Scheduling
- Use Hangfire for reliable background job processing
- Implement retry policies for failed import attempts
- Configure job persistence for system restarts

### Monitoring and Alerting
- Log all import operations with correlation IDs
- Set up alerts for consecutive import failures
- Monitor API response times and success rates
- Track duplicate detection rates for anomaly detection

### Security Considerations
- Encrypt sensitive account information at rest
- Use secure connections (HTTPS/TLS) for external API calls
- Implement proper authorization for manual import endpoints
- Audit all access to financial data

## Performance Requirements
- Handle up to 10,000 statements per import batch
- Complete import cycle within 5 minutes under normal load
- Support concurrent processing of multiple provider imports
- Maintain sub-second response times for query operations