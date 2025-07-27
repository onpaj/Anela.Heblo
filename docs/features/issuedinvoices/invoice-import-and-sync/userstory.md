# Invoice Import and Sync User Story

## Feature Overview
The Invoice Import and Sync feature enables automated import of sales invoices from e-commerce platforms (primarily Shoptet) and their synchronization with the ERP system (FlexiBee). This feature includes data transformation, validation, error handling, and comprehensive audit trails to ensure financial compliance and operational efficiency.

## Business Requirements

### Primary Use Case
As a financial administrator, I want to automatically import invoices from e-commerce platforms and synchronize them with our ERP system so that I can maintain accurate financial records without manual data entry and ensure all sales are properly recorded for compliance purposes.

### Acceptance Criteria
1. The system shall import invoices from multiple sources (Shoptet e-commerce, Dropbox files, manual upload)
2. The system shall transform imported data according to business rules and product mappings
3. The system shall synchronize invoices with FlexiBee ERP system
4. The system shall track synchronization status and maintain audit history
5. The system shall handle errors gracefully with categorization and retry mechanisms
6. The system shall support both automated daily imports and manual single invoice imports
7. The system shall provide comprehensive reporting on import success/failure rates

## Technical Contracts

### Domain Model

```csharp
// Primary aggregate root
public class IssuedInvoice : AuditedAggregateRoot<string>
{
    // Natural key: Invoice code
    public string Id { get; set; } // Invoice code (e.g., "INV-2024-001")
    
    // Temporal properties
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime TaxDate { get; set; }
    
    // Financial information
    public decimal Price { get; set; } = 0; // Total in primary currency
    public decimal PriceC { get; set; } = 0; // Total in alternative currency
    public string Currency { get; set; } = "CZK";
    public long? VarSymbol { get; set; } // Variable symbol for payments
    
    // Customer information
    public string? CustomerName { get; set; }
    public bool? VatPayer { get; set; }
    
    // Business classification
    public BillingMethod BillingMethod { get; set; }
    public ShippingMethod ShippingMethod { get; set; }
    public int ItemsCount { get; set; }
    
    // Synchronization status (denormalized for performance)
    public bool IsSynced { get; private set; }
    public DateTime? LastSyncTime { get; private set; }
    public string? ErrorMessage { get; private set; }
    public IssuedInvoiceErrorType? ErrorType { get; private set; }
    
    // Audit trail
    public IList<IssuedInvoiceSyncData> SyncHistory { get; set; } = new List<IssuedInvoiceSyncData>();
    public int SyncHistoryCount { get; set; } = 0;
    
    // Business rules
    public bool IsCriticalError => ErrorType != null && ErrorType != IssuedInvoiceErrorType.InvoicePaired;
    
    public static IssuedInvoice Create(
        string invoiceCode,
        DateTime invoiceDate,
        DateTime dueDate,
        decimal price,
        string currency = "CZK")
    {
        if (string.IsNullOrEmpty(invoiceCode))
            throw new BusinessException("Invoice code is required");
        
        if (invoiceDate > dueDate)
            throw new BusinessException("Invoice date cannot be after due date");
        
        if (price < 0)
            throw new BusinessException("Price cannot be negative");
        
        return new IssuedInvoice
        {
            Id = invoiceCode,
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            TaxDate = invoiceDate,
            Price = price,
            Currency = currency,
            IsSynced = false
        };
    }
    
    public void SyncSucceeded(object syncedInvoice)
    {
        var lastSync = new IssuedInvoiceSyncData()
        {
            IsSuccess = true,
            Error = null,
            SyncTime = DateTime.UtcNow,
            Data = JsonConvert.SerializeObject(syncedInvoice)
        };
        
        SetLastSync(lastSync);
    }
    
    public void SyncFailed(object syncedInvoice, IssuedInvoiceError error)
    {
        var lastSync = new IssuedInvoiceSyncData()
        {
            IsSuccess = false,
            Error = error,
            SyncTime = DateTime.UtcNow,
            Data = JsonConvert.SerializeObject(syncedInvoice)
        };
        
        SetLastSync(lastSync);
    }
    
    private void SetLastSync(IssuedInvoiceSyncData lastSync)
    {
        SyncHistory.Add(lastSync);
        
        IsSynced = lastSync.IsSuccess;
        SyncHistoryCount = SyncHistory.Count;
        LastSyncTime = lastSync.SyncTime;
        ErrorType = lastSync.Error?.ErrorType;
        ErrorMessage = lastSync.Error?.Message;
    }
}

// Detailed invoice information
public class IssuedInvoiceDetail : Entity<string>
{
    public string DetailId { get; set; }
    public Customer Customer { get; set; }
    public Address BillingAddress { get; set; }
    public Address DeliveryAddress { get; set; }
    public string? Note { get; set; }
    public List<IssuedInvoiceDetailItem> Items { get; set; } = new();
    
    public static IssuedInvoiceDetail Create(string detailId, Customer customer)
    {
        if (string.IsNullOrEmpty(detailId))
            throw new BusinessException("Detail ID is required");
        
        if (customer == null)
            throw new BusinessException("Customer is required");
        
        return new IssuedInvoiceDetail
        {
            DetailId = detailId,
            Customer = customer,
            Items = new List<IssuedInvoiceDetailItem>()
        };
    }
    
    public void AddItem(IssuedInvoiceDetailItem item)
    {
        if (item == null)
            throw new BusinessException("Item cannot be null");
        
        Items.Add(item);
    }
    
    public decimal CalculateTotalPrice()
    {
        return Items.Sum(i => i.PriceSum);
    }
}

// Invoice line item
public class IssuedInvoiceDetailItem : Entity<string>
{
    public string ItemId { get; set; }
    public string Name { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; }
    public decimal Price { get; set; } // Unit price
    public decimal PriceSum { get; set; } // Total line price
    public decimal PriceSumVat { get; set; } // Total including VAT
    public decimal Tax { get; set; } // Tax rate (0.21 = 21%)
    public string Type { get; set; } // "product", "service"
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierNumber { get; set; }
    
    public static IssuedInvoiceDetailItem Create(
        string itemId,
        string name,
        decimal quantity,
        decimal price,
        decimal tax)
    {
        if (string.IsNullOrEmpty(itemId))
            throw new BusinessException("Item ID is required");
        
        if (string.IsNullOrEmpty(name))
            throw new BusinessException("Item name is required");
        
        if (quantity <= 0)
            throw new BusinessException("Quantity must be positive");
        
        if (price < 0)
            throw new BusinessException("Price cannot be negative");
        
        var item = new IssuedInvoiceDetailItem
        {
            ItemId = itemId,
            Name = name,
            Quantity = quantity,
            Price = price,
            Tax = tax,
            Unit = "ks"
        };
        
        item.CalculatePrices();
        return item;
    }
    
    private void CalculatePrices()
    {
        PriceSum = Quantity * Price;
        PriceSumVat = PriceSum * (1 + Tax);
    }
}

// Sync audit trail
public class IssuedInvoiceSyncData : Entity<int>
{
    public bool IsSuccess { get; set; }
    public IssuedInvoiceError? Error { get; set; }
    public DateTime SyncTime { get; set; }
    public string? Data { get; set; } // JSON of synced object
}

// Error information
public class IssuedInvoiceError
{
    public string Message { get; set; }
    public IssuedInvoiceErrorType ErrorType { get; set; }
}

// Error categorization
public enum IssuedInvoiceErrorType
{
    General = 0,
    InvoicePaired = 1, // Non-critical: Invoice already exists in ERP
    ProductNotFound = 2 // Critical: Product code mapping missing
}

// Payment methods
public enum BillingMethod
{
    BankTransfer = 0,
    Cash = 1,
    CoD = 2,
    Comgate = 3,
    CreditCard = 4
}

// Shipping methods
public enum ShippingMethod
{
    PickUp = 0,
    PPL = 1,
    PPLParcelShop = 2,
    Zasilkovna = 3,
    GLS = 4
}
```

### Application Layer Contracts

```csharp
// Application service interface
public interface IIssuedInvoiceAppService : ICrudAppService<IssuedInvoiceDto, string, IssuedInvoiceRequestDto>
{
    Task<IssuedInvoiceDto> GetAsync(string id, bool withDetails);
    Task<List<string>> EnqueueImportInvoiceAsync(ImportInvoiceRequestDto request, CancellationToken cancellationToken = default);
    Task<ImportResultDto> ImportInvoiceAsync(IssuedInvoiceSourceQuery query, CancellationToken cancellationToken = default);
    Task<List<CashRegisterOrderResult>> GetCashRegisterOrdersAsync(CashRegistryRequestDto request, CancellationToken cancellationToken = default);
}

// DTOs
public class IssuedInvoiceDto
{
    public string Id { get; set; } // Invoice code
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime TaxDate { get; set; }
    public long? VarSymbol { get; set; }
    public decimal Price { get; set; }
    public decimal PriceC { get; set; }
    public string Currency { get; set; }
    public string? CustomerName { get; set; }
    public bool? VatPayer { get; set; }
    public BillingMethod BillingMethod { get; set; }
    public ShippingMethod ShippingMethod { get; set; }
    public int ItemsCount { get; set; }
    public bool IsSynced { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public string? ErrorMessage { get; set; }
    public IssuedInvoiceErrorType? ErrorType { get; set; }
    public int SyncHistoryCount { get; set; }
    public bool IsCriticalError { get; set; }
    public List<IssuedInvoiceSyncDataDto>? SyncData { get; set; }
}

public class ImportInvoiceRequestDto
{
    public List<string> InvoiceIds { get; set; } = new();
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Currency { get; set; }
    public bool TryUnpairIfNecessary { get; set; }
}

public class ImportResultDto
{
    public string RequestId { get; set; }
    public List<string> Succeeded { get; set; } = new();
    public List<string> Failed { get; set; } = new();
    public int TotalProcessed => Succeeded.Count + Failed.Count;
    public double SuccessRate => TotalProcessed > 0 ? (double)Succeeded.Count / TotalProcessed : 0;
}

public class IssuedInvoiceRequestDto : PagedAndSortedResultRequestDto
{
    public string? Code { get; set; }
    public string? InvoiceDate { get; set; }
    public string? SyncDate { get; set; }
    public bool? Synced { get; set; }
    public string? ErrorType { get; set; } // Supports "!" prefix for exclusion
}
```

### Repository Pattern

```csharp
public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    Task<List<IssuedInvoice>> GetUnsyncedInvoicesAsync();
    Task<List<IssuedInvoice>> GetInvoicesByDateRangeAsync(DateTime fromDate, DateTime toDate);
    Task<PagedResultDto<IssuedInvoice>> GetPagedListAsync(
        int skipCount,
        int maxResultCount,
        string sorting = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        bool? isSynced = null,
        IssuedInvoiceErrorType? errorType = null);
}
```

## Implementation Details

### Data Source Abstraction

```csharp
public interface IIssuedInvoiceSource
{
    Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query);
    Task CommitAsync(IssuedInvoiceDetailBatch batch);
    Task FailAsync(IssuedInvoiceDetailBatch batch);
}

// Shoptet implementation
public class ShoptetIssuedInvoiceSource : IIssuedInvoiceSource
{
    private readonly IPlaywrightService _playwrightService;
    private readonly IDropboxService _dropboxService;
    private readonly ILogger<ShoptetIssuedInvoiceSource> _logger;
    
    public async Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query)
    {
        var batches = new List<IssuedInvoiceDetailBatch>();
        
        // 1. Playwright automation for direct access
        if (query.UsePlaywright)
        {
            var playwrightBatch = await GetFromPlaywrightAsync(query);
            if (playwrightBatch != null)
                batches.Add(playwrightBatch);
        }
        
        // 2. Dropbox file monitoring
        if (query.UseDropbox)
        {
            var dropboxBatches = await GetFromDropboxAsync(query);
            batches.AddRange(dropboxBatches);
        }
        
        return batches;
    }
    
    private async Task<IssuedInvoiceDetailBatch> GetFromPlaywrightAsync(IssuedInvoiceSourceQuery query)
    {
        using var browser = await _playwrightService.LaunchBrowserAsync();
        var page = await browser.NewPageAsync();
        
        // Navigate to Shoptet admin
        await page.GotoAsync("https://admin.shoptet.cz/login");
        
        // Perform authentication
        await AuthenticateAsync(page);
        
        // Navigate to invoice export
        await page.GotoAsync("https://admin.shoptet.cz/invoices/export");
        
        // Set date range
        await page.FillAsync("#dateFrom", query.FromDate?.ToString("dd.MM.yyyy"));
        await page.FillAsync("#dateTo", query.ToDate?.ToString("dd.MM.yyyy"));
        
        // Start export and download
        await page.ClickAsync("#exportButton");
        var downloadTask = page.WaitForDownloadAsync();
        var download = await downloadTask;
        
        // Process downloaded XML
        var xmlPath = await download.SaveAsAsync();
        return await ParseXmlFileAsync(xmlPath);
    }
    
    private async Task<List<IssuedInvoiceDetailBatch>> GetFromDropboxAsync(IssuedInvoiceSourceQuery query)
    {
        var batches = new List<IssuedInvoiceDetailBatch>();
        var files = await _dropboxService.ListFilesAsync("/invoices/new");
        
        foreach (var file in files.Where(f => f.Name.EndsWith(".xml")))
        {
            try
            {
                // Move to processing folder
                await _dropboxService.MoveAsync(file.Path, "/invoices/processing");
                
                // Download and parse
                var content = await _dropboxService.DownloadAsync(file.Path);
                var batch = await ParseXmlContentAsync(content);
                
                if (batch != null)
                    batches.Add(batch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file {FileName}", file.Name);
                await _dropboxService.MoveAsync(file.Path, "/invoices/failed");
            }
        }
        
        return batches;
    }
}
```

### Transformation Pipeline

```csharp
public interface IIssuedInvoiceImportTransformation
{
    IssuedInvoice Transform(IssuedInvoice invoice);
}

// Product code mapping transformation
public class ProductMappingTransformation : IIssuedInvoiceImportTransformation
{
    private readonly IProductMappingRepository _mappingRepository;
    
    public IssuedInvoice Transform(IssuedInvoice invoice)
    {
        foreach (var item in invoice.Detail.Items)
        {
            var mapping = await _mappingRepository.GetMappingAsync(item.SupplierCode);
            if (mapping != null)
            {
                item.SupplierCode = mapping.ErpCode;
                item.SupplierName = mapping.ErpName;
            }
        }
        
        return invoice;
    }
}

// Gift item VAT transformation
public class GiftWithoutVATTransformation : IIssuedInvoiceImportTransformation
{
    public IssuedInvoice Transform(IssuedInvoice invoice)
    {
        foreach (var item in invoice.Detail.Items)
        {
            if (IsGiftItem(item))
            {
                item.Tax = 0; // Remove VAT for gift items
                item.Type = "service"; // Change type for tax compliance
            }
        }
        
        return invoice;
    }
    
    private bool IsGiftItem(IssuedInvoiceDetailItem item)
    {
        return item.Name.ToLower().Contains("gift") || 
               item.Name.ToLower().Contains("dárek") ||
               item.Price == 0;
    }
}

// Product code cleanup transformation
public class RemoveDAtTheEndTransformation : IIssuedInvoiceImportTransformation
{
    public IssuedInvoice Transform(IssuedInvoice invoice)
    {
        foreach (var item in invoice.Detail.Items)
        {
            if (item.SupplierCode?.EndsWith("D") == true)
            {
                item.SupplierCode = item.SupplierCode.Substring(0, item.SupplierCode.Length - 1);
            }
        }
        
        return invoice;
    }
}
```

### Background Job Implementation

```csharp
// Daily import job
public class IssuedInvoiceDailyImportJob : AsyncBackgroundJob<IssuedInvoiceDailyImportArgs>
{
    private readonly IIssuedInvoiceAppService _appService;
    private readonly ILogger<IssuedInvoiceDailyImportJob> _logger;
    
    public override async Task ExecuteAsync(IssuedInvoiceDailyImportArgs args)
    {
        try
        {
            var query = new IssuedInvoiceSourceQuery
            {
                FromDate = args.Date.Date,
                ToDate = args.Date.Date.AddDays(1).AddTicks(-1),
                Currency = args.Currency,
                RequestId = Guid.NewGuid().ToString()
            };
            
            var result = await _appService.ImportInvoiceAsync(query);
            
            _logger.LogInformation(
                "Daily import completed. Processed: {Total}, Success: {Success}, Failed: {Failed}",
                result.TotalProcessed, result.Succeeded.Count, result.Failed.Count);
                
            if (result.Failed.Any())
            {
                _logger.LogWarning("Failed invoices: {FailedInvoices}", string.Join(", ", result.Failed));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily import failed for date {Date}", args.Date);
            throw;
        }
    }
}

// Single invoice import job
public class IssuedInvoiceSingleImportJob : AsyncBackgroundJob<IssuedInvoiceSingleImportArgs>
{
    private readonly IIssuedInvoiceAppService _appService;
    
    public override async Task ExecuteAsync(IssuedInvoiceSingleImportArgs args)
    {
        var query = new IssuedInvoiceSourceQuery
        {
            InvoiceCode = args.InvoiceCode,
            Currency = args.Currency,
            RequestId = Guid.NewGuid().ToString()
        };
        
        await _appService.ImportInvoiceAsync(query);
    }
}
```

## Happy Day Scenario

1. **Scheduled Trigger**: Daily job triggers at 2:00 AM for previous day's invoices
2. **Data Source Access**: System connects to Shoptet via Playwright automation
3. **Data Extraction**: XML invoice file is downloaded and parsed
4. **Data Transformation**: Business rules are applied (product mapping, VAT adjustments)
5. **Domain Object Creation**: Parsed data is converted to domain aggregates
6. **ERP Synchronization**: Invoice is sent to FlexiBee via API
7. **Status Update**: Success status is recorded with audit trail
8. **File Management**: Source file is moved to success folder
9. **Completion Report**: Summary statistics are logged

## Error Handling

### Data Source Errors
- **Shoptet Authentication Failure**: Retry with fresh credentials, alert administrators
- **Network Timeouts**: Implement exponential backoff retry mechanism
- **File Parsing Errors**: Move file to error folder, log detailed parsing issues

### Transformation Errors
- **Product Not Found**: Mark as critical error, require manual product mapping
- **Invalid Data Format**: Log validation errors, skip problematic records
- **Business Rule Violations**: Apply default transformations where possible

### ERP Synchronization Errors
- **Invoice Already Exists**: Mark as non-critical (InvoicePaired), optionally auto-unpair
- **Product Code Missing**: Critical error requiring product master data update
- **API Errors**: Implement circuit breaker pattern, retry with backoff

### System Errors
- **Database Failures**: Use unit of work pattern for transaction integrity
- **Memory Issues**: Implement streaming for large batches
- **Concurrency Issues**: Use optimistic concurrency control

## Persistence Layer Requirements

### Database Schema
```sql
CREATE TABLE IssuedInvoices (
    Id NVARCHAR(50) PRIMARY KEY, -- Invoice code
    InvoiceDate DATETIME2 NOT NULL,
    DueDate DATETIME2 NOT NULL,
    TaxDate DATETIME2 NOT NULL,
    VarSymbol BIGINT NULL,
    Price DECIMAL(18,2) NOT NULL,
    PriceC DECIMAL(18,2) NOT NULL,
    Currency NVARCHAR(3) NOT NULL,
    CustomerName NVARCHAR(255) NULL,
    VatPayer BIT NULL,
    BillingMethod INT NOT NULL,
    ShippingMethod INT NOT NULL,
    ItemsCount INT NOT NULL,
    IsSynced BIT NOT NULL,
    LastSyncTime DATETIME2 NULL,
    ErrorMessage NVARCHAR(MAX) NULL,
    ErrorType INT NULL,
    SyncHistoryCount INT NOT NULL,
    CreationTime DATETIME2 NOT NULL,
    CreatorId UNIQUEIDENTIFIER NULL,
    LastModificationTime DATETIME2 NULL,
    LastModifierId UNIQUEIDENTIFIER NULL
);

CREATE TABLE IssuedInvoiceSyncData (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    InvoiceId NVARCHAR(50) NOT NULL,
    IsSuccess BIT NOT NULL,
    SyncTime DATETIME2 NOT NULL,
    ErrorMessage NVARCHAR(MAX) NULL,
    ErrorType INT NULL,
    Data NVARCHAR(MAX) NULL,
    FOREIGN KEY (InvoiceId) REFERENCES IssuedInvoices(Id)
);

CREATE INDEX IX_IssuedInvoices_InvoiceDate ON IssuedInvoices(InvoiceDate);
CREATE INDEX IX_IssuedInvoices_IsSynced ON IssuedInvoices(IsSynced);
CREATE INDEX IX_IssuedInvoices_ErrorType ON IssuedInvoices(ErrorType);
```

### Caching Strategy
- **Transformation Cache**: Cache product mappings and business rules (TTL: 1 hour)
- **Query Cache**: Cache frequently accessed invoice lists (TTL: 5 minutes)
- **Configuration Cache**: Cache source settings and credentials (TTL: 30 minutes)

## Integration Requirements

### External Systems
- **Shoptet E-commerce**: Playwright automation, XML parsing, file management
- **FlexiBee ERP**: REST API integration, error handling, batch operations
- **Dropbox**: File monitoring, download/upload, folder management

### Job Scheduling
- **Hangfire**: Reliable background job processing with retry policies
- **Daily Schedule**: 2:00 AM daily import, 3:00 AM error reporting
- **Manual Jobs**: On-demand single invoice import with immediate feedback

### Monitoring and Alerting
- **Application Insights**: Performance monitoring, error tracking
- **Email Notifications**: Daily import summaries, critical error alerts
- **Dashboard Metrics**: Success rates, processing times, error trends

## Security Considerations

### Data Protection
- **Encryption**: Customer data encrypted in transit and at rest
- **Access Control**: Role-based permissions for import operations
- **Audit Trail**: Complete history of all synchronization attempts

### Credential Management
- **Azure Key Vault**: Secure storage of API keys and passwords
- **Rotation Policy**: Regular credential updates with zero downtime
- **Least Privilege**: Minimal permissions for service accounts

### Compliance
- **GDPR**: Customer data anonymization options
- **Financial Regulations**: Immutable audit trails, data retention policies
- **Tax Compliance**: VAT calculation accuracy, invoice numbering integrity

## Performance Requirements
- Handle 1000+ invoices per daily import
- Complete standard import within 10 minutes
- Support concurrent manual imports
- Maintain sub-second response times for queries
- Linear scaling with invoice volume
- 99.9% uptime for scheduled imports