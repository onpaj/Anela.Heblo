# Relocate `IssuedInvoiceFilters` and `PaginatedResult<T>` out of Domain — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `IssuedInvoiceFilters`, `PaginatedResult<T>`, `IIssuedInvoiceRepository` (interface), and its implementation out of the Domain/Persistence projects into the Application layer, restoring Clean Architecture boundaries without changing any runtime behavior.

**Architecture:** Pure structural refactor. The implementation must be done in an additive-then-migrate sequence: (1) create the new types in Application as additions, (2) migrate every consumer to the new namespaces one file at a time, (3) flip DI in `InvoicesModule.cs`, (4) delete the obsolete Domain/Persistence files. This sequence keeps the solution buildable after every task. Also extract `IssuedInvoiceSyncStats` into its own Domain file (housekeeping required by FR-5 amendment) and add a reflection-based architectural test to prevent regression.

**Tech Stack:** .NET 8, C#, EF Core 8 (Npgsql), xUnit + FluentAssertions + Moq, AutoMapper. The codebase has inverted Clean-Architecture project references: `Application → Domain + Persistence + Xcc`, `Persistence → Domain + Xcc` (does **not** reference Application). Because Persistence cannot reference Application, both the interface AND the implementation move into Application (the implementation goes to `Application/Features/Invoices/Infrastructure/`, using `ApplicationDbContext` and `BaseRepository<T,TKey>` which are reachable via the existing `Application → Persistence` reference).

---

## File Structure

### New files

| File | Namespace | Responsibility |
|------|-----------|----------------|
| `backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs` | `Anela.Heblo.Application.Shared` | Generic pagination envelope, reusable across features. |
| `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs` | `Anela.Heblo.Application.Features.Invoices.Contracts` | Filter/sort/page parameters for issued invoice listing. |
| `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs` | `Anela.Heblo.Application.Features.Invoices.Contracts` | Application-owned repository contract for `IssuedInvoice`. |
| `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` | `Anela.Heblo.Application.Features.Invoices.Infrastructure` | EF Core implementation of the contract. |
| `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceSyncStats.cs` | `Anela.Heblo.Domain.Features.Invoices` | Domain stats type, split out of the now-deleted multi-type file. |

### Files to delete

| File | Reason |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs` | All four types it contained have moved or been split out. |
| `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` | Implementation moved to `Application/Features/Invoices/Infrastructure/`. |

### Files to modify (using-directive updates only)

| File | Edit |
|------|------|
| `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` | Swap `using Anela.Heblo.Domain.Features.Invoices;` + `using Anela.Heblo.Persistence.Features.Invoices;` for the new namespaces. DI registration line itself is unchanged. |
| `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoicesList/GetIssuedInvoicesListHandler.cs` | Remove `using Anela.Heblo.Domain.Features.Invoices;` (no Domain type names appear directly). The existing `using Anela.Heblo.Application.Features.Invoices.Contracts;` and `using Anela.Heblo.Application.Shared;` now cover the moved types. |
| `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceSyncStats/GetIssuedInvoiceSyncStatsHandler.cs` | Add `using Anela.Heblo.Application.Features.Invoices.Contracts;` (for the moved repo interface). Keep `using Anela.Heblo.Domain.Features.Invoices;` (still needed for `IssuedInvoiceSyncStats`). |
| `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceDetail/GetIssuedInvoiceDetailHandler.cs` | Keep `using Anela.Heblo.Application.Features.Invoices.Contracts;` (already imported; now also resolves the repo interface). Keep `using Anela.Heblo.Domain.Features.Invoices;` (entity `IssuedInvoice` referenced indirectly via repo return). |
| `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` | Same as above — Contracts using is already there; Domain using remains for entity. |
| `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapter.cs` | Add `using Anela.Heblo.Application.Features.Invoices.Contracts;`. Keep `using Anela.Heblo.Domain.Features.Invoices;` for `IssuedInvoice` entity used indirectly. |
| `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` | Swap `using Anela.Heblo.Persistence.Features.Invoices;` for `using Anela.Heblo.Application.Features.Invoices.Infrastructure;`. Add `using Anela.Heblo.Application.Features.Invoices.Contracts;` (filters). Keep `using Anela.Heblo.Domain.Features.Invoices;` (entity). |
| `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoicesListHandlerPaginationTests.cs` | Already imports `Anela.Heblo.Application.Features.Invoices.Contracts`. Add `using Anela.Heblo.Application.Shared;` (for `PaginatedResult`). Keep `using Anela.Heblo.Domain.Features.Invoices;` (entity). |
| `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs` | Add `using Anela.Heblo.Application.Features.Invoices.Contracts;` (for repo interface). Keep `using Anela.Heblo.Domain.Features.Invoices;` (entity + `IssuedInvoiceSource*` types). |
| `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapterTests.cs` | Add `using Anela.Heblo.Application.Features.Invoices.Contracts;` (for repo interface). Keep `using Anela.Heblo.Domain.Features.Invoices;` (entity). |

### Files to extend

| File | Edit |
|------|------|
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | Add a new `[Fact]` that enforces NFR-6: no `Anela.Heblo.Domain.*` type references `Anela.Heblo.Application.*`, and that the three relocated tokens (`IssuedInvoiceFilters`, `PaginatedResult`, `IIssuedInvoiceRepository`) do not appear anywhere under `Anela.Heblo.Domain.Features.Invoices`. |

---

## Task-by-Task Plan

### Task 1: Create `PaginatedResult<T>` in `Application/Shared`

This is an additive change. The new type coexists with `Anela.Heblo.Domain.Features.Invoices.PaginatedResult<T>` until later tasks delete the Domain version. No consumer is touched yet.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs`

- [ ] **Step 1: Create the new file**

Write the file at `backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs` with exactly this content:

```csharp
namespace Anela.Heblo.Application.Shared;

/// <summary>
/// Paginated result container
/// </summary>
/// <typeparam name="T">Type of items</typeparam>
public class PaginatedResult<T>
{
    public IEnumerable<T> Items { get; set; } = new List<T>();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasPreviousPage => PageNumber > 1;
}
```

- [ ] **Step 2: Verify the build still passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, zero new warnings. (The new type and the existing Domain type now coexist in different namespaces — this is fine because no consumer imports both.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs
git commit -m "refactor: add PaginatedResult<T> in Application.Shared (parallel to Domain copy)"
```

---

### Task 2: Create `IssuedInvoiceFilters` in `Application/Features/Invoices/Contracts`

Same additive pattern as Task 1.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs`

- [ ] **Step 1: Create the new file**

Write the file at `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs` with exactly this content (property names, types, defaults, and accessibility must be byte-identical to the Domain original):

```csharp
namespace Anela.Heblo.Application.Features.Invoices.Contracts;

/// <summary>
/// Filter criteria for issued invoices
/// </summary>
public class IssuedInvoiceFilters
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
    public string? InvoiceId { get; set; }
    public string? CustomerName { get; set; }
    public DateTime? InvoiceDateFrom { get; set; }
    public DateTime? InvoiceDateTo { get; set; }
    public bool? IsSynced { get; set; }
    public bool ShowOnlyUnsynced { get; set; }
    public bool ShowOnlyWithErrors { get; set; }
}
```

- [ ] **Step 2: Verify the build still passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, zero new warnings.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs
git commit -m "refactor: add IssuedInvoiceFilters in Application.Features.Invoices.Contracts (parallel to Domain copy)"
```

---

### Task 3: Extract `IssuedInvoiceSyncStats` into its own Domain file

Required by the FR-5 amendment in `arch-review.r1.md`. The type stays in Domain in the same namespace — only its file location changes. The Domain file `IIssuedInvoiceRepository.cs` is still kept in this task; only the `IssuedInvoiceSyncStats` class is removed from it.

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceSyncStats.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs:84-96` (remove the `IssuedInvoiceSyncStats` class)

- [ ] **Step 1: Create the new Domain file**

Write `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceSyncStats.cs` with this content:

```csharp
namespace Anela.Heblo.Domain.Features.Invoices;

/// <summary>
/// Statistics for invoice synchronization
/// </summary>
public class IssuedInvoiceSyncStats
{
    public int TotalInvoices { get; set; }
    public int SyncedInvoices { get; set; }
    public int UnsyncedInvoices { get; set; }
    public int InvoicesWithErrors { get; set; }
    public int CriticalErrors { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public decimal SyncSuccessRate => TotalInvoices > 0 ? (decimal)SyncedInvoices / TotalInvoices * 100 : 0;
}
```

- [ ] **Step 2: Remove `IssuedInvoiceSyncStats` from the original combined file**

Open `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs` and delete lines 83–96 (the blank separator line through the closing brace of the `IssuedInvoiceSyncStats` class). Use this exact `old_string → new_string` edit:

`old_string`:

```csharp
    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>
/// Statistics for invoice synchronization
/// </summary>
public class IssuedInvoiceSyncStats
{
    public int TotalInvoices { get; set; }
    public int SyncedInvoices { get; set; }
    public int UnsyncedInvoices { get; set; }
    public int InvoicesWithErrors { get; set; }
    public int CriticalErrors { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public decimal SyncSuccessRate => TotalInvoices > 0 ? (decimal)SyncedInvoices / TotalInvoices * 100 : 0;
}

/// <summary>
/// Filter criteria for issued invoices
/// </summary>
```

`new_string`:

```csharp
    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter criteria for issued invoices
/// </summary>
```

- [ ] **Step 3: Verify the build still passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, zero new warnings. (`IssuedInvoiceSyncStats` now lives in its own file but in the same namespace — every existing reference resolves identically.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceSyncStats.cs backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs
git commit -m "refactor: extract IssuedInvoiceSyncStats into its own file"
```

---

### Task 4: Create the new `IIssuedInvoiceRepository` in `Application/Features/Invoices/Contracts`

The new interface has the identical shape to the Domain original but lives in the Application Contracts namespace. Briefly there are two interfaces with the same simple name `IIssuedInvoiceRepository` — this is fine because no consumer imports both namespaces in the same file (until later tasks switch them over). DI still registers the Domain version (via `InvoicesModule.cs`).

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`

- [ ] **Step 1: Create the new file**

Write `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs` with exactly this content (note `Persistance` spelling — that is the actual Xcc namespace, not a typo):

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Features.Invoices.Contracts;

/// <summary>
/// Repository interface for IssuedInvoice entity
/// Provides specialized query operations beyond basic CRUD
/// </summary>
public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    /// <summary>
    /// Finds invoices by their synchronization status
    /// </summary>
    /// <param name="isSynced">True for synced invoices, false for unsynced, null for all</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching invoices</returns>
    Task<IEnumerable<IssuedInvoice>> FindBySyncStatusAsync(bool? isSynced, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices within a date range
    /// </summary>
    /// <param name="fromDate">Start date (inclusive)</param>
    /// <param name="toDate">End date (inclusive)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching invoices</returns>
    Task<IEnumerable<IssuedInvoice>> FindByInvoiceDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices by customer name (partial match)
    /// </summary>
    /// <param name="customerName">Customer name or partial name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching invoices</returns>
    Task<IEnumerable<IssuedInvoice>> FindByCustomerNameAsync(string customerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices with sync errors (excluding invoice paired errors)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of invoices with critical errors</returns>
    Task<IEnumerable<IssuedInvoice>> FindWithCriticalErrorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an invoice with its complete sync history
    /// </summary>
    /// <param name="id">Invoice ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Invoice with sync history or null if not found</returns>
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets invoices that were last synced before a specific date
    /// </summary>
    /// <param name="beforeDate">Date threshold</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of invoices that need re-sync</returns>
    Task<IEnumerable<IssuedInvoice>> FindStaleInvoicesAsync(DateTime beforeDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sync statistics for a date range
    /// </summary>
    /// <param name="fromDate">Start date</param>
    /// <param name="toDate">End date</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sync statistics</returns>
    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated and filtered list of issued invoices with sorting
    /// </summary>
    /// <param name="filters">Filter criteria</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated result with total count</returns>
    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets invoice headers for a specific date
    /// </summary>
    /// <param name="date">The date to retrieve invoice headers for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Invoice headers whose invoice date falls on the given date</returns>
    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify the build still passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, zero new warnings. (Two interfaces with the same simple name coexist — no consumer imports both at the same time.)

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs
git commit -m "refactor: add IIssuedInvoiceRepository in Application.Features.Invoices.Contracts"
```

---

### Task 5: Create the new `IssuedInvoiceRepository` implementation in `Application/Features/Invoices/Infrastructure`

This new file implements the Application-namespace interface from Task 4. It is the byte-equivalent of the existing Persistence implementation with adjusted namespaces. Two implementations coexist briefly; neither is wired into DI by accident because `InvoicesModule.cs` still uses the Domain interface.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs`

- [ ] **Step 1: Create the new file**

Write `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` with this content (this is a copy-with-namespace-adjustment of the current Persistence file; behavior is identical):

```csharp
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

/// <summary>
/// Repository implementation for IssuedInvoice entity
/// </summary>
public class IssuedInvoiceRepository : BaseRepository<IssuedInvoice, string>, IIssuedInvoiceRepository
{
    private readonly ILogger<IssuedInvoiceRepository> _logger;

    public IssuedInvoiceRepository(ApplicationDbContext context, ILogger<IssuedInvoiceRepository> logger)
        : base(context)
    {
        _logger = logger;
    }

    public override async Task<IssuedInvoice?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(x => x.SyncHistory)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<IssuedInvoice>> FindBySyncStatusAsync(bool? isSynced, CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();

        if (isSynced.HasValue)
        {
            query = query.Where(x => x.IsSynced == isSynced.Value);
        }

        return await query
            .OrderByDescending(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IssuedInvoice>> FindByInvoiceDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(x => x.InvoiceDate >= fromDate.Date && x.InvoiceDate <= toDate.Date)
            .OrderByDescending(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IssuedInvoice>> FindByCustomerNameAsync(string customerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return new List<IssuedInvoice>();
        }

        var searchTerm = customerName.Trim().ToLower();

        return await DbSet
            .Where(x => x.CustomerName != null && x.CustomerName.ToLower().Contains(searchTerm))
            .OrderByDescending(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IssuedInvoice>> FindWithCriticalErrorsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(x => x.ErrorType != null && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired)
            .OrderByDescending(x => x.LastSyncTime ?? x.InvoiceDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IssuedInvoice>> FindStaleInvoicesAsync(DateTime beforeDate, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(x => !x.IsSynced || (x.LastSyncTime.HasValue && x.LastSyncTime.Value < beforeDate))
            .OrderByDescending(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(x => x.InvoiceDate >= fromDate.Date && x.InvoiceDate <= toDate.Date);

        var totalInvoices = await query.CountAsync(cancellationToken);
        var syncedInvoices = await query.CountAsync(x => x.IsSynced, cancellationToken);
        var unsyncedInvoices = totalInvoices - syncedInvoices;
        var invoicesWithErrors = await query.CountAsync(x => x.ErrorType.HasValue, cancellationToken);
        var criticalErrors = await query.CountAsync(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired, cancellationToken);

        var lastSyncTime = await query
            .Where(x => x.LastSyncTime.HasValue)
            .MaxAsync(x => (DateTime?)x.LastSyncTime, cancellationToken);

        return new IssuedInvoiceSyncStats
        {
            TotalInvoices = totalInvoices,
            SyncedInvoices = syncedInvoices,
            UnsyncedInvoices = unsyncedInvoices,
            InvoicesWithErrors = invoicesWithErrors,
            CriticalErrors = criticalErrors,
            LastSyncTime = lastSyncTime
        };
    }

    public override async Task<IssuedInvoice> AddAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
    {
        // Set audit fields for new entities
        entity.CreationTime = DateTime.UtcNow;
        entity.ConcurrencyStamp = Guid.NewGuid().ToString();

        return await base.AddAsync(entity, cancellationToken);
    }

    public override async Task UpdateAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
    {
        // Set audit fields for updates
        entity.LastModificationTime = DateTime.UtcNow;
        entity.ConcurrencyStamp = Guid.NewGuid().ToString();

        await base.UpdateAsync(entity, cancellationToken);
    }

    public async Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(filters.InvoiceId))
        {
            var invoiceId = filters.InvoiceId.Trim();
            query = query.Where(x => x.Id.Contains(invoiceId));
        }

        if (!string.IsNullOrWhiteSpace(filters.CustomerName))
        {
            var customerName = filters.CustomerName.Trim();
            query = query.Where(x => EF.Functions.ILike(x.CustomerName!, $"%{customerName}%"));
        }

        if (filters.InvoiceDateFrom.HasValue)
        {
            query = query.Where(x => x.InvoiceDate >= filters.InvoiceDateFrom.Value.Date);
        }

        if (filters.InvoiceDateTo.HasValue)
        {
            query = query.Where(x => x.InvoiceDate <= filters.InvoiceDateTo.Value.Date);
        }

        if (filters.ShowOnlyUnsynced)
        {
            query = query.Where(x => !x.IsSynced);
        }
        else if (filters.IsSynced.HasValue)
        {
            query = query.Where(x => x.IsSynced == filters.IsSynced.Value);
        }

        if (filters.ShowOnlyWithErrors)
        {
            query = query.Where(x => x.ErrorType.HasValue);
        }

        // Apply sorting
        query = ApplySorting(query, filters.SortBy, filters.SortDescending);

        // Apply pagination
        List<IssuedInvoice> items;
        int totalCount;
        if (filters.PageSize == 0)
        {
            // PageSize = 0 means return all items without pagination; derive count from loaded list to avoid a second DB round-trip
            items = await query.ToListAsync(cancellationToken);
            totalCount = items.Count;
        }
        else
        {
            totalCount = await query.CountAsync(cancellationToken);
            items = await query
                .Skip((filters.PageNumber - 1) * filters.PageSize)
                .Take(filters.PageSize)
                .ToListAsync(cancellationToken);
        }

        var totalPages = filters.PageSize > 0 ? Math.Ceiling((double)totalCount / filters.PageSize) : 1;
        _logger.LogInformation("Retrieved {Count} issued invoices (page {PageNumber}/{TotalPages}, total: {TotalCount})",
            items.Count, filters.PageNumber, totalPages, totalCount);

        return new PaginatedResult<IssuedInvoice>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filters.PageNumber,
            PageSize = filters.PageSize
        };
    }

    public async Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.ToDateTime(TimeOnly.MaxValue);
        return await DbSet
            .Where(x => x.InvoiceDate >= start && x.InvoiceDate <= end)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<IssuedInvoice> ApplySorting(IQueryable<IssuedInvoice> query, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrEmpty(sortBy))
        {
            return sortDescending
                ? query.OrderByDescending(x => x.InvoiceDate)
                : query.OrderBy(x => x.InvoiceDate);
        }

        return sortBy.ToLower() switch
        {
            "invoicedate" => sortDescending
                ? query.OrderByDescending(x => x.InvoiceDate)
                : query.OrderBy(x => x.InvoiceDate),
            "id" => sortDescending
                ? query.OrderByDescending(x => x.Id)
                : query.OrderBy(x => x.Id),
            "customername" => sortDescending
                ? query.OrderByDescending(x => x.CustomerName ?? string.Empty)
                : query.OrderBy(x => x.CustomerName ?? string.Empty),
            "price" => sortDescending
                ? query.OrderByDescending(x => x.Price)
                : query.OrderBy(x => x.Price),
            "issync" or "issynced" => sortDescending
                ? query.OrderByDescending(x => x.IsSynced)
                : query.OrderBy(x => x.IsSynced),
            "lastsynctime" => sortDescending
                ? query.OrderByDescending(x => x.LastSyncTime ?? DateTime.MinValue)
                : query.OrderBy(x => x.LastSyncTime ?? DateTime.MinValue),
            _ => sortDescending
                ? query.OrderByDescending(x => x.InvoiceDate)
                : query.OrderBy(x => x.InvoiceDate)
        };
    }
}
```

- [ ] **Step 2: Verify the build still passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, zero new warnings. Two `IssuedInvoiceRepository` classes coexist briefly — DI still wires the Persistence one because `InvoicesModule.cs` hasn't been touched.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs
git commit -m "refactor: add IssuedInvoiceRepository implementation in Application.Features.Invoices.Infrastructure"
```

---

### Task 6: Migrate `GetIssuedInvoicesListHandler` to the new namespaces

This handler currently imports `Anela.Heblo.Domain.Features.Invoices` solely to resolve `IIssuedInvoiceRepository`, `IssuedInvoiceFilters`, and `PaginatedResult` (it uses no Domain entity name directly — `IssuedInvoice` only appears as an inferred generic via `var paginatedResult = await _repository.GetPaginatedAsync(...)`). After the switch, the existing `using Anela.Heblo.Application.Features.Invoices.Contracts;` covers the moved repo + filters, and `using Anela.Heblo.Application.Shared;` covers `PaginatedResult`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoicesList/GetIssuedInvoicesListHandler.cs:1-7`

- [ ] **Step 1: Replace the using block**

Apply this exact `old_string → new_string` edit:

`old_string`:

```csharp
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
```

`new_string`:

```csharp
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Shared;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
```

- [ ] **Step 2: Verify the build still passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, zero new warnings. The handler now uses the Application versions of all three relocated types; DI still resolves to the Domain version (registered in `InvoicesModule.cs`). At this point this handler will be incompatible with the registered service at runtime — but that's fine because no test runs DI startup validation until Task 12 fixes `InvoicesModule.cs` to register the Application interface.

> NOTE: Some unit tests may use a manual `Mock<IIssuedInvoiceRepository>()` constructed against the Application interface (see `GetIssuedInvoicesListHandlerPaginationTests` — already imports Contracts). Tests that mock the repository directly are unaffected by DI changes. The full integration test (`InvoiceImportIntegrationTests`) boots the host and would only break if DI resolution fails — but it does not depend on the Application interface yet.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoicesList/GetIssuedInvoicesListHandler.cs
git commit -m "refactor: switch GetIssuedInvoicesListHandler to Application.Features.Invoices.Contracts"
```

---

### Task 7: Migrate `GetIssuedInvoiceSyncStatsHandler` to the new namespaces

This handler uses `IIssuedInvoiceRepository` (moves) and `IssuedInvoiceSyncStats` (stays in Domain in its own file from Task 3).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceSyncStats/GetIssuedInvoiceSyncStatsHandler.cs:1-4`

- [ ] **Step 1: Update the using block**

`old_string`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using MediatR;
using Microsoft.Extensions.Logging;
```

`new_string`:

```csharp
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using MediatR;
using Microsoft.Extensions.Logging;
```

(The `Domain.Features.Invoices` using is kept because `IssuedInvoiceSyncStats` lives there — although its name doesn't appear in this file by simple identifier, leaving the using is harmless; the test methods of the handler reference it indirectly via `stats.TotalInvoices` on an inferred `var stats`. Leaving the Domain using ensures no surprise breaks.)

- [ ] **Step 2: Verify the build still passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, zero new warnings.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceSyncStats/GetIssuedInvoiceSyncStatsHandler.cs
git commit -m "refactor: switch GetIssuedInvoiceSyncStatsHandler to Application.Features.Invoices.Contracts"
```

---

### Task 8: Migrate `GetIssuedInvoiceDetailHandler` to the new namespaces

This handler already imports `Anela.Heblo.Application.Features.Invoices.Contracts` (for the `IssuedInvoiceDetailDto`). After the relocation, the same using also resolves `IIssuedInvoiceRepository`. The `Domain.Features.Invoices` using stays because `invoice` is inferred from `_repository.GetByIdAsync(...)` which returns `Task<IssuedInvoice?>` — keeping the Domain using costs nothing and avoids any indirect-resolution surprise.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceDetail/GetIssuedInvoiceDetailHandler.cs`

- [ ] **Step 1: Verify no change is required**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, zero new warnings. (The handler's using directives already cover both the old and the new home of `IIssuedInvoiceRepository`. Once Task 12 ships, the new namespace will be the only home and the existing using continues to work.)

If at this stage the build is still green, no edit is needed in this task. **Skip to commit step.**

- [ ] **Step 2: Confirm with a focused grep**

Run: `grep -nE "IIssuedInvoiceRepository|IssuedInvoiceFilters|PaginatedResult" backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceDetail/GetIssuedInvoiceDetailHandler.cs`
Expected: only `IIssuedInvoiceRepository` matches (twice — field + ctor param). Filters and PaginatedResult are not used here.

- [ ] **Step 3: Commit (only if any edit was actually made)**

If no edit was required, skip to Task 9. Otherwise:

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceDetail/GetIssuedInvoiceDetailHandler.cs
git commit -m "refactor: confirm GetIssuedInvoiceDetailHandler usings cover Application.Features.Invoices.Contracts"
```

---

### Task 9: Migrate `InvoiceImportService` to the new namespaces

Like the detail handler, this service already imports `Anela.Heblo.Application.Features.Invoices.Contracts` (for DTOs). The new `IIssuedInvoiceRepository` will resolve via the same using. `using Anela.Heblo.Domain.Features.Invoices;` remains because the service uses `IssuedInvoice` (entity) and `IssuedInvoiceSourceQuery` (Domain types).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`

- [ ] **Step 1: Verify no change is required**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 2: Confirm with a focused grep**

Run: `grep -nE "IIssuedInvoiceRepository|IssuedInvoiceFilters|PaginatedResult" backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`
Expected: only `IIssuedInvoiceRepository` matches (field + ctor param + assignment).

- [ ] **Step 3: Commit (only if any edit was actually made)**

No edit expected. Skip to Task 10.

---

### Task 10: Migrate `InvoiceConsumptionSourceAdapter` to the new namespaces

This adapter uses `IIssuedInvoiceRepository` and indirectly the `IssuedInvoice` entity. It currently imports only `Domain.Features.Invoices`. Add the Contracts using so the moved interface is resolved from there once the Domain copy is gone in Task 13.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapter.cs:1-4`

- [ ] **Step 1: Update the using block**

`old_string`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;
```

`new_string`:

```csharp
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;
```

- [ ] **Step 2: Verify the build still passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

> ⚠️ POTENTIAL AMBIGUITY: this file is in namespace `Anela.Heblo.Application.Features.Invoices.Infrastructure`. The new `IssuedInvoiceRepository` (Task 5) is in the **same** namespace. The new `IIssuedInvoiceRepository` is in `Anela.Heblo.Application.Features.Invoices.Contracts`. No type name collision arises because the adapter only references the interface, not the concrete class. If the compiler reports `CS0104 Ambiguous reference` for `IIssuedInvoiceRepository`, the only possible cause is that the Domain copy still exists (which is expected at this stage) AND the file imports both. In that case, fall back to fully-qualified `Anela.Heblo.Application.Features.Invoices.Contracts.IIssuedInvoiceRepository` for the type until Task 13 deletes the Domain copy. The default expectation is that no ambiguity occurs because the adapter's `Domain.Features.Invoices` using is purely for `IssuedInvoice` entity references (the field type `IIssuedInvoiceRepository` resolves through the Contracts using added in this task; but if the compiler binds to the Domain copy first, an explicit fully-qualified name is the safest hot-fix).

- [ ] **Step 3: If `CS0104` appears, apply the fully-qualified hot-fix**

`old_string`:

```csharp
    private readonly IIssuedInvoiceRepository _repository;

    public InvoiceConsumptionSourceAdapter(IIssuedInvoiceRepository repository)
```

`new_string`:

```csharp
    private readonly Anela.Heblo.Application.Features.Invoices.Contracts.IIssuedInvoiceRepository _repository;

    public InvoiceConsumptionSourceAdapter(Anela.Heblo.Application.Features.Invoices.Contracts.IIssuedInvoiceRepository repository)
```

After Task 13 deletes the Domain copy, revert this to the simple name in a follow-up step. (If no `CS0104` appears, skip this step entirely.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapter.cs
git commit -m "refactor: switch InvoiceConsumptionSourceAdapter to Application.Features.Invoices.Contracts"
```

---

### Task 11: Migrate test files to the new namespaces

Five test files reference the relocated types: `IssuedInvoiceRepositoryTests`, `GetIssuedInvoicesListHandlerPaginationTests`, `InvoiceImportServiceTests`, `Infrastructure/InvoiceConsumptionSourceAdapterTests`, and a transformations test that references only `IssuedInvoice` (no change needed). Each file's `using` block is updated to import the new namespaces. Test logic is **not** changed.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs:1-7`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoicesListHandlerPaginationTests.cs:1-11`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs:1-9`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapterTests.cs:1-6`

- [ ] **Step 1: Update `IssuedInvoiceRepositoryTests.cs`**

`old_string`:

```csharp
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

`new_string`:

```csharp
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

- [ ] **Step 2: Update `GetIssuedInvoicesListHandlerPaginationTests.cs`**

`old_string`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoicesList;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Moq;
using Xunit;
```

`new_string`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoicesList;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Moq;
using Xunit;
```

- [ ] **Step 3: Update `InvoiceImportServiceTests.cs`**

`old_string`:

```csharp
using System.ComponentModel;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

`new_string`:

```csharp
using System.ComponentModel;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

(This file's using block already covers both `Contracts` and `Domain.Features.Invoices`. No textual change is required, but verify by running grep below.)

Run: `grep -n "using Anela.Heblo.Application.Features.Invoices.Contracts" backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs`
Expected: exactly one match. If the line is missing for any reason, add it.

- [ ] **Step 4: Update `InvoiceConsumptionSourceAdapterTests.cs`**

`old_string`:

```csharp
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Moq;
using Xunit;
```

`new_string`:

```csharp
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Moq;
using Xunit;
```

- [ ] **Step 5: Verify the test project builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds. The Application interface and Persistence interface coexist; tests bind their `Mock<IIssuedInvoiceRepository>()` and `IssuedInvoiceFilters` to whatever the using resolves to. Tests that use the new Contracts using bind to the Application copy; tests that don't bind to whatever resolves via their other usings (i.e., the Domain copy, which still exists).

> ⚠️ AMBIGUITY CHECK: If any test now imports both `Contracts` and `Domain.Features.Invoices`, the compiler may emit `CS0104` for `IIssuedInvoiceRepository`, `IssuedInvoiceFilters`, or `PaginatedResult`. In that case, fall back to fully-qualifying the relocated names at the call site for now (e.g. `Mock<Anela.Heblo.Application.Features.Invoices.Contracts.IIssuedInvoiceRepository>()`). Task 13 removes the Domain copy and the simple names work again — revert to simple names then.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoicesListHandlerPaginationTests.cs backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapterTests.cs
git commit -m "refactor: switch invoice tests to Application.Features.Invoices.Contracts and Application.Shared"
```

---

### Task 12: Flip the DI registration in `InvoicesModule.cs`

Now every production-code consumer references the Application interface. Swap the `InvoicesModule.cs` usings so `AddScoped<IIssuedInvoiceRepository, IssuedInvoiceRepository>()` resolves both symbols to the Application copies. The line itself does not change.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs:1-10`

- [ ] **Step 1: Replace the using block**

`old_string`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Persistence.Features.Invoices;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;

namespace Anela.Heblo.Application.Features.Invoices;
```

`new_string`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Bank;

namespace Anela.Heblo.Application.Features.Invoices;
```

(The `Domain.Features.Bank` using stays; it is unrelated to this refactor. The `Persistence.Features.Invoices` using is removed — its only purpose was to resolve the old `IssuedInvoiceRepository` concrete class, which is now reachable via the new `Application.Features.Invoices.Infrastructure` using.)

- [ ] **Step 2: Verify the build still passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 3: Verify the runtime tests pass (DI smoke test)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Invoices" --no-build`
Expected: all invoices tests pass. This is the moment the DI rewiring is live — if the registration is wrong (e.g. still resolving to a stale type via global usings), unit tests for handlers that depend on `IIssuedInvoiceRepository` would fail.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs
git commit -m "refactor: flip InvoicesModule DI to use Application.Features.Invoices contracts"
```

---

### Task 13: Delete the old Domain file

The old file `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs` now contains only the obsolete Domain copies of `IIssuedInvoiceRepository`, `IssuedInvoiceFilters`, and `PaginatedResult<T>` (the `IssuedInvoiceSyncStats` class was moved to its own file in Task 3). Nothing references these Domain copies anymore.

**Files:**
- Delete: `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`

- [ ] **Step 1: Confirm no remaining references**

Run: `grep -rnE "Anela\.Heblo\.Domain\.Features\.Invoices\.(IIssuedInvoiceRepository|IssuedInvoiceFilters|PaginatedResult)" backend/`
Expected: zero matches (after Task 12, no consumer fully-qualifies these names).

Run: `grep -rnE "(IIssuedInvoiceRepository|IssuedInvoiceFilters|PaginatedResult)" backend/src/Anela.Heblo.Domain/`
Expected: zero matches.

- [ ] **Step 2: Delete the file**

Run: `rm backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`

- [ ] **Step 3: Verify the build still passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds with zero new warnings. If any `CS0246`-type errors appear, that file still had a consumer — re-check Tasks 6–11.

- [ ] **Step 4: If Task 10/11 applied the fully-qualified hot-fix, revert it now**

For any file that used `Anela.Heblo.Application.Features.Invoices.Contracts.IIssuedInvoiceRepository` (fully qualified) as a CS0104 workaround, replace with the simple name `IIssuedInvoiceRepository`. The simple name now resolves unambiguously to the Application interface.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add -u backend/src/Anela.Heblo.Domain/Features/Invoices/
git commit -m "refactor: remove obsolete Domain copies of IIssuedInvoiceRepository, IssuedInvoiceFilters, PaginatedResult<T>"
```

---

### Task 14: Delete the old Persistence implementation

The old `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` implemented the now-deleted Domain interface. With the Domain interface gone, the file no longer compiles (it would fail Step 3 of Task 13 if it existed). So Task 13 must be followed immediately by Task 14, or the order can be flipped — but the simplest path is: at the moment we delete the Domain interface, the Persistence file also fails to compile, so we delete both in the same commit.

> **CORRECTION TO TASK 13:** If `dotnet build` in Task 13 Step 3 fails with errors in `Persistence/Invoices/IssuedInvoiceRepository.cs` (it will — that file's `using Anela.Heblo.Domain.Features.Invoices;` no longer resolves `IIssuedInvoiceRepository`/`IssuedInvoiceFilters`/`PaginatedResult`), proceed with Task 14 below **before** declaring Task 13 done. Combine the two deletions into one commit.

**Files:**
- Delete: `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`

- [ ] **Step 1: Confirm no remaining references to the Persistence concrete class**

Run: `grep -rn "Persistence\.Features\.Invoices" backend/`
Expected: at most matches inside `Persistence/Invoices/IssuedInvoiceConfiguration.cs` (unrelated — namespace prefix only). No production or test code outside Persistence should reference `Anela.Heblo.Persistence.Features.Invoices.IssuedInvoiceRepository` anymore.

Also check the EF Core configuration files don't depend on this class:
Run: `grep -nE "IssuedInvoiceRepository" backend/src/Anela.Heblo.Persistence/`
Expected: matches are only inside `Persistence/Invoices/IssuedInvoiceRepository.cs` itself.

- [ ] **Step 2: Delete the file**

Run: `rm backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`

- [ ] **Step 3: Verify the build passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds with zero new warnings.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build`
Expected: all tests pass.

- [ ] **Step 5: Combine Task 13 + Task 14 deletions into one commit**

If Task 13 Step 6 was already committed and the build was broken by the Persistence file at that point, amend the previous commit:

```bash
git add -u backend/src/Anela.Heblo.Persistence/Invoices/
git commit --amend --no-edit
```

(`--amend` is acceptable here because Task 13's commit is local-only and has not been pushed. If it was pushed, instead create a separate commit:)

```bash
git add -u backend/src/Anela.Heblo.Persistence/Invoices/
git commit -m "refactor: remove obsolete Persistence implementation of IssuedInvoiceRepository"
```

---

### Task 15: Verify behavior end-to-end

This is the global gate before adding the architectural guard test.

- [ ] **Step 1: Full build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds with zero warnings introduced by this change.

- [ ] **Step 2: Full test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: every test passes. Verify that no test was modified beyond `using` directives in this entire refactor.

- [ ] **Step 3: Run `dotnet format`**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no manual fixes needed; all auto-format adjustments are mechanical.

- [ ] **Step 4: NFR-3 grep gate — Domain must be free of UI/pagination vocabulary**

Run: `grep -rnwE "(PageNumber|PageSize|SortBy|TotalPages|HasNextPage|HasPreviousPage|ShowOnlyUnsynced|ShowOnlyWithErrors)" backend/src/Anela.Heblo.Domain/`
Expected: zero matches.

- [ ] **Step 5: Commit formatting fixes (if any)**

```bash
git status
git diff
# Only commit if dotnet format produced actual changes
git add -u backend/
git commit -m "chore: dotnet format after invoice contract relocation"
```

---

### Task 16: Add the NFR-6 architectural guard test

Adds a regression guard so that future changes cannot reintroduce the three relocated type names under `Anela.Heblo.Domain.Features.Invoices`, and cannot make any Domain type reference any Application type.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Write the failing test (new `[Fact]`)**

Append a new test method inside the existing `ModuleBoundariesTests` class, just before the closing brace at the end of the file. Insert the following content:

```csharp
    [Fact]
    public void Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone()
    {
        // NFR-6 from spec 2026-06-02: after relocating IssuedInvoiceFilters,
        // PaginatedResult<T>, and IIssuedInvoiceRepository out of Domain, the Domain
        // assembly must (a) not reference any Anela.Heblo.Application.* type, and
        // (b) not contain types with the three relocated names anywhere under
        // Anela.Heblo.Domain.Features.Invoices.
        const string DomainNamespacePrefix = "Anela.Heblo.Domain";
        const string ForbiddenPrefix = "Anela.Heblo.Application";
        var relocatedTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "IssuedInvoiceFilters",
            "PaginatedResult`1",
            "IIssuedInvoiceRepository",
        };

        var assembly = Assembly.Load("Anela.Heblo.Domain");
        var domainTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not null
                && t.Namespace.StartsWith(DomainNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        // (a) No Domain type references any Application type.
        var crossLayerViolations = new List<string>();
        foreach (var domainType in domainTypes)
        {
            foreach (var (referencedType, memberDescription) in EnumerateReferencedTypes(domainType))
            {
                if (referencedType.Namespace is null)
                    continue;

                if (!referencedType.Namespace.Equals(ForbiddenPrefix, StringComparison.Ordinal)
                    && !referencedType.Namespace.StartsWith(ForbiddenPrefix + ".", StringComparison.Ordinal))
                    continue;

                crossLayerViolations.Add($"{domainType.FullName} -> {referencedType.FullName} (via {memberDescription})");
            }
        }

        crossLayerViolations.Should().BeEmpty(
            "Domain layer must not reference Anela.Heblo.Application.* types. " +
            "Found:\n  " + string.Join("\n  ", crossLayerViolations));

        // (b) The three relocated type names must not exist anywhere under Domain.
        var orphanRelocations = domainTypes
            .Where(t => relocatedTypeNames.Contains(t.Name))
            .Select(t => t.FullName)
            .ToList();

        orphanRelocations.Should().BeEmpty(
            "Relocated types (IssuedInvoiceFilters, PaginatedResult<T>, IIssuedInvoiceRepository) " +
            "must not exist in Anela.Heblo.Domain after the 2026-06-02 relocation. " +
            "Found:\n  " + string.Join("\n  ", orphanRelocations));
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Domain_must_not_reference_Application_and_relocated_invoice_types_must_be_gone"`
Expected: 1 test passed.

- [ ] **Step 3: Sanity-check the test would have caught the pre-refactor state (manual reasoning, no code change)**

Before this refactor, `Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceFilters`, `Anela.Heblo.Domain.Features.Invoices.PaginatedResult\`1`, and `Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceRepository` all existed — part (b) would have failed with three entries. The Domain still does not reference Application — part (a) was passing both before and after; it is added as forward-protection. No further verification needed.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: add architectural guard for Domain/Application boundary and relocated invoice types"
```

---

### Task 17: Final verification gate

Final cumulative check matching the project's "Validation before completion" rule in `CLAUDE.md`.

- [ ] **Step 1: BE build is clean**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: 0 errors, no new warnings.

- [ ] **Step 2: All BE tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: all tests pass. None of the original test assertions were modified — only `using` directives.

- [ ] **Step 3: BE formatting clean**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: exit code 0.

- [ ] **Step 4: NFR-3 grep gate (re-run as a final defense)**

Run: `grep -rnwE "(PageNumber|PageSize|SortBy|TotalPages|HasNextPage|HasPreviousPage|ShowOnlyUnsynced|ShowOnlyWithErrors)" backend/src/Anela.Heblo.Domain/`
Expected: zero matches.

- [ ] **Step 5: Confirm no Domain.* fully-qualified leftovers**

Run: `grep -rnE "Anela\.Heblo\.Domain\.Features\.Invoices\.(IIssuedInvoiceRepository|IssuedInvoiceFilters|PaginatedResult)" backend/`
Expected: zero matches.

- [ ] **Step 6: Confirm no Persistence.Features.Invoices leftovers outside Persistence project**

Run: `grep -rn "Anela\.Heblo\.Persistence\.Features\.Invoices" backend/src/ backend/test/`
Expected: zero matches in `backend/src/Anela.Heblo.Application` and `backend/test/`. Matches inside `backend/src/Anela.Heblo.Persistence/Invoices/` for `IssuedInvoiceConfiguration` are allowed (entity configuration stays in Persistence; it is in a different namespace anyway).

- [ ] **Step 7: Frontend is unaffected — do not touch it**

The OpenAPI surface is byte-identical (no handler signature changed). No `npm run build` is required as part of this refactor. If the agent runs FE checks defensively, expect a no-op.

- [ ] **Step 8: E2E suite — not required for this change**

Per `arch-review.r1.md` "Prerequisites" section: this refactor does not affect the API surface, so Playwright E2E does not need to run. CLAUDE.md also notes E2E runs nightly, not in PR CI.

---

## Self-Review

**1. Spec coverage:**

| Spec / Arch-review item | Covered by |
|---|---|
| FR-1: Relocate `IssuedInvoiceFilters` to `Application.Features.Invoices.Contracts` | Task 2 |
| FR-2: Relocate `PaginatedResult<T>` to `Application.Shared` | Task 1 |
| FR-3: `IIssuedInvoiceRepository` signatures reference the new namespaces | Task 4 |
| FR-3a: Move interface to Application (Option A′ per arch review) | Task 4 |
| FR-3a (amendment): move implementation to Application/Features/Invoices/Infrastructure | Task 5 |
| FR-4: Update all call sites | Tasks 6, 7, 8, 9, 10, 11, 12 |
| FR-4 (amendment): include `InvoicesModule.cs` | Task 12 |
| FR-5: Preserve runtime behavior (existing tests pass unchanged) | Task 15 + Task 17 |
| FR-5 (amendment): extract `IssuedInvoiceSyncStats` into its own file | Task 3 |
| NFR-1: Performance — no impact | (no task needed — purely structural) |
| NFR-2: Security — no impact | (no task needed) |
| NFR-3: Domain free of pagination/sort/UI vocabulary — grep gate | Task 15 Step 4, Task 17 Step 4 |
| NFR-4: Tests unchanged beyond `using` updates | Task 11 + Task 15 Step 2 |
| NFR-5: Code style preserved | Task 15 Step 3 + Task 17 Step 3 |
| NFR-6: Architectural guard test | Task 16 |
| Risk: implementer leaves impl in Persistence (per spec original FR-3a) | Task 5 explicitly moves impl |
| Risk: missed `using` somewhere in test project | Task 11 + Task 17 Step 5/6 grep gates |
| Risk: `IssuedInvoiceSyncStats` split breaks unusual reference | Task 3 keeps namespace identical |
| Risk: EF cached migration metadata | (no task needed — entity config unchanged, no migration needed) |
| Risk: other modules import transitively | Task 17 Step 5 grep gate |
| Risk: Frontend regen | (no task needed — no API surface change) |

All FRs and NFRs covered. All identified risks have explicit mitigations.

**2. Placeholder scan:** none found. Every code block contains complete content. Every command is exact. Every expected output is stated.

**3. Type consistency:**
- `PaginatedResult<T>` properties are identical across Tasks 1, 4, 5 (matches the source we copied from in `Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceRepository.cs:120-128`).
- `IssuedInvoiceFilters` properties are identical across Tasks 2, 4, 5 (matches `Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceRepository.cs:101-114`).
- `IIssuedInvoiceRepository` method names and signatures across Tasks 4 and 5 are byte-equivalent to the source at `Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceRepository.cs:9-82`.
- `IssuedInvoiceSyncStats` properties (Task 3) are byte-equivalent to the source at `Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceRepository.cs:87-96`.

Note on `PaginatedResult\`1`: in Task 16 the relocated-type-names hash set uses the runtime CLR name `PaginatedResult\`1` (with backtick) because `Type.Name` for a generic returns the arity-suffixed form. This is intentional and correct for reflection lookup.

**4. Coordination caveats explicitly captured:**
- The "additive then migrate then delete" sequence is preserved through Tasks 1–14.
- Tasks 13 and 14 may need to be a single commit (the build is briefly broken between deleting the Domain interface and deleting the Persistence implementation) — Task 14 explicitly captures this via the `--amend` instruction.
- Tasks 10 and 11 include fallback instructions for `CS0104` ambiguous-reference errors, with explicit revert instructions in Task 13 Step 4.
