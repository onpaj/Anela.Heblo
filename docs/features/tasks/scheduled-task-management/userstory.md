# Scheduled Task Management User Story

## Feature Overview
The Scheduled Task Management feature provides a comprehensive database-backed task queue system for immediate execution operations. It implements pessimistic locking mechanisms to prevent concurrent task execution, supports external system integration through function key authentication, and provides complete audit trails for task lifecycle management. This system complements the recurring job infrastructure by handling ad-hoc and on-demand tasks.

## Business Requirements

### Primary Use Case
As a system administrator, I want to manage scheduled tasks that can be executed immediately by external systems or internal processes so that critical business operations can be triggered on-demand, tracked comprehensively, and executed safely without conflicts, ensuring reliable operation automation and external system integration.

### Acceptance Criteria
1. The system shall create scheduled tasks with typed categorization and JSON data payloads
2. The system shall implement pessimistic locking with 10-minute timeout to prevent concurrent execution
3. The system shall provide function key authentication for external system access
4. The system shall track complete task lifecycle from creation to completion
5. The system shall support task retrieval with automatic locking for safe execution
6. The system shall validate task completion and prevent double completion
7. The system shall store execution results for audit and debugging purposes
8. The system shall provide comprehensive task management CRUD operations
9. The system shall support multiple task types through extensible TaskType system
10. The system shall maintain audit trails with user attribution for all operations

## Technical Contracts

### Domain Model

```csharp
// Primary aggregate for scheduled task management
public class ScheduledTask : AuditedAggregateRoot<int>
{
    // Task identification and categorization
    public string? TaskType { get; set; }
    public string Data { get; set; } = "{}";
    
    // Task execution state
    public DateTime? CompletedDate { get; set; }
    public string? CompletedBy { get; set; }
    public string? Result { get; set; }
    
    // Concurrency control
    public DateTime? LockDate { get; set; } = null;
    
    // Computed properties for task state
    public bool IsCompleted => CompletedDate.HasValue;
    public bool IsLocked => LockDate.HasValue;
    public bool IsPending => !IsCompleted && !IsLocked;
    public bool IsExpiredLock(DateTime currentTime, TimeSpan lockTimeout) => 
        IsLocked && LockDate!.Value.Add(lockTimeout) < currentTime;
    
    // Business methods for task management
    public static ScheduledTask Create(string taskType, object? data = null)
    {
        if (string.IsNullOrEmpty(taskType))
            throw new BusinessException("Task type is required");
        
        return new ScheduledTask
        {
            TaskType = taskType,
            Data = data != null ? JsonSerializer.Serialize(data) : "{}"
        };
    }
    
    public void Lock(DateTime lockTime)
    {
        if (IsCompleted)
            throw new BusinessException("Cannot lock completed task");
        
        if (IsLocked)
            throw new BusinessException("Task is already locked");
        
        LockDate = lockTime;
    }
    
    public void Complete(string completedBy, object? result = null)
    {
        if (IsCompleted)
            throw new BusinessException("Task is already completed");
        
        if (string.IsNullOrEmpty(completedBy))
            throw new BusinessException("CompletedBy is required");
        
        CompletedDate = DateTime.UtcNow;
        CompletedBy = completedBy;
        Result = result != null ? JsonSerializer.Serialize(result) : null;
        LockDate = null; // Clear lock on completion
    }
    
    public void ReleaseLock()
    {
        if (!IsLocked)
            throw new BusinessException("Task is not locked");
        
        LockDate = null;
    }
    
    public T? GetData<T>() where T : class
    {
        if (string.IsNullOrEmpty(Data) || Data == "{}")
            return null;
        
        try
        {
            return JsonSerializer.Deserialize<T>(Data);
        }
        catch (JsonException ex)
        {
            throw new BusinessException($"Failed to deserialize task data: {ex.Message}", ex);
        }
    }
    
    public T? GetResult<T>() where T : class
    {
        if (string.IsNullOrEmpty(Result))
            return null;
        
        try
        {
            return JsonSerializer.Deserialize<T>(Result);
        }
        catch (JsonException ex)
        {
            throw new BusinessException($"Failed to deserialize task result: {ex.Message}", ex);
        }
    }
    
    public void UpdateData(object data)
    {
        if (IsCompleted)
            throw new BusinessException("Cannot update data for completed task");
        
        if (IsLocked)
            throw new BusinessException("Cannot update data for locked task");
        
        Data = JsonSerializer.Serialize(data);
    }
}

// Task type enumeration for type safety
public enum TaskTypes
{
    EshopExport = 1,
    DataImport = 2,
    ReportGeneration = 3,
    SystemMaintenance = 4,
    CustomOperation = 5
}

// Task execution context for external systems
public class TaskExecutionContext
{
    public int TaskId { get; set; }
    public string TaskType { get; set; }
    public string Data { get; set; }
    public DateTime LockedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string ExecutorIdentity { get; set; }
    
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    public TimeSpan RemainingTime => ExpiresAt - DateTime.UtcNow;
    
    public static TaskExecutionContext Create(ScheduledTask task, string executorIdentity, TimeSpan lockTimeout)
    {
        if (!task.IsLocked)
            throw new BusinessException("Task must be locked to create execution context");
        
        return new TaskExecutionContext
        {
            TaskId = task.Id,
            TaskType = task.TaskType!,
            Data = task.Data,
            LockedAt = task.LockDate!.Value,
            ExpiresAt = task.LockDate.Value.Add(lockTimeout),
            ExecutorIdentity = executorIdentity
        };
    }
}

// Task completion result for structured responses
public class TaskCompletionResult
{
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public object? ResultData { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public string? CompletedBy { get; set; }
    public TimeSpan ExecutionDuration { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    public static TaskCompletionResult Success(object? resultData = null, string? completedBy = null)
    {
        return new TaskCompletionResult
        {
            IsSuccessful = true,
            ResultData = resultData,
            CompletedBy = completedBy
        };
    }
    
    public static TaskCompletionResult Failure(string errorMessage, string? completedBy = null)
    {
        return new TaskCompletionResult
        {
            IsSuccessful = false,
            ErrorMessage = errorMessage,
            CompletedBy = completedBy
        };
    }
}
```

### Application Layer Contracts

```csharp
// Primary application service interface
public interface ITaskAppService : ICrudAppService<ScheduledTaskDto, int, PagedAndSortedResultRequestDto, CreateTaskDto>
{
    // External system integration
    Task<ScheduledTaskDto?> GetExportAsync(string functionKey, ExportTaskDto dto);
    Task<ScheduledTaskDto?> CreateCompleteAsync(string functionKey, int id, CompleteTaskDto dto);
    
    // Enhanced task management
    Task<TaskExecutionContextDto?> AcquireTaskAsync(string taskType, string executorIdentity);
    Task<ScheduledTaskDto> CompleteTaskAsync(int taskId, TaskCompletionResult result);
    Task<ScheduledTaskDto> ReleaseTaskAsync(int taskId, string reason);
    Task<List<ScheduledTaskDto>> GetTasksByTypeAsync(string taskType, TaskStatus? status = null);
    Task<List<ScheduledTaskDto>> GetExpiredLocksAsync();
    Task<int> CleanupExpiredLocksAsync();
    Task<TaskStatisticsDto> GetTaskStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null);
}

// Repository interface for data access
public interface IScheduledTaskRepository : IRepository<ScheduledTask, int>
{
    Task<ScheduledTask?> GetAvailableTaskAsync(string taskType, DateTime validLockDate, CancellationToken cancellationToken = default);
    Task<List<ScheduledTask>> GetTasksByTypeAsync(string taskType, TaskStatus? status = null, CancellationToken cancellationToken = default);
    Task<List<ScheduledTask>> GetExpiredLocksAsync(DateTime expiredBefore, CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredLocksAsync(DateTime expiredBefore, CancellationToken cancellationToken = default);
    Task<List<ScheduledTask>> GetCompletedTasksAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
}

// Authentication service for function keys
public interface IFunctionKeyService
{
    Task<bool> ValidateKeyAsync(string functionKey, string? scope = null);
    Task<string> GenerateKeyAsync(string name, string? scope = null, TimeSpan? expiration = null);
    Task<bool> RevokeKeyAsync(string functionKey);
    Task<List<FunctionKeyInfo>> GetActiveKeysAsync();
}

// DTOs for API contracts
public class ScheduledTaskDto : AuditedEntityDto<int>
{
    public string? TaskType { get; set; }
    public DateTime? CompletedDate { get; set; }
    public DateTime? LockDate { get; set; }
    public string Data { get; set; } = "{}";
    public string? CompletedBy { get; set; }
    public string? Result { get; set; }
    
    // Computed properties
    public bool IsCompleted => CompletedDate.HasValue;
    public bool IsLocked => LockDate.HasValue;
    public bool IsPending => !IsCompleted && !IsLocked;
    public TaskStatus Status => GetTaskStatus();
    
    private TaskStatus GetTaskStatus()
    {
        if (IsCompleted) return TaskStatus.Completed;
        if (IsLocked) return TaskStatus.Locked;
        return TaskStatus.Pending;
    }
}

public class CreateTaskDto
{
    [Required]
    public string TaskType { get; set; } = "";
    
    public object? Data { get; set; }
    
    [StringLength(500)]
    public string? Description { get; set; }
    
    public DateTime? ScheduledFor { get; set; }
    
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class ExportTaskDto
{
    public string? TaskType { get; set; }
    public string? ExecutorIdentity { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class CompleteTaskDto
{
    [Required]
    public string CompletedBy { get; set; } = "";
    
    public object? Result { get; set; }
    
    public bool IsSuccessful { get; set; } = true;
    
    public string? ErrorMessage { get; set; }
    
    public TimeSpan? ExecutionDuration { get; set; }
}

public class TaskExecutionContextDto
{
    public int TaskId { get; set; }
    public string TaskType { get; set; }
    public string Data { get; set; }
    public DateTime LockedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string ExecutorIdentity { get; set; }
    public bool IsExpired { get; set; }
    public TimeSpan RemainingTime { get; set; }
}

public class TaskStatisticsDto
{
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int PendingTasks { get; set; }
    public int LockedTasks { get; set; }
    public int ExpiredLocks { get; set; }
    public double AverageExecutionTime { get; set; }
    public Dictionary<string, int> TasksByType { get; set; } = new();
    public Dictionary<string, int> CompletionsByUser { get; set; } = new();
    public DateTime AnalyzedFrom { get; set; }
    public DateTime AnalyzedTo { get; set; }
}

public enum TaskStatus
{
    Pending = 0,
    Locked = 1,
    Completed = 2,
    Expired = 3,
    Failed = 4
}

public class FunctionKeyInfo
{
    public string Name { get; set; }
    public string Key { get; set; }
    public string? Scope { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public int UsageCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
```

## Implementation Details

### Enhanced Application Service Implementation

```csharp
[Authorize]
public class TaskAppService : CrudAppService<ScheduledTask, ScheduledTaskDto, int, PagedAndSortedResultRequestDto, CreateTaskDto>, 
    ITaskAppService
{
    private readonly IScheduledTaskRepository _repository;
    private readonly IFunctionKeyService _functionKeyService;
    private readonly IClock _clock;
    private readonly ILogger<TaskAppService> _logger;
    private readonly TimeSpan _lockTimeout = TimeSpan.FromMinutes(10);

    public TaskAppService(
        IScheduledTaskRepository repository,
        IFunctionKeyService functionKeyService,
        IClock clock,
        ILogger<TaskAppService> logger)
        : base(repository)
    {
        _repository = repository;
        _functionKeyService = functionKeyService;
        _clock = clock;
        _logger = logger;
    }

    [AllowAnonymous]
    public async Task<ScheduledTaskDto?> GetExportAsync(
        [FromHeader(Name = "x-function-key")] string functionKey, 
        ExportTaskDto dto)
    {
        _logger.LogDebug("Getting export task (Type: {TaskType}, Executor: {Executor})", 
            dto.TaskType, dto.ExecutorIdentity);
        
        // Validate function key
        if (!await _functionKeyService.ValidateKeyAsync(functionKey, "task-execution"))
        {
            throw new AbpAuthorizationException("Valid function key required for task access");
        }

        var validLockDate = _clock.Now.Subtract(_lockTimeout);
        var taskType = dto.TaskType ?? TaskTypes.EshopExport.ToString();

        var task = await _repository.GetAvailableTaskAsync(taskType, validLockDate);

        if (task != null)
        {
            try
            {
                task.Lock(_clock.Now);
                task = await _repository.UpdateAsync(task);
                
                _logger.LogInformation("Task {TaskId} ({TaskType}) locked for execution by {Executor}", 
                    task.Id, task.TaskType, dto.ExecutorIdentity);
                
                return ObjectMapper.Map<ScheduledTask, ScheduledTaskDto>(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to lock task {TaskId}", task.Id);
                throw;
            }
        }

        _logger.LogDebug("No available tasks found for type {TaskType}", taskType);
        return null;
    }

    [AllowAnonymous]
    public async Task<ScheduledTaskDto?> CreateCompleteAsync(
        [FromHeader(Name = "x-function-key")] string functionKey,
        [FromRoute] int id,
        [FromBody] CompleteTaskDto dto)
    {
        _logger.LogDebug("Completing task {TaskId} by {CompletedBy}", id, dto.CompletedBy);
        
        // Validate function key
        if (!await _functionKeyService.ValidateKeyAsync(functionKey, "task-execution"))
        {
            throw new AbpAuthorizationException("Valid function key required for task completion");
        }

        var task = await _repository.GetAsync(id);
        
        if (task.IsCompleted)
        {
            throw new AbpValidationException("Task is already completed");
        }

        try
        {
            var result = dto.IsSuccessful 
                ? TaskCompletionResult.Success(dto.Result, dto.CompletedBy)
                : TaskCompletionResult.Failure(dto.ErrorMessage ?? "Task failed", dto.CompletedBy);
            
            if (dto.ExecutionDuration.HasValue)
            {
                result.ExecutionDuration = dto.ExecutionDuration.Value;
            }

            task.Complete(dto.CompletedBy, result);
            task = await _repository.UpdateAsync(task);

            _logger.LogInformation("Task {TaskId} completed successfully by {CompletedBy} (Success: {Success})", 
                id, dto.CompletedBy, dto.IsSuccessful);

            return ObjectMapper.Map<ScheduledTask, ScheduledTaskDto>(task);
        }
        catch (BusinessException ex)
        {
            _logger.LogWarning("Failed to complete task {TaskId}: {Error}", id, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error completing task {TaskId}", id);
            throw;
        }
    }

    public async Task<TaskExecutionContextDto?> AcquireTaskAsync(string taskType, string executorIdentity)
    {
        Check.NotNullOrEmpty(taskType, nameof(taskType));
        Check.NotNullOrEmpty(executorIdentity, nameof(executorIdentity));
        
        _logger.LogDebug("Acquiring task (Type: {TaskType}, Executor: {Executor})", taskType, executorIdentity);
        
        var validLockDate = _clock.Now.Subtract(_lockTimeout);
        var task = await _repository.GetAvailableTaskAsync(taskType, validLockDate);

        if (task == null)
        {
            _logger.LogDebug("No available tasks found for type {TaskType}", taskType);
            return null;
        }

        try
        {
            task.Lock(_clock.Now);
            task = await _repository.UpdateAsync(task);
            
            var context = TaskExecutionContext.Create(task, executorIdentity, _lockTimeout);
            
            _logger.LogInformation("Task {TaskId} acquired by {Executor}, expires at {ExpiresAt}", 
                task.Id, executorIdentity, context.ExpiresAt);
            
            return ObjectMapper.Map<TaskExecutionContext, TaskExecutionContextDto>(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire task {TaskId}", task.Id);
            throw;
        }
    }

    public async Task<ScheduledTaskDto> CompleteTaskAsync(int taskId, TaskCompletionResult result)
    {
        _logger.LogDebug("Completing task {TaskId} (Success: {Success})", taskId, result.IsSuccessful);
        
        var task = await _repository.GetAsync(taskId);
        
        if (task.IsCompleted)
        {
            throw new BusinessException("Task is already completed");
        }

        if (!task.IsLocked)
        {
            throw new BusinessException("Task must be locked before completion");
        }

        try
        {
            task.Complete(result.CompletedBy ?? CurrentUser.UserName ?? "System", result);
            task = await _repository.UpdateAsync(task);

            _logger.LogInformation("Task {TaskId} completed (Success: {Success}, Duration: {Duration})", 
                taskId, result.IsSuccessful, result.ExecutionDuration);

            return ObjectMapper.Map<ScheduledTask, ScheduledTaskDto>(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete task {TaskId}", taskId);
            throw;
        }
    }

    public async Task<ScheduledTaskDto> ReleaseTaskAsync(int taskId, string reason)
    {
        _logger.LogDebug("Releasing task {TaskId}, reason: {Reason}", taskId, reason);
        
        var task = await _repository.GetAsync(taskId);
        
        if (!task.IsLocked)
        {
            throw new BusinessException("Task is not locked");
        }

        if (task.IsCompleted)
        {
            throw new BusinessException("Cannot release completed task");
        }

        try
        {
            task.ReleaseLock();
            task = await _repository.UpdateAsync(task);

            _logger.LogInformation("Task {TaskId} released, reason: {Reason}", taskId, reason);

            return ObjectMapper.Map<ScheduledTask, ScheduledTaskDto>(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release task {TaskId}", taskId);
            throw;
        }
    }

    public async Task<List<ScheduledTaskDto>> GetTasksByTypeAsync(string taskType, TaskStatus? status = null)
    {
        var tasks = await _repository.GetTasksByTypeAsync(taskType, status);
        return ObjectMapper.Map<List<ScheduledTask>, List<ScheduledTaskDto>>(tasks);
    }

    public async Task<List<ScheduledTaskDto>> GetExpiredLocksAsync()
    {
        var expiredBefore = _clock.Now.Subtract(_lockTimeout);
        var tasks = await _repository.GetExpiredLocksAsync(expiredBefore);
        return ObjectMapper.Map<List<ScheduledTask>, List<ScheduledTaskDto>>(tasks);
    }

    public async Task<int> CleanupExpiredLocksAsync()
    {
        _logger.LogDebug("Starting cleanup of expired task locks");
        
        var expiredBefore = _clock.Now.Subtract(_lockTimeout);
        var cleanedCount = await _repository.CleanupExpiredLocksAsync(expiredBefore);
        
        _logger.LogInformation("Cleaned up {Count} expired task locks", cleanedCount);
        
        return cleanedCount;
    }

    public async Task<TaskStatisticsDto> GetTaskStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var from = fromDate ?? _clock.Now.AddDays(-30);
        var to = toDate ?? _clock.Now;
        
        var allTasks = await _repository.GetCompletedTasksAsync(from, to);
        var pendingTasks = await _repository.GetTasksByTypeAsync("", TaskStatus.Pending);
        var lockedTasks = await _repository.GetTasksByTypeAsync("", TaskStatus.Locked);
        var expiredLocks = await GetExpiredLocksAsync();
        
        var completedTasks = allTasks.Where(t => t.IsCompleted).ToList();
        
        var statistics = new TaskStatisticsDto
        {
            TotalTasks = allTasks.Count + pendingTasks.Count + lockedTasks.Count,
            CompletedTasks = completedTasks.Count,
            PendingTasks = pendingTasks.Count,
            LockedTasks = lockedTasks.Count,
            ExpiredLocks = expiredLocks.Count,
            AnalyzedFrom = from,
            AnalyzedTo = to
        };
        
        // Calculate average execution time
        var executionTimes = completedTasks
            .Select(t => t.GetResult<TaskCompletionResult>()?.ExecutionDuration)
            .Where(d => d.HasValue)
            .Select(d => d!.Value.TotalSeconds)
            .ToList();
        
        statistics.AverageExecutionTime = executionTimes.Any() ? executionTimes.Average() : 0;
        
        // Group by task type
        statistics.TasksByType = allTasks
            .GroupBy(t => t.TaskType ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());
        
        // Group by completed by
        statistics.CompletionsByUser = completedTasks
            .GroupBy(t => t.CompletedBy ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());
        
        return statistics;
    }

    protected override ScheduledTask MapToEntity(CreateTaskDto createInput)
    {
        var entity = ScheduledTask.Create(createInput.TaskType, createInput.Data);
        
        // Set scheduled time if provided
        if (createInput.ScheduledFor.HasValue)
        {
            // For future implementation of delayed execution
            entity.CreationTime = createInput.ScheduledFor.Value;
        }
        
        return entity;
    }

    protected override async Task<IQueryable<ScheduledTask>> CreateFilteredQueryAsync(PagedAndSortedResultRequestDto input)
    {
        var query = await base.CreateFilteredQueryAsync(input);
        
        // Default sorting by creation time descending
        return query.OrderByDescending(t => t.CreationTime);
    }
}
```

### Repository Implementation

```csharp
public class ScheduledTaskRepository : EfCoreRepository<HebloDbContext, ScheduledTask, int>, IScheduledTaskRepository
{
    public ScheduledTaskRepository(IDbContextProvider<HebloDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<ScheduledTask?> GetAvailableTaskAsync(string taskType, DateTime validLockDate, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        
        return await dbContext.Set<ScheduledTask>()
            .Where(t => t.TaskType == taskType)
            .Where(t => t.CompletedDate == null) // Not completed
            .Where(t => t.LockDate == null || t.LockDate < validLockDate) // Not locked or expired lock
            .OrderBy(t => t.CreationTime) // FIFO order
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<ScheduledTask>> GetTasksByTypeAsync(string taskType, TaskStatus? status = null, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var query = dbContext.Set<ScheduledTask>().AsQueryable();
        
        if (!string.IsNullOrEmpty(taskType))
        {
            query = query.Where(t => t.TaskType == taskType);
        }
        
        if (status.HasValue)
        {
            switch (status.Value)
            {
                case TaskStatus.Pending:
                    query = query.Where(t => t.CompletedDate == null && t.LockDate == null);
                    break;
                case TaskStatus.Locked:
                    query = query.Where(t => t.CompletedDate == null && t.LockDate != null);
                    break;
                case TaskStatus.Completed:
                    query = query.Where(t => t.CompletedDate != null);
                    break;
            }
        }
        
        return await query.OrderByDescending(t => t.CreationTime).ToListAsync(cancellationToken);
    }

    public async Task<List<ScheduledTask>> GetExpiredLocksAsync(DateTime expiredBefore, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        
        return await dbContext.Set<ScheduledTask>()
            .Where(t => t.CompletedDate == null) // Not completed
            .Where(t => t.LockDate != null && t.LockDate < expiredBefore) // Expired locks
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CleanupExpiredLocksAsync(DateTime expiredBefore, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        
        var expiredTasks = await dbContext.Set<ScheduledTask>()
            .Where(t => t.CompletedDate == null)
            .Where(t => t.LockDate != null && t.LockDate < expiredBefore)
            .ToListAsync(cancellationToken);
        
        foreach (var task in expiredTasks)
        {
            task.LockDate = null;
        }
        
        await dbContext.SaveChangesAsync(cancellationToken);
        
        return expiredTasks.Count;
    }

    public async Task<List<ScheduledTask>> GetCompletedTasksAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        
        return await dbContext.Set<ScheduledTask>()
            .Where(t => t.CreationTime >= fromDate && t.CreationTime <= toDate)
            .ToListAsync(cancellationToken);
    }
}
```

### Function Key Service Implementation

```csharp
public class FunctionKeyService : IFunctionKeyService
{
    private readonly IRepository<FunctionKey, string> _repository;
    private readonly ILogger<FunctionKeyService> _logger;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    public FunctionKeyService(
        IRepository<FunctionKey, string> repository,
        ILogger<FunctionKeyService> logger,
        IMemoryCache cache)
    {
        _repository = repository;
        _logger = logger;
        _cache = cache;
    }

    public async Task<bool> ValidateKeyAsync(string functionKey, string? scope = null)
    {
        if (string.IsNullOrEmpty(functionKey))
            return false;

        var cacheKey = $"FunctionKey_{functionKey}";
        
        if (_cache.TryGetValue(cacheKey, out FunctionKey? cachedKey))
        {
            return ValidateKey(cachedKey!, scope);
        }

        try
        {
            var key = await _repository.FindAsync(functionKey);
            
            if (key != null)
            {
                _cache.Set(cacheKey, key, CacheExpiration);
                
                // Update usage statistics
                key.UsageCount++;
                key.LastUsedAt = DateTime.UtcNow;
                await _repository.UpdateAsync(key);
            }
            
            return ValidateKey(key, scope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating function key");
            return false;
        }
    }

    public async Task<string> GenerateKeyAsync(string name, string? scope = null, TimeSpan? expiration = null)
    {
        var key = new FunctionKey
        {
            Id = GenerateSecureKey(),
            Name = name,
            Scope = scope,
            ExpiresAt = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.InsertAsync(key);
        
        _logger.LogInformation("Generated new function key '{Name}' with scope '{Scope}'", name, scope);
        
        return key.Id;
    }

    public async Task<bool> RevokeKeyAsync(string functionKey)
    {
        var key = await _repository.FindAsync(functionKey);
        
        if (key == null)
            return false;

        key.IsActive = false;
        await _repository.UpdateAsync(key);
        
        _cache.Remove($"FunctionKey_{functionKey}");
        
        _logger.LogInformation("Revoked function key '{Name}'", key.Name);
        
        return true;
    }

    public async Task<List<FunctionKeyInfo>> GetActiveKeysAsync()
    {
        var keys = await _repository.GetListAsync(k => k.IsActive);
        
        return keys.Select(k => new FunctionKeyInfo
        {
            Name = k.Name,
            Key = k.Id,
            Scope = k.Scope,
            CreatedAt = k.CreatedAt,
            ExpiresAt = k.ExpiresAt,
            IsActive = k.IsActive,
            UsageCount = k.UsageCount,
            LastUsedAt = k.LastUsedAt
        }).ToList();
    }

    private bool ValidateKey(FunctionKey? key, string? scope)
    {
        if (key == null || !key.IsActive)
            return false;

        if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.UtcNow)
            return false;

        if (!string.IsNullOrEmpty(scope) && key.Scope != scope)
            return false;

        return true;
    }

    private string GenerateSecureKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-").TrimEnd('=');
    }
}

// Entity for function key storage
public class FunctionKey : Entity<string>
{
    public string Name { get; set; }
    public string? Scope { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public int UsageCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
```

## Happy Day Scenario

1. **Task Creation**: System administrator creates a new scheduled task with specific type and JSON data
2. **Task Queuing**: Task is stored in database with pending status and audit information
3. **External Request**: External system requests available task using function key authentication
4. **Function Key Validation**: System validates function key and scope permissions
5. **Task Locking**: Available task is retrieved and locked with 10-minute timeout
6. **Task Execution**: External system processes the task using provided data
7. **Task Completion**: External system reports completion with results and execution details
8. **Audit Recording**: System records completion status, results, and timing information
9. **Lock Release**: Task lock is cleared and task marked as completed
10. **Statistics Update**: Task execution metrics are updated for monitoring and reporting

## Error Handling

### Authentication Errors
- **Invalid Function Key**: Clear error message with proper HTTP status codes
- **Expired Function Key**: Automatic key expiration handling with cache invalidation
- **Scope Mismatch**: Validate function key scope against requested operations
- **Missing Authentication**: Proper error responses for unauthenticated requests

### Concurrency Errors
- **Double Lock Prevention**: Validate task availability before locking
- **Lock Timeout Handling**: Automatic cleanup of expired locks
- **Concurrent Completion**: Prevent multiple completion attempts for same task
- **Race Condition Protection**: Database-level consistency checks

### Business Logic Errors
- **Invalid Task Type**: Validate task type against enumeration or configuration
- **Missing Task Data**: Handle empty or malformed JSON data gracefully
- **Completion Validation**: Ensure only locked tasks can be completed
- **State Transition Errors**: Validate proper task state transitions

### System Errors
- **Database Connectivity**: Retry mechanisms and connection pooling
- **Serialization Errors**: Robust JSON handling with error recovery
- **Cache Failures**: Fallback to database when cache unavailable
- **Memory Issues**: Proper resource cleanup and disposal

## Business Rules

### Task Lifecycle Rules
1. **Creation**: Tasks start in pending state with audit trail
2. **Locking**: Only pending or expired lock tasks can be acquired
3. **Execution**: Only locked tasks can be completed
4. **Completion**: Tasks can only be completed once
5. **Timeout**: Locks expire after 10 minutes automatically

### Concurrency Rules
1. **Single Lock**: One task per type can be locked by one executor
2. **FIFO Processing**: Tasks processed in creation time order
3. **Lock Expiration**: Automatic cleanup prevents stuck tasks
4. **Atomic Operations**: Lock acquisition and release are atomic

### Security Rules
1. **Function Key Authentication**: All external access requires valid keys
2. **Scope Validation**: Keys validated against operation scope
3. **Audit Trail**: All operations recorded with user attribution
4. **Key Management**: Secure generation, storage, and revocation

### Data Integrity Rules
1. **JSON Validation**: Structured data handling with error recovery
2. **Type Safety**: Task type validation against known types
3. **Result Storage**: Completion results stored for audit and analysis
4. **Cleanup Procedures**: Regular maintenance of expired locks and old data

## Performance Requirements
- Process single task acquisition within 100ms
- Handle 1000+ concurrent task requests
- Support multiple task types with isolated queues
- Maintain lock cleanup every 5 minutes
- Scale linearly with task volume
- Provide sub-second task statistics queries