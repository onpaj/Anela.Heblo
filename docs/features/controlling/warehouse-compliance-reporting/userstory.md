# Warehouse Compliance Reporting User Story

## Feature Overview
The Warehouse Compliance Reporting feature provides comprehensive business intelligence and compliance monitoring through automated inventory validation reports. It ensures products are stored in appropriate warehouses based on their type classification, validates warehouse inventory integrity, and monitors business rule compliance. The system implements a pluggable reporting framework that integrates with FlexiBee ERP to provide real-time compliance validation and operational insights.

## Business Requirements

### Primary Use Case
As a warehouse manager, I want to automatically validate that products are stored in the correct warehouses according to their type classification so that I can ensure inventory integrity, prevent fulfillment errors, maintain manufacturing planning accuracy, and identify process violations that could impact operational efficiency and business compliance.

### Acceptance Criteria
1. The system shall validate material warehouse contains only Material type products
2. The system shall validate product warehouse contains only Product and Goods type products  
3. The system shall validate semi-product warehouse contains only SemiProduct type products
4. The system shall generate compliance reports showing any misplaced products with quantities
5. The system shall provide cached results for performance optimization
6. The system shall integrate with FlexiBee ERP for real-time stock data
7. The system shall support pluggable report architecture for extensibility
8. The system shall provide role-based access control for report generation
9. The system shall execute background jobs for automated compliance monitoring
10. The system shall provide success/failure indicators with detailed violation messages

## Technical Contracts

### Domain Model

```csharp
// Core reporting framework interface
public interface IReport
{
    string Name { get; }
    Task<ReportResult> GenerateAsync();
}

// Report execution result with factory methods
public class ReportResult
{
    public string Message { get; set; }
    public bool IsSuccess { get; set; }
    public string Report { get; set; }
    
    public static ReportResult Success(IReport report)
    {
        return new ReportResult
        {
            IsSuccess = true,
            Report = report.Name,
            Message = null
        };
    }
    
    public static ReportResult Fail(IReport report, string message)
    {
        return new ReportResult
        {
            IsSuccess = false,
            Report = report.Name,
            Message = message
        };
    }
    
    public SyncSeverity Severity => IsSuccess ? SyncSeverity.Green : SyncSeverity.Red;
}

// Base class for warehouse validation reports
public abstract class StockTypeInInvalidWarehouseReport : IReport
{
    private readonly IStockToDateClient _stockToDateClient;
    private readonly Warehouses _warehouse;
    private readonly ProductType[] _allowedProductTypes;
    
    protected StockTypeInInvalidWarehouseReport(
        IStockToDateClient stockToDateClient, 
        Warehouses warehouse,
        ProductType[] allowedProductTypes)
    {
        _stockToDateClient = stockToDateClient;
        _warehouse = warehouse;
        _allowedProductTypes = allowedProductTypes;
    }
    
    public string Name => GetType().Name;
    
    public async Task<ReportResult> GenerateAsync()
    {
        var stockToDate = await _stockToDateClient.GetAsync(
            DateTime.Now.Date, 
            warehouseId: (int)_warehouse);
            
        var invalidProducts = stockToDate
            .Where(stock => stock.ProductTypeId.HasValue && 
                           !_allowedProductTypes.Contains((ProductType)stock.ProductTypeId.Value) && 
                           stock.OnStock > 0)
            .ToList();
            
        if (invalidProducts.Any())
        {
            var violationDetails = string.Join(", ", 
                invalidProducts.Select(p => $"{p.ProductCode} ({p.OnStock}ks)"));
            return ReportResult.Fail(this, violationDetails);
        }
        
        return ReportResult.Success(this);
    }
}

// Material warehouse compliance report
public class MaterialWarehouseInvalidProductsReport : StockTypeInInvalidWarehouseReport
{
    public MaterialWarehouseInvalidProductsReport(IStockToDateClient stockToDateClient)
        : base(stockToDateClient, Warehouses.Material, new[] { ProductType.Material })
    {
    }
}

// Product warehouse compliance report
public class ProductsProductWarehouseInvalidProductsReport : StockTypeInInvalidWarehouseReport
{
    public ProductsProductWarehouseInvalidProductsReport(IStockToDateClient stockToDateClient)
        : base(stockToDateClient, Warehouses.Product, new[] { ProductType.Product, ProductType.Goods })
    {
    }
}

// Semi-product warehouse compliance report
public class SemiProductsWarehouseInvalidProductsReport : StockTypeInInvalidWarehouseReport
{
    public SemiProductsWarehouseInvalidProductsReport(IStockToDateClient stockToDateClient)
        : base(stockToDateClient, Warehouses.SemiProduct, new[] { ProductType.SemiProduct })
    {
    }
}

// Warehouse and product type enumerations
public enum Warehouses
{
    UNDEFINED = 0,
    Product = 4,
    Material = 5,
    SemiProduct = 20
}

public enum ProductType
{
    UNDEFINED = 0,
    Goods = 1,
    Material = 3,
    SemiProduct = 7,
    Product = 8
}

// Compliance validation aggregate
public class ComplianceValidation : AuditedAggregateRoot<int>
{
    public DateTime ValidationDate { get; set; }
    public string ReportName { get; set; }
    public bool IsCompliant { get; set; }
    public string? ViolationDetails { get; set; }
    public int ViolationCount { get; set; }
    public Warehouses WarehouseId { get; set; }
    public TimeSpan ExecutionDuration { get; set; }
    
    protected ComplianceValidation() { }
    
    public static ComplianceValidation Create(
        string reportName, 
        Warehouses warehouseId, 
        ReportResult result, 
        TimeSpan executionDuration)
    {
        var validation = new ComplianceValidation
        {
            ValidationDate = DateTime.UtcNow,
            ReportName = reportName,
            WarehouseId = warehouseId,
            IsCompliant = result.IsSuccess,
            ViolationDetails = result.Message,
            ExecutionDuration = executionDuration
        };
        
        // Count violations by parsing violation details
        if (!result.IsSuccess && !string.IsNullOrEmpty(result.Message))
        {
            validation.ViolationCount = result.Message.Split(',').Length;
        }
        
        return validation;
    }
    
    public bool HasCriticalViolations => !IsCompliant && ViolationCount > 10;
    public bool IsHistoricalCompliance => ValidationDate < DateTime.UtcNow.AddDays(-30);
    
    public void UpdateViolationStatus(bool isCompliant, string? violationDetails = null)
    {
        IsCompliant = isCompliant;
        ViolationDetails = violationDetails;
        ViolationCount = !isCompliant && !string.IsNullOrEmpty(violationDetails) 
            ? violationDetails.Split(',').Length 
            : 0;
    }
}

// Warehouse compliance statistics
public class WarehouseComplianceStatistics
{
    public Warehouses WarehouseId { get; set; }
    public string WarehouseName { get; set; }
    public int TotalValidations { get; set; }
    public int CompliantValidations { get; set; }
    public int ViolationValidations { get; set; }
    public double ComplianceRate { get; set; }
    public int CurrentViolationCount { get; set; }
    public DateTime LastValidationDate { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public List<string> CommonViolations { get; set; } = new();
    
    public bool IsHealthy => ComplianceRate >= 95.0;
    public bool RequiresAttention => ComplianceRate < 80.0 || CurrentViolationCount > 5;
    public ComplianceHealthStatus HealthStatus => GetHealthStatus();
    
    private ComplianceHealthStatus GetHealthStatus()
    {
        if (ComplianceRate >= 95.0 && CurrentViolationCount == 0) return ComplianceHealthStatus.Excellent;
        if (ComplianceRate >= 90.0 && CurrentViolationCount <= 2) return ComplianceHealthStatus.Good;
        if (ComplianceRate >= 80.0 && CurrentViolationCount <= 5) return ComplianceHealthStatus.Fair;
        return ComplianceHealthStatus.Poor;
    }
}

public enum ComplianceHealthStatus
{
    Excellent = 1,
    Good = 2,
    Fair = 3,
    Poor = 4
}
```

### Application Layer Contracts

```csharp
// Primary application service interface
public interface IControllingAppService : IApplicationService
{
    Task<List<ReportResultDto>> GenerateReportsAsync();
    Task<List<ReportResultDto>> GetAsync();
    Task<List<ReportResultDto>> GetReportHistoryAsync(DateTime? fromDate = null, DateTime? toDate = null);
    Task<WarehouseComplianceStatisticsDto> GetComplianceStatisticsAsync(Warehouses? warehouseId = null);
    Task<List<ComplianceValidationDto>> GetViolationsAsync(Warehouses? warehouseId = null, int maxResults = 100);
    Task<ComplianceValidationDto> GetLatestValidationAsync(Warehouses warehouseId);
    Task<bool> ValidateWarehouseComplianceAsync(Warehouses warehouseId);
    Task<List<ComplianceTrendDto>> GetComplianceTrendsAsync(DateTime fromDate, DateTime toDate);
}

// Repository interfaces for data access
public interface IComplianceValidationRepository : IRepository<ComplianceValidation, int>
{
    Task<List<ComplianceValidation>> GetValidationHistoryAsync(
        DateTime fromDate, 
        DateTime toDate, 
        Warehouses? warehouseId = null,
        CancellationToken cancellationToken = default);
    
    Task<List<ComplianceValidation>> GetActiveViolationsAsync(
        Warehouses? warehouseId = null,
        CancellationToken cancellationToken = default);
    
    Task<ComplianceValidation?> GetLatestValidationAsync(
        Warehouses warehouseId,
        CancellationToken cancellationToken = default);
    
    Task<List<WarehouseComplianceStatistics>> GetComplianceStatisticsAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);
    
    Task<int> CleanupOldValidationsAsync(
        DateTime olderThan,
        CancellationToken cancellationToken = default);
}

// Background job service for automated monitoring
public interface IComplianceMonitoringService : ITransientDependency
{
    Task ExecuteComplianceCheckAsync();
    Task ProcessComplianceViolationsAsync();
    Task GenerateComplianceAlertsAsync();
    Task CleanupOldComplianceDataAsync();
}

// DTOs for API contracts
public class ReportResultDto
{
    public string Message { get; set; }
    public bool IsSuccess { get; set; }
    public string Report { get; set; }
    public SyncSeverity Severity { get; set; }
    public DateTime GeneratedAt { get; set; }
    public TimeSpan? ExecutionDuration { get; set; }
}

public class ComplianceValidationDto : AuditedEntityDto<int>
{
    public DateTime ValidationDate { get; set; }
    public string ReportName { get; set; }
    public bool IsCompliant { get; set; }
    public string? ViolationDetails { get; set; }
    public int ViolationCount { get; set; }
    public Warehouses WarehouseId { get; set; }
    public string WarehouseName { get; set; }
    public TimeSpan ExecutionDuration { get; set; }
    public bool HasCriticalViolations { get; set; }
}

public class WarehouseComplianceStatisticsDto
{
    public Warehouses WarehouseId { get; set; }
    public string WarehouseName { get; set; }
    public int TotalValidations { get; set; }
    public int CompliantValidations { get; set; }
    public int ViolationValidations { get; set; }
    public double ComplianceRate { get; set; }
    public int CurrentViolationCount { get; set; }
    public DateTime LastValidationDate { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public List<string> CommonViolations { get; set; } = new();
    public ComplianceHealthStatus HealthStatus { get; set; }
    public bool RequiresAttention { get; set; }
}

public class ComplianceTrendDto
{
    public DateTime Date { get; set; }
    public Warehouses WarehouseId { get; set; }
    public double ComplianceRate { get; set; }
    public int ViolationCount { get; set; }
    public bool IsCompliant { get; set; }
}

public class GenerateReportsRequestDto
{
    public bool ForceRegeneration { get; set; } = false;
    public List<Warehouses>? SpecificWarehouses { get; set; }
    public bool IncludeDetailedViolations { get; set; } = true;
}

public enum SyncSeverity
{
    Green = 1,
    Yellow = 2,
    Red = 3
}
```

## Implementation Details

### Enhanced Application Service Implementation

```csharp
[Authorize]
public class ControllingAppService : ApplicationService, IControllingAppService
{
    private readonly IEnumerable<IReport> _reports;
    private readonly IComplianceValidationRepository _validationRepository;
    private readonly ILogger<ControllingAppService> _logger;
    private readonly IClock _clock;
    
    private IEnumerable<ReportResult> _cachedResults = new List<ReportResult>();

    public ControllingAppService(
        IEnumerable<IReport> reports,
        IComplianceValidationRepository validationRepository,
        ILogger<ControllingAppService> logger,
        IClock clock)
    {
        _reports = reports;
        _validationRepository = validationRepository;
        _logger = logger;
        _clock = clock;
    }

    [RemoteService(IsEnabled = false)]
    [AllowAnonymous]
    public async Task<List<ReportResultDto>> GenerateReportsAsync()
    {
        _logger.LogInformation("Starting compliance report generation with {ReportCount} reports", _reports.Count());
        
        var results = new List<ReportResult>();
        var validations = new List<ComplianceValidation>();
        
        foreach (var report in _reports)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                _logger.LogDebug("Executing report: {ReportName}", report.Name);
                
                var result = await report.GenerateAsync();
                results.Add(result);
                
                stopwatch.Stop();
                
                // Create compliance validation record
                var warehouseId = DetermineWarehouseFromReportName(report.Name);
                var validation = ComplianceValidation.Create(
                    report.Name, 
                    warehouseId, 
                    result, 
                    stopwatch.Elapsed);
                
                validations.Add(validation);
                
                _logger.LogInformation("Report {ReportName} completed: {IsSuccess} (Duration: {Duration}ms)", 
                    report.Name, result.IsSuccess, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(ex, "Error executing report {ReportName}", report.Name);
                
                var failureResult = ReportResult.Fail(report, $"Execution error: {ex.Message}");
                results.Add(failureResult);
                
                var warehouseId = DetermineWarehouseFromReportName(report.Name);
                var validation = ComplianceValidation.Create(
                    report.Name, 
                    warehouseId, 
                    failureResult, 
                    stopwatch.Elapsed);
                
                validations.Add(validation);
            }
        }

        // Save validation records
        foreach (var validation in validations)
        {
            await _validationRepository.InsertAsync(validation);
        }

        _cachedResults = results;
        
        _logger.LogInformation("Compliance report generation completed. Success: {SuccessCount}, Failures: {FailureCount}",
            results.Count(r => r.IsSuccess),
            results.Count(r => !r.IsSuccess));

        return results.Select(r => ObjectMapper.Map<ReportResult, ReportResultDto>(r)).ToList();
    }

    public async Task<List<ReportResultDto>> GetAsync()
    {
        if (!_cachedResults.Any())
        {
            _logger.LogDebug("No cached results available, generating new reports");
            return await GenerateReportsAsync();
        }

        _logger.LogDebug("Returning cached compliance results");
        return _cachedResults.Select(r => ObjectMapper.Map<ReportResult, ReportResultDto>(r)).ToList();
    }

    public async Task<List<ReportResultDto>> GetReportHistoryAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var from = fromDate ?? _clock.Now.AddDays(-30);
        var to = toDate ?? _clock.Now;
        
        var validations = await _validationRepository.GetValidationHistoryAsync(from, to);
        
        var results = validations.Select(v => new ReportResult
        {
            IsSuccess = v.IsCompliant,
            Message = v.ViolationDetails,
            Report = v.ReportName
        }).ToList();
        
        return results.Select(r => ObjectMapper.Map<ReportResult, ReportResultDto>(r)).ToList();
    }

    public async Task<WarehouseComplianceStatisticsDto> GetComplianceStatisticsAsync(Warehouses? warehouseId = null)
    {
        var fromDate = _clock.Now.AddDays(-90); // 90-day analysis window
        var toDate = _clock.Now;
        
        var statistics = await _validationRepository.GetComplianceStatisticsAsync(fromDate, toDate);
        
        if (warehouseId.HasValue)
        {
            var warehouseStats = statistics.FirstOrDefault(s => s.WarehouseId == warehouseId.Value);
            return warehouseStats != null 
                ? ObjectMapper.Map<WarehouseComplianceStatistics, WarehouseComplianceStatisticsDto>(warehouseStats)
                : new WarehouseComplianceStatisticsDto { WarehouseId = warehouseId.Value };
        }

        // Return aggregated statistics for all warehouses
        var aggregated = new WarehouseComplianceStatistics
        {
            WarehouseId = Warehouses.UNDEFINED,
            WarehouseName = "All Warehouses",
            TotalValidations = statistics.Sum(s => s.TotalValidations),
            CompliantValidations = statistics.Sum(s => s.CompliantValidations),
            ViolationValidations = statistics.Sum(s => s.ViolationValidations),
            CurrentViolationCount = statistics.Sum(s => s.CurrentViolationCount),
            LastValidationDate = statistics.Max(s => s.LastValidationDate),
            AverageExecutionTime = TimeSpan.FromMilliseconds(statistics.Average(s => s.AverageExecutionTime.TotalMilliseconds))
        };

        aggregated.ComplianceRate = aggregated.TotalValidations > 0 
            ? (double)aggregated.CompliantValidations / aggregated.TotalValidations * 100 
            : 0;

        return ObjectMapper.Map<WarehouseComplianceStatistics, WarehouseComplianceStatisticsDto>(aggregated);
    }

    public async Task<List<ComplianceValidationDto>> GetViolationsAsync(Warehouses? warehouseId = null, int maxResults = 100)
    {
        var violations = await _validationRepository.GetActiveViolationsAsync(warehouseId);
        
        return violations
            .Take(maxResults)
            .Select(v => ObjectMapper.Map<ComplianceValidation, ComplianceValidationDto>(v))
            .ToList();
    }

    public async Task<ComplianceValidationDto> GetLatestValidationAsync(Warehouses warehouseId)
    {
        var validation = await _validationRepository.GetLatestValidationAsync(warehouseId);
        
        return validation != null 
            ? ObjectMapper.Map<ComplianceValidation, ComplianceValidationDto>(validation)
            : new ComplianceValidationDto { WarehouseId = warehouseId };
    }

    public async Task<bool> ValidateWarehouseComplianceAsync(Warehouses warehouseId)
    {
        var relevantReport = _reports.FirstOrDefault(r => 
            DetermineWarehouseFromReportName(r.Name) == warehouseId);
        
        if (relevantReport == null)
        {
            _logger.LogWarning("No compliance report found for warehouse {WarehouseId}", warehouseId);
            return false;
        }

        try
        {
            var result = await relevantReport.GenerateAsync();
            
            // Save validation result
            var validation = ComplianceValidation.Create(
                relevantReport.Name, 
                warehouseId, 
                result, 
                TimeSpan.Zero);
            
            await _validationRepository.InsertAsync(validation);
            
            return result.IsSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating compliance for warehouse {WarehouseId}", warehouseId);
            return false;
        }
    }

    public async Task<List<ComplianceTrendDto>> GetComplianceTrendsAsync(DateTime fromDate, DateTime toDate)
    {
        var validations = await _validationRepository.GetValidationHistoryAsync(fromDate, toDate);
        
        var trends = validations
            .GroupBy(v => new { Date = v.ValidationDate.Date, v.WarehouseId })
            .Select(g => new ComplianceTrendDto
            {
                Date = g.Key.Date,
                WarehouseId = g.Key.WarehouseId,
                ComplianceRate = g.Average(v => v.IsCompliant ? 100.0 : 0.0),
                ViolationCount = g.Sum(v => v.ViolationCount),
                IsCompliant = g.All(v => v.IsCompliant)
            })
            .OrderBy(t => t.Date)
            .ThenBy(t => t.WarehouseId)
            .ToList();
        
        return trends;
    }

    private Warehouses DetermineWarehouseFromReportName(string reportName)
    {
        return reportName switch
        {
            nameof(MaterialWarehouseInvalidProductsReport) => Warehouses.Material,
            nameof(ProductsProductWarehouseInvalidProductsReport) => Warehouses.Product,
            nameof(SemiProductsWarehouseInvalidProductsReport) => Warehouses.SemiProduct,
            _ => Warehouses.UNDEFINED
        };
    }
}
```

### Repository Implementation

```csharp
public class ComplianceValidationRepository : EfCoreRepository<HebloDbContext, ComplianceValidation, int>, 
    IComplianceValidationRepository
{
    public ComplianceValidationRepository(IDbContextProvider<HebloDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<List<ComplianceValidation>> GetValidationHistoryAsync(
        DateTime fromDate, 
        DateTime toDate, 
        Warehouses? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var query = dbContext.Set<ComplianceValidation>()
            .Where(v => v.ValidationDate >= fromDate && v.ValidationDate <= toDate);

        if (warehouseId.HasValue)
        {
            query = query.Where(v => v.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderByDescending(v => v.ValidationDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ComplianceValidation>> GetActiveViolationsAsync(
        Warehouses? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var query = dbContext.Set<ComplianceValidation>()
            .Where(v => !v.IsCompliant);

        if (warehouseId.HasValue)
        {
            query = query.Where(v => v.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderByDescending(v => v.ValidationDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<ComplianceValidation?> GetLatestValidationAsync(
        Warehouses warehouseId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        
        return await dbContext.Set<ComplianceValidation>()
            .Where(v => v.WarehouseId == warehouseId)
            .OrderByDescending(v => v.ValidationDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<WarehouseComplianceStatistics>> GetComplianceStatisticsAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        
        var statistics = await dbContext.Set<ComplianceValidation>()
            .Where(v => v.ValidationDate >= fromDate && v.ValidationDate <= toDate)
            .GroupBy(v => v.WarehouseId)
            .Select(g => new WarehouseComplianceStatistics
            {
                WarehouseId = g.Key,
                TotalValidations = g.Count(),
                CompliantValidations = g.Count(v => v.IsCompliant),
                ViolationValidations = g.Count(v => !v.IsCompliant),
                CurrentViolationCount = g.Where(v => !v.IsCompliant).Sum(v => v.ViolationCount),
                LastValidationDate = g.Max(v => v.ValidationDate),
                AverageExecutionTime = TimeSpan.FromMilliseconds(g.Average(v => v.ExecutionDuration.TotalMilliseconds))
            })
            .ToListAsync(cancellationToken);

        // Calculate compliance rates
        foreach (var stat in statistics)
        {
            stat.ComplianceRate = stat.TotalValidations > 0 
                ? (double)stat.CompliantValidations / stat.TotalValidations * 100 
                : 0;
            
            stat.WarehouseName = stat.WarehouseId.ToString();
        }

        return statistics;
    }

    public async Task<int> CleanupOldValidationsAsync(
        DateTime olderThan,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        
        var oldValidations = await dbContext.Set<ComplianceValidation>()
            .Where(v => v.ValidationDate < olderThan)
            .ToListAsync(cancellationToken);

        dbContext.Set<ComplianceValidation>().RemoveRange(oldValidations);
        await dbContext.SaveChangesAsync(cancellationToken);

        return oldValidations.Count;
    }
}
```

### Background Compliance Monitoring Service

```csharp
public class ComplianceMonitoringService : IComplianceMonitoringService
{
    private readonly IControllingAppService _controllingService;
    private readonly IComplianceValidationRepository _validationRepository;
    private readonly ILogger<ComplianceMonitoringService> _logger;
    private readonly IClock _clock;

    public ComplianceMonitoringService(
        IControllingAppService controllingService,
        IComplianceValidationRepository validationRepository,
        ILogger<ComplianceMonitoringService> logger,
        IClock clock)
    {
        _controllingService = controllingService;
        _validationRepository = validationRepository;
        _logger = logger;
        _clock = clock;
    }

    public async Task ExecuteComplianceCheckAsync()
    {
        _logger.LogInformation("Starting automated compliance check");

        try
        {
            var results = await _controllingService.GenerateReportsAsync();
            
            var violations = results.Count(r => !r.IsSuccess);
            var successes = results.Count(r => r.IsSuccess);
            
            _logger.LogInformation("Compliance check completed. Violations: {Violations}, Compliant: {Successes}", 
                violations, successes);

            if (violations > 0)
            {
                await ProcessComplianceViolationsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during automated compliance check");
            throw;
        }
    }

    public async Task ProcessComplianceViolationsAsync()
    {
        _logger.LogInformation("Processing compliance violations");

        var violations = await _validationRepository.GetActiveViolationsAsync();
        
        var criticalViolations = violations.Where(v => v.HasCriticalViolations).ToList();
        
        if (criticalViolations.Any())
        {
            _logger.LogWarning("Found {Count} critical compliance violations requiring immediate attention", 
                criticalViolations.Count);
            
            await GenerateComplianceAlertsAsync();
        }
    }

    public async Task GenerateComplianceAlertsAsync()
    {
        _logger.LogInformation("Generating compliance alerts for critical violations");
        
        // Implementation would include:
        // - Email notifications to warehouse managers
        // - Dashboard alerts
        // - Integration with external monitoring systems
        // - Escalation procedures for repeated violations
        
        await Task.CompletedTask; // Placeholder for alert generation
    }

    public async Task CleanupOldComplianceDataAsync()
    {
        _logger.LogInformation("Starting cleanup of old compliance data");

        var cutoffDate = _clock.Now.AddDays(-90); // Keep 90 days of history
        
        try
        {
            var deletedCount = await _validationRepository.CleanupOldValidationsAsync(cutoffDate);
            
            _logger.LogInformation("Cleaned up {Count} old compliance validation records", deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during compliance data cleanup");
            throw;
        }
    }
}
```

## Happy Day Scenario

1. **Automated Execution**: Background job triggers compliance validation at scheduled intervals
2. **Report Generation**: System executes all registered warehouse compliance reports
3. **Data Retrieval**: Each report queries FlexiBee ERP for current stock levels in specific warehouses
4. **Type Validation**: System validates product types match warehouse classifications
5. **Compliance Check**: Products are verified against allowed types for each warehouse
6. **Result Processing**: Success/failure results are generated with detailed violation information
7. **Data Persistence**: Validation results are stored for historical tracking and analysis
8. **Cache Update**: Results are cached for immediate dashboard and API access
9. **Alert Generation**: Critical violations trigger automated notifications
10. **Dashboard Update**: Real-time compliance status is updated for management visibility

## Error Handling

### Data Integration Errors
- **ERP Connectivity Issues**: Implement retry mechanisms with exponential backoff
- **Stock Data Unavailability**: Graceful degradation with cached data when possible
- **Authentication Failures**: Secure credential refresh and error notifications
- **Network Timeouts**: Robust timeout handling with appropriate error messaging

### Business Logic Errors
- **Invalid Product Types**: Handle unknown or null product type classifications
- **Warehouse Configuration**: Validate warehouse IDs exist in system configuration
- **Data Consistency**: Detect and report inconsistencies between systems
- **Report Execution Failures**: Comprehensive exception handling with detailed logging

### Performance Errors
- **Large Dataset Handling**: Implement pagination and streaming for large stock queries
- **Memory Management**: Proper disposal of resources and memory optimization
- **Concurrent Access**: Thread-safe operations and database connection management
- **Timeout Management**: Configurable timeouts for long-running operations

### System Errors
- **Database Connectivity**: Connection pooling and retry logic for database operations
- **Cache Failures**: Fallback mechanisms when caching systems are unavailable
- **Background Job Failures**: Dead letter queues and manual intervention capabilities
- **Resource Exhaustion**: Monitoring and alerting for system resource constraints

## Business Rules

### Warehouse Classification Rules
1. **Material Warehouse (ID: 5)**: Only ProductType.Material (3) allowed
2. **Product Warehouse (ID: 4)**: Only ProductType.Product (8) and ProductType.Goods (1) allowed
3. **SemiProduct Warehouse (ID: 20)**: Only ProductType.SemiProduct (7) allowed
4. **Stock Validation**: Only products with OnStock > 0 are validated
5. **Type Enforcement**: Product type must match warehouse classification exactly

### Compliance Validation Rules
1. **Real-time Validation**: Stock data retrieved from current date
2. **Zero-tolerance Policy**: Any misplaced product constitutes a violation
3. **Quantity Reporting**: Violation details include product codes and quantities
4. **Historical Tracking**: All validation results stored for trend analysis
5. **Alert Thresholds**: Critical violations trigger immediate notifications

### Performance Requirements
- Execute complete compliance validation within 5 minutes
- Support concurrent access from multiple dashboard users
- Cache results for 1 hour to optimize performance
- Handle 10,000+ stock items per warehouse validation
- Provide sub-second response for cached compliance status
- Maintain 99.9% uptime for compliance monitoring services