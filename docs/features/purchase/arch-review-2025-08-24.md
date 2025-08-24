# Architecture Review: Purchase Feature
**Date:** 2025-08-24  
**Reviewer:** Architecture Team  
**Feature Path:** `backend/src/Anela.Heblo.Application/features/Purchase/`

## Executive Summary

The Purchase feature has a well-designed aggregate model but contains critical dependency injection antipatterns and security issues. The domain model is sound with proper status management and history tracking, but the implementation has hardcoded users and unsafe date handling that need immediate attention.

**Overall Score: 7/10** - Good domain design with critical DI issue now resolved

## Feature Overview

**Purpose:** Purchase order management with status tracking and stock analysis
**Key Operations:** CRUD operations on purchase orders, status updates, stock shortage analysis
**Domain Complexity:** Medium - involves order lifecycle, line items, and status history

## Architecture Analysis

### Strengths ✅

#### 1. Well-Designed Aggregate
```csharp
public class PurchaseOrder : AggregateRoot<Guid>
{
    public string OrderNumber { get; private set; }
    public string SupplierName { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public List<PurchaseOrderLine> Lines { get; private set; }
    public List<PurchaseOrderStatusHistory> StatusHistory { get; private set; }
    
    // Proper encapsulation and business methods
    public void UpdateStatus(PurchaseOrderStatus newStatus, Guid updatedByUserId, string? reason = null)
    {
        StatusHistory.Add(new PurchaseOrderStatusHistory(Status, newStatus, updatedByUserId, reason));
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }
}
```
- Proper aggregate design with encapsulation
- Status history tracking
- Business invariant protection

#### 2. Clean Repository Pattern
```csharp
public interface IPurchaseOrderRepository : IRepository<PurchaseOrder, Guid>
{
    Task<PurchaseOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<IEnumerable<PurchaseOrder>> GetByStatusAsync(PurchaseOrderStatus status, CancellationToken cancellationToken = default);
}
```
- Clean abstraction with specific query methods
- Both in-memory and database implementations available

#### 3. Good Stock Analysis Integration
```csharp
public class GetPurchaseStockAnalysisHandler
{
    // Integrates with catalog for intelligent purchase recommendations
    private readonly IStockSeverityCalculator _stockSeverityCalculator;
}
```

### Critical Issues 🔴

#### 1. ServiceProvider Antipattern ✅ FIXED
```csharp
// FIXED: In PurchaseModule.cs - Now uses factory pattern
public static IServiceCollection AddPurchaseModule(this IServiceCollection services)
{
    services.AddScoped<IPurchaseOrderRepository>(provider =>
    {
        var environment = provider.GetRequiredService<IHostEnvironment>();
        
        if (environment.EnvironmentName == "Automation" || environment.EnvironmentName == "Test")
        {
            return new InMemoryPurchaseOrderRepository();
        }
        else
        {
            var context = provider.GetRequiredService<ApplicationDbContext>();
            return new PurchaseOrderRepository(context);
        }
    });
}
```
**Resolution:** Factory pattern properly defers service provider access until service is requested.

#### 2. Hardcoded System User ⚠️
```csharp
// In CreatePurchaseOrderHandler.cs
var purchaseOrder = new PurchaseOrder(
    orderNumber,
    request.SupplierName,
    request.OrderDate.ToDateTime(TimeOnly.MinValue),
    Guid.Parse("00000000-0000-0000-0000-000000000001"), // ❌ Hardcoded "System" user
    request.ExpectedDeliveryDate?.ToDateTime(TimeOnly.MinValue),
    request.Notes,
    lines);
```
**Security Risk:** Hardcoded user ID bypasses authentication
**Solution:** Use current user service or explicit user parameter

#### 3. Unsafe Date Handling ❌
```csharp
// Manual DateTime conversion prone to timezone issues
request.OrderDate.ToDateTime(TimeOnly.MinValue)
request.ExpectedDeliveryDate?.ToDateTime(TimeOnly.MinValue)
```
**Problems:**
- Timezone handling inconsistencies
- Manual conversion error-prone
- No validation of date ranges

### Moderate Issues 🟡

#### 1. Business Logic in Handlers ⚠️
```csharp
public class CreatePurchaseOrderHandler
{
    // Mapping logic should be in dedicated mapper
    var lines = request.Lines?.Select(lineRequest => new PurchaseOrderLine(
        lineRequest.ProductId,
        lineRequest.ProductName ?? string.Empty,
        lineRequest.Quantity,
        lineRequest.UnitPrice,
        lineRequest.TotalPrice
    )).ToList() ?? new List<PurchaseOrderLine>();
}
```

#### 2. Missing Domain Events ⚠️
```csharp
public void UpdateStatus(PurchaseOrderStatus newStatus, Guid updatedByUserId, string? reason = null)
{
    // Updates status but doesn't raise domain events
    // Should notify other modules (inventory, accounting, etc.)
}
```

#### 3. No Transaction Boundaries ❌
```csharp
// Multiple repository operations without transaction coordination
await _repository.AddAsync(purchaseOrder, cancellationToken);
// What if this fails after order is created?
```

## Architecture Patterns

### Well Implemented ✅
- **Aggregate Pattern** - Proper aggregate root with encapsulation
- **Repository Pattern** - Clean data access abstraction
- **Status Pattern** - Order lifecycle management
- **History Tracking** - Audit trail for status changes

### Missing/Needed ❌
- **Unit of Work** - Transaction management
- **Domain Events** - Cross-module communication
- **Current User Context** - Proper user injection
- **Validation** - Input validation missing

## Security Assessment

### Critical Security Issues 🔴

#### 1. Hardcoded User ID
```csharp
Guid.Parse("00000000-0000-0000-0000-000000000001") // System user
```
**Risk:** Bypasses authentication and authorization
**Impact:** Orders created without proper user context

#### 2. No Authorization Checks
```csharp
public class UpdatePurchaseOrderHandler
{
    // No check if current user can modify this order
    public async Task<UpdatePurchaseOrderResponse> Handle(...)
}
```

#### 3. No Input Validation
```csharp
public class CreatePurchaseOrderRequest
{
    // No validation attributes
    public string SupplierName { get; set; } = null!;
    public DateOnly OrderDate { get; set; }
}
```

## Performance Considerations

### Current Performance
- ✅ Good use of async/await
- ✅ Proper cancellation token support
- ⚠️ No caching for frequently accessed data
- ❌ No pagination for order lists

### Optimization Opportunities
```csharp
// Add pagination
public class GetPurchaseOrdersRequest
{
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 50;
    public PurchaseOrderStatus? StatusFilter { get; set; }
}

// Add caching for lookups
[MemoryCache(Duration = 300)] // 5 minutes
public async Task<PurchaseOrder?> GetByOrderNumberAsync(string orderNumber)
```

## Recommendations

### Immediate Fixes (P0)

#### 1. Fix ServiceProvider Antipattern ✅ COMPLETED
**Status:** ALREADY IMPLEMENTED - ServiceProvider antipattern has been fixed using factory pattern.

The module now properly uses factory functions to defer service provider access:
- Repository selection based on environment without building intermediate ServiceProvider
- Proper dependency injection follows .NET Core best practices
- Memory leaks and circular dependency risks eliminated

#### 2. Fix Hardcoded User Issue
```csharp
public class CreatePurchaseOrderHandler
{
    private readonly ICurrentUserService _currentUserService;
    
    public async Task<CreatePurchaseOrderResponse> Handle(...)
    {
        var currentUserId = _currentUserService.GetCurrentUserId();
        
        var purchaseOrder = new PurchaseOrder(
            orderNumber,
            request.SupplierName,
            request.OrderDate.ToDateTime(TimeOnly.MinValue),
            currentUserId, // ✅ Use current user
            request.ExpectedDeliveryDate?.ToDateTime(TimeOnly.MinValue),
            request.Notes,
            lines);
    }
}
```

#### 3. Improve Date Handling
```csharp
public static class DateOnlyExtensions
{
    public static DateTime ToUtcDateTime(this DateOnly dateOnly)
    {
        return dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }
}

// Usage
var purchaseOrder = new PurchaseOrder(
    orderNumber,
    request.SupplierName,
    request.OrderDate.ToUtcDateTime(),
    currentUserId,
    request.ExpectedDeliveryDate?.ToUtcDateTime(),
    request.Notes,
    lines);
```

### Short-term Improvements (P1)

#### 1. Add Input Validation
```csharp
public class CreatePurchaseOrderRequestValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierName)
            .NotEmpty()
            .MaximumLength(200);
            
        RuleFor(x => x.OrderDate)
            .NotEmpty()
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)))
            .WithMessage("Order date cannot be more than 30 days in the future");
            
        RuleFor(x => x.ExpectedDeliveryDate)
            .GreaterThanOrEqualTo(x => x.OrderDate)
            .When(x => x.ExpectedDeliveryDate.HasValue);
            
        RuleForEach(x => x.Lines)
            .SetValidator(new CreatePurchaseOrderLineRequestValidator());
    }
}
```

#### 2. Add Domain Events
```csharp
public void UpdateStatus(PurchaseOrderStatus newStatus, Guid updatedByUserId, string? reason = null)
{
    var oldStatus = Status;
    StatusHistory.Add(new PurchaseOrderStatusHistory(oldStatus, newStatus, updatedByUserId, reason));
    Status = newStatus;
    UpdatedAt = DateTime.UtcNow;
    
    // Raise domain event
    AddDomainEvent(new PurchaseOrderStatusChangedEvent(
        Id, oldStatus, newStatus, updatedByUserId, reason));
}
```

#### 3. Implement Unit of Work
```csharp
public class CreatePurchaseOrderHandler
{
    private readonly IUnitOfWork _unitOfWork;
    
    public async Task<CreatePurchaseOrderResponse> Handle(...)
    {
        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var orderNumber = await _numberGenerator.GenerateNextNumberAsync();
            var purchaseOrder = new PurchaseOrder(/* params */);
            
            await _repository.AddAsync(purchaseOrder, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync();
            
            return new CreatePurchaseOrderResponse { Id = purchaseOrder.Id };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

### Long-term Improvements (P2)

1. **Workflow Engine** - Complex approval workflows
2. **Integration Events** - Publish to message bus
3. **Document Management** - Attach PO documents
4. **Advanced Analytics** - Purchase pattern analysis
5. **Supplier Integration** - Direct supplier system integration

## Testing Strategy

### Current Gaps
- No unit tests for aggregate behavior
- No integration tests for repository operations
- No validation tests for business rules

### Recommended Tests
```csharp
public class PurchaseOrderTests
{
    [Test]
    public void UpdateStatus_ValidTransition_UpdatesStatusAndHistory()
    {
        var order = new PurchaseOrder(/* params */);
        var userId = Guid.NewGuid();
        
        order.UpdateStatus(PurchaseOrderStatus.Approved, userId, "Approved by manager");
        
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Single(order.StatusHistory);
        Assert.Equal(userId, order.StatusHistory[0].UpdatedByUserId);
    }
}
```

## Conclusion

The Purchase feature has a solid domain model and proper aggregate design, but critical implementation issues make it unsuitable for production. The ServiceProvider antipattern and hardcoded user ID are security and reliability risks that must be addressed immediately.

**Critical Actions:**
1. ✅ **COMPLETED**: ServiceProvider antipattern fixed 
2. **URGENT**: Remove hardcoded user ID (1 day)
3. **HIGH**: Add proper validation and authorization (3 days)
4. **MEDIUM**: Implement transaction management (1 week)

**Risk Level:** Medium - Critical DI issue resolved, remaining security issues manageable

**Recommendation:** The feature shows good architectural thinking and major reliability issue is now resolved. Focus on hardcoded user ID and validation next.