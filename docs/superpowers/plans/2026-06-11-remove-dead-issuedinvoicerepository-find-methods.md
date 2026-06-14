# Remove Dead Query Methods from `IIssuedInvoiceRepository` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove five unused `Find*` query methods from `IIssuedInvoiceRepository`, their EF Core implementations, and the seven unit tests that exercise them — narrowing the contract to the four members production code actually uses.

**Architecture:** Pure deletion across three files in the Invoices module: the contract (`IIssuedInvoiceRepository.cs`), the EF Core implementation (`IssuedInvoiceRepository.cs`), and the unit test class (`IssuedInvoiceRepositoryTests.cs`). No new components, no DI changes, no controller/contract changes. The change is one self-contained commit and fully reversible by `git revert`.

**Tech Stack:** .NET 8 / C# 12, EF Core 8 (`Microsoft.EntityFrameworkCore`), xUnit 2.x with `Microsoft.EntityFrameworkCore.InMemory`, Moq.

---

## File Map

| File | Action | What changes |
|------|--------|--------------|
| `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs` | Modify | Delete 5 method declarations + their XML doc comments. Retain `GetByIdWithSyncHistoryAsync`, `GetSyncStatsAsync`, `GetPaginatedAsync`, `GetHeadersByDateAsync`. |
| `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` | Modify | Delete 5 method bodies (lines 37–88). Retain `GetByIdAsync`, `GetByIdWithSyncHistoryAsync`, `GetSyncStatsAsync`, `AddAsync`, `UpdateAsync`, `GetPaginatedAsync`, `GetHeadersByDateAsync`, `ApplySorting`. |
| `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` | Modify | Delete 7 `[Fact]` test methods covering removed APIs + private helper `SetLastSyncTime` (no remaining caller). Retain `CreateTestSyncData` (still used by retained tests). |

**Removal order matters for compilation:**
1. Tests first (no production-code coupling; intermediate state stays green).
2. Interface + implementation **together** in one task (deleting either alone would either leave dead public methods or break the `IIssuedInvoiceRepository` interface contract). Both edits land in the same task and are validated as a unit before commit.

---

## Task 1: Delete obsolete unit tests and the now-orphan helper

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`

**Removals:**
- `FindBySyncStatusAsync_WithSyncedFilter_ReturnsOnlySynced` (lines 119–142)
- `FindBySyncStatusAsync_WithNullFilter_ReturnsAll` (lines 144–164)
- `FindByInvoiceDateRangeAsync_WithDateRange_ReturnsFilteredInvoices` (lines 166–187)
- `FindWithCriticalErrorsAsync_WithErrorTypes_ReturnsOnlyCriticalErrors` (lines 189–214)
- `FindStaleInvoicesAsync_WithStaleInvoices_ReturnsUnsyncedAndOldSynced` (lines 216–243)
- `FindByCustomerNameAsync_WithPartialName_ReturnsMatchingInvoices` (lines 285–309)
- `FindByCustomerNameAsync_WithEmptyName_ReturnsEmpty` (lines 311–326)
- Private helper `SetLastSyncTime` (lines 458–462) — used only inside `FindStaleInvoicesAsync_…`

**Retain:**
- All other `[Fact]` methods, including the two `GetByIdAsync_…`, `GetByIdWithSyncHistoryAsync_…`, `GetSyncStatsAsync_…`, three `GetPaginatedAsync_…`, `AddAsync_…`, `UpdateAsync_…`.
- The `CreateTestSyncData` helper (lines 445–456) — still called from `GetByIdWithSyncHistoryAsync_…`, `GetSyncStatsAsync_…`, and three `GetPaginatedAsync_…` tests.
- The constructor, `Dispose`, and all fields.

---

- [ ] **Step 1: Open the test file and confirm baseline**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryTests" --no-restore`
Expected: All tests green (this is the pre-change baseline — captures the current passing set so the delta after deletion is purely "fewer tests, same green").

- [ ] **Step 2: Delete `FindBySyncStatusAsync_WithSyncedFilter_ReturnsOnlySynced`**

In `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`, remove the exact block (including the `[Fact]` attribute on line 119, the blank line above it, and trailing blank line up to but not including the next `[Fact]`):

```csharp
    [Fact]
    public async Task FindBySyncStatusAsync_WithSyncedFilter_ReturnsOnlySynced()
    {
        // Arrange
        var syncedInvoice = new IssuedInvoice { Id = "INV-SYNCED", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        syncedInvoice.SyncSucceeded(CreateTestSyncData());

        var unsyncedInvoice = new IssuedInvoice { Id = "INV-UNSYNCED", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };

        await _repository.AddAsync(syncedInvoice);
        await _repository.AddAsync(unsyncedInvoice);
        await _repository.SaveChangesAsync();

        // Act
        var syncedResult = await _repository.FindBySyncStatusAsync(true);
        var unsyncedResult = await _repository.FindBySyncStatusAsync(false);

        // Assert
        Assert.Single(syncedResult);
        Assert.Equal("INV-SYNCED", syncedResult.First().Id);

        Assert.Single(unsyncedResult);
        Assert.Equal("INV-UNSYNCED", unsyncedResult.First().Id);
    }
```

- [ ] **Step 3: Delete `FindBySyncStatusAsync_WithNullFilter_ReturnsAll`**

Remove the exact block:

```csharp
    [Fact]
    public async Task FindBySyncStatusAsync_WithNullFilter_ReturnsAll()
    {
        // Arrange
        var syncedInvoice = new IssuedInvoice { Id = "INV-SYNCED", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        syncedInvoice.SyncSucceeded(CreateTestSyncData());

        var unsyncedInvoice = new IssuedInvoice { Id = "INV-UNSYNCED", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };

        await _repository.AddAsync(syncedInvoice);
        await _repository.AddAsync(unsyncedInvoice);
        await _repository.SaveChangesAsync();

        // Act
        var result = await _repository.FindBySyncStatusAsync(null);

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains(result, i => i.Id == "INV-SYNCED");
        Assert.Contains(result, i => i.Id == "INV-UNSYNCED");
    }
```

- [ ] **Step 4: Delete `FindByInvoiceDateRangeAsync_WithDateRange_ReturnsFilteredInvoices`**

Remove the exact block:

```csharp
    [Fact]
    public async Task FindByInvoiceDateRangeAsync_WithDateRange_ReturnsFilteredInvoices()
    {
        // Arrange
        var invoice1 = new IssuedInvoice { Id = "INV-OLD", InvoiceDate = DateTime.Today.AddDays(-10), DueDate = DateTime.Today.AddDays(20), TaxDate = DateTime.Today.AddDays(-10) };
        var invoice2 = new IssuedInvoice { Id = "INV-RECENT", InvoiceDate = DateTime.Today.AddDays(-2), DueDate = DateTime.Today.AddDays(28), TaxDate = DateTime.Today.AddDays(-2) };
        var invoice3 = new IssuedInvoice { Id = "INV-FUTURE", InvoiceDate = DateTime.Today.AddDays(5), DueDate = DateTime.Today.AddDays(35), TaxDate = DateTime.Today.AddDays(5) };

        await _repository.AddAsync(invoice1);
        await _repository.AddAsync(invoice2);
        await _repository.AddAsync(invoice3);
        await _repository.SaveChangesAsync();

        // Act
        var result = await _repository.FindByInvoiceDateRangeAsync(
            DateTime.Today.AddDays(-5),
            DateTime.Today);

        // Assert
        Assert.Single(result);
        Assert.Equal("INV-RECENT", result.First().Id);
    }
```

- [ ] **Step 5: Delete `FindWithCriticalErrorsAsync_WithErrorTypes_ReturnsOnlyCriticalErrors`**

Remove the exact block:

```csharp
    [Fact]
    public async Task FindWithCriticalErrorsAsync_WithErrorTypes_ReturnsOnlyCriticalErrors()
    {
        // Arrange
        var criticalErrorInvoice = new IssuedInvoice { Id = "INV-CRITICAL", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        criticalErrorInvoice.SyncFailed(CreateTestSyncData(), "Critical error");

        var pairedInvoice = new IssuedInvoice { Id = "INV-PAIRED", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        pairedInvoice.SyncFailed(CreateTestSyncData(), new IssuedInvoiceError { ErrorType = IssuedInvoiceErrorType.InvoicePaired, Message = "Invoice paired" }); // Not critical

        var successInvoice = new IssuedInvoice { Id = "INV-SUCCESS", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        successInvoice.SyncSucceeded(CreateTestSyncData());

        await _repository.AddAsync(criticalErrorInvoice);
        await _repository.AddAsync(pairedInvoice);
        await _repository.AddAsync(successInvoice);
        await _repository.SaveChangesAsync();

        // Act
        var result = await _repository.FindWithCriticalErrorsAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("INV-CRITICAL", result.First().Id);
        // Should not include paired invoice (not critical) or success invoice
    }
```

- [ ] **Step 6: Delete `FindStaleInvoicesAsync_WithStaleInvoices_ReturnsUnsyncedAndOldSynced`**

Remove the exact block:

```csharp
    [Fact]
    public async Task FindStaleInvoicesAsync_WithStaleInvoices_ReturnsUnsyncedAndOldSynced()
    {
        // Arrange
        var unsyncedInvoice = new IssuedInvoice { Id = "INV-UNSYNCED", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };

        var oldSyncedInvoice = new IssuedInvoice { Id = "INV-OLD-SYNC", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        oldSyncedInvoice.SyncSucceeded(CreateTestSyncData());
        SetLastSyncTime(oldSyncedInvoice, DateTime.UtcNow.AddDays(-10));

        var recentSyncedInvoice = new IssuedInvoice { Id = "INV-RECENT-SYNC", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        recentSyncedInvoice.SyncSucceeded(CreateTestSyncData());
        SetLastSyncTime(recentSyncedInvoice, DateTime.UtcNow.AddHours(-1));

        await _repository.AddAsync(unsyncedInvoice);
        await _repository.AddAsync(oldSyncedInvoice);
        await _repository.AddAsync(recentSyncedInvoice);
        await _repository.SaveChangesAsync();

        // Act
        var result = await _repository.FindStaleInvoicesAsync(DateTime.UtcNow.AddDays(-5));

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains(result, i => i.Id == "INV-UNSYNCED");
        Assert.Contains(result, i => i.Id == "INV-OLD-SYNC");
        Assert.DoesNotContain(result, i => i.Id == "INV-RECENT-SYNC");
    }
```

- [ ] **Step 7: Delete `FindByCustomerNameAsync_WithPartialName_ReturnsMatchingInvoices`**

Remove the exact block:

```csharp
    [Fact]
    public async Task FindByCustomerNameAsync_WithPartialName_ReturnsMatchingInvoices()
    {
        // Arrange
        var invoice1 = new IssuedInvoice { Id = "INV-001", CustomerName = "ACME Corporation", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        var invoice2 = new IssuedInvoice { Id = "INV-002", CustomerName = "Beta Corp", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        var invoice3 = new IssuedInvoice { Id = "INV-003", CustomerName = "ACME Industries", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        var invoice4 = new IssuedInvoice { Id = "INV-004", CustomerName = null, InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };

        await _repository.AddAsync(invoice1);
        await _repository.AddAsync(invoice2);
        await _repository.AddAsync(invoice3);
        await _repository.AddAsync(invoice4);
        await _repository.SaveChangesAsync();

        // Act
        var result = await _repository.FindByCustomerNameAsync("ACME");

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains(result, i => i.Id == "INV-001");
        Assert.Contains(result, i => i.Id == "INV-003");
        Assert.DoesNotContain(result, i => i.Id == "INV-002");
        Assert.DoesNotContain(result, i => i.Id == "INV-004");
    }
```

- [ ] **Step 8: Delete `FindByCustomerNameAsync_WithEmptyName_ReturnsEmpty`**

Remove the exact block:

```csharp
    [Fact]
    public async Task FindByCustomerNameAsync_WithEmptyName_ReturnsEmpty()
    {
        // Arrange
        var invoice = new IssuedInvoice { Id = "INV-001", CustomerName = "Test Customer", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };
        await _repository.AddAsync(invoice);
        await _repository.SaveChangesAsync();

        // Act
        var result1 = await _repository.FindByCustomerNameAsync("");
        var result2 = await _repository.FindByCustomerNameAsync("   ");

        // Assert
        Assert.Empty(result1);
        Assert.Empty(result2);
    }
```

- [ ] **Step 9: Delete the now-orphan `SetLastSyncTime` private helper**

The reflection-based helper at lines 458–462 was used **only** by `FindStaleInvoicesAsync_…` (now deleted in Step 6). Remove the exact block:

```csharp
    private static void SetLastSyncTime(IssuedInvoice invoice, DateTime syncTime)
    {
        var property = typeof(IssuedInvoice).GetProperty("LastSyncTime");
        property?.SetValue(invoice, syncTime);
    }
```

`CreateTestSyncData` must remain untouched — it is still referenced by retained tests at lines 103, 254, 259, 262, 368, 397, 400 of the original file.

- [ ] **Step 10: Verify no leftover references to the deleted helper or to the deleted method names exist in the test file**

Run (from repo root):

```bash
grep -nE "SetLastSyncTime|FindBySyncStatusAsync|FindByInvoiceDateRangeAsync|FindByCustomerNameAsync|FindWithCriticalErrorsAsync|FindStaleInvoicesAsync" \
  backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs
```

Expected output: empty (no lines, exit code 1 from grep is fine here).

- [ ] **Step 11: Build the test project to confirm it still compiles (interface methods still exist; production code unchanged)**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 12: Run the remaining tests in `IssuedInvoiceRepositoryTests` to confirm green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryTests" --no-build`
Expected: All retained tests pass (`AddAsync_…`, `UpdateAsync_…`, two `GetByIdAsync_…`, `GetByIdWithSyncHistoryAsync_…`, `GetSyncStatsAsync_…`, three `GetPaginatedAsync_…`). Total count is 7 fewer than the baseline in Step 1.

---

## Task 2: Delete the five interface method declarations and their implementations together

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs`

**Why these two edits must land in the same task:** if the interface methods are removed without removing the implementation, the implementation simply has extra public methods (compiles but is ugly intermediate state and creates an unstable in-between commit). If the implementation methods are removed without removing the interface methods, `IssuedInvoiceRepository` no longer satisfies `IIssuedInvoiceRepository` and the solution will not compile. They must be deleted as one unit before the build/test gate runs.

---

- [ ] **Step 1: Remove the five method declarations + their XML doc comments from `IIssuedInvoiceRepository.cs`**

In `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`, remove the following five blocks (each is an XML doc comment block followed by the method declaration and its trailing blank line):

```csharp
    /// <summary>
    /// Finds invoices by their synchronization status
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> FindBySyncStatusAsync(bool? isSynced, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices within a date range
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> FindByInvoiceDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices by customer name (partial match)
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> FindByCustomerNameAsync(string customerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds invoices with sync errors (excluding invoice paired errors)
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> FindWithCriticalErrorsAsync(CancellationToken cancellationToken = default);
```

and the fifth, located between `GetByIdWithSyncHistoryAsync` and `GetSyncStatsAsync`:

```csharp
    /// <summary>
    /// Gets invoices that were last synced before a specific date
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> FindStaleInvoicesAsync(DateTime beforeDate, CancellationToken cancellationToken = default);

```

After this step, the resulting interface body must be exactly:

```csharp
public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    /// <summary>
    /// Gets an invoice with its complete sync history
    /// </summary>
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sync statistics for a date range
    /// </summary>
    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated and filtered list of issued invoices with sorting
    /// </summary>
    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets invoice headers for a specific date
    /// </summary>
    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}
```

The `using` directives at the top of the file (`Anela.Heblo.Application.Shared`, `Anela.Heblo.Domain.Features.Invoices`, `Anela.Heblo.Xcc.Persistance`) and the file-scoped namespace declaration are still required by the retained members; leave them untouched.

- [ ] **Step 2: Remove the five implementation method bodies from `IssuedInvoiceRepository.cs`**

In `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs`, remove lines 37–88 (the five consecutive method bodies for `FindBySyncStatusAsync`, `FindByInvoiceDateRangeAsync`, `FindByCustomerNameAsync`, `FindWithCriticalErrorsAsync`, `FindStaleInvoicesAsync`):

```csharp
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

```

After this step, the structure of `IssuedInvoiceRepository.cs` between `GetByIdWithSyncHistoryAsync` (which retains its body at the original lines 30–35) and `GetSyncStatsAsync` (original lines 90–113) should be a single blank line separator — no `Find*` methods between them.

`using` directives are still all required: `Microsoft.EntityFrameworkCore` (still used by `Include`, `FirstOrDefaultAsync`, `ToListAsync`, `EF.Functions.ILike`, `CountAsync`, `MaxAsync`), `Microsoft.Extensions.Logging` (still used by `_logger.LogInformation`), the four project namespaces (Application.Features.Invoices.Contracts, Application.Shared, Domain.Features.Invoices, Persistence, Persistence.Repositories) are all still referenced by retained members. Do not edit the `using` block.

- [ ] **Step 3: Verify no source file outside the three target files references any of the five removed method names**

Run (from repo root):

```bash
grep -rnE "FindBySyncStatusAsync|FindByInvoiceDateRangeAsync|FindByCustomerNameAsync|FindWithCriticalErrorsAsync|FindStaleInvoicesAsync" backend/
```

Expected output: empty (exit 1 from grep). If any line appears in a file other than the two now-edited source files (which should not contain them anymore either), stop and investigate — the spec asserts zero non-test references and Task 1 already removed the test references.

- [ ] **Step 4: Build the entire backend solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. In particular, no `CS0535` "does not implement interface member" errors (which would mean the interface and implementation got out of sync), and no new `CS86xx` nullable warnings.

- [ ] **Step 5: Verify formatting is clean**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: exit code 0, no output about files that would be changed. If it reports drift, run `dotnet format backend/Anela.Heblo.sln` and re-run with `--verify-no-changes` to confirm. Per NFR-4 (Surgical diff), only the three target files should appear in any formatting changes.

- [ ] **Step 6: Run the full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln --no-build`
Expected: all tests pass; total count is exactly 7 fewer than the pre-change baseline (the seven tests deleted in Task 1). Architecture tests (`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`) — which assert placement of the `IIssuedInvoiceRepository` type, not its members — remain green.

---

## Task 3: Final verification and commit

**Files:** none (verification + commit only).

---

- [ ] **Step 1: Confirm the diff touches exactly three files**

Run: `git status --short`
Expected (file order may vary):

```
 M backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs
 M backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs
 M backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs
```

No untracked files. No other modified files. If anything else appears (e.g. an auto-regenerated OpenAPI client or a `dotnet format` cleanup of an unrelated file), revert those changes — the surgical-diff requirement (FR-4, NFR-4) forbids them.

- [ ] **Step 2: Inspect the diff itself for unintended edits**

Run: `git diff`
Expected: only deletions in the three files listed above. No additions other than what fall naturally out of removing lines (e.g. zero added lines, or only whitespace-only adjustments inside the interface body where the deleted blocks used to be). No edits to retained methods, `using` directives, namespace, class declaration, or formatting of unrelated code.

- [ ] **Step 3: Re-run the validation gates one final time**

Run, in this order:

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: each command exits 0; build reports `0 Warning(s) 0 Error(s)`; format reports nothing to change; tests all pass with the count exactly 7 lower than baseline.

- [ ] **Step 4: Stage and commit**

Run:

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs
git commit -m "refactor: remove unused Find* methods from IIssuedInvoiceRepository

The five Find* query methods (FindBySyncStatusAsync, FindByInvoiceDateRangeAsync,
FindByCustomerNameAsync, FindWithCriticalErrorsAsync, FindStaleInvoicesAsync) had
no production callers — all live filtering goes through GetPaginatedAsync. Drop
them from the interface, the EF Core implementation, and the unit tests that
exercised them. Also remove the now-orphan SetLastSyncTime test helper used only
by the deleted FindStaleInvoicesAsync test. YAGNI/ISP cleanup; reversible by
git revert."
```

Expected: commit created, hook-driven checks (if any) pass. If a pre-commit hook fails, fix the underlying issue and create a new commit (do not amend).

- [ ] **Step 5: Confirm clean working tree**

Run: `git status`
Expected: `nothing to commit, working tree clean`. The branch now contains a single self-contained commit implementing the spec.

---

## Self-Review

**Spec coverage:**
- FR-1 (remove 5 interface declarations + doc comments) — Task 2 / Step 1.
- FR-2 (remove 5 implementation bodies, keep `using` directives that remain in use) — Task 2 / Step 2 and Step 5 (`dotnet format` gate).
- FR-3 (remove 7 named test methods + orphan helper) — Task 1 / Steps 2–9.
- FR-4 (no other files touched, no OpenAPI/client regeneration, build + format clean, tests pass) — Task 3 / Steps 1–3.
- NFR-1 (build & analysis cleanliness) — Task 2 / Steps 4–5 and Task 3 / Step 3.
- NFR-2 (test stability, count drops by exactly 7) — Task 1 / Step 12, Task 2 / Step 6, Task 3 / Step 3.
- NFR-3 (reversibility) — Single commit in Task 3 / Step 4; `git revert` restores all three files atomically.
- NFR-4 (surgical diff to exactly three files) — Task 3 / Steps 1–2 enforce this explicitly.
- Arch-review amendment 1 (delete `SetLastSyncTime` helper) — Task 1 / Step 9.
- Arch-review amendment 2 (no other files in repo may show modifications) — Task 3 / Step 1.

**Placeholder scan:** No TBD / TODO / "appropriate error handling" / "similar to Task N" markers. All code blocks shown literally. All file paths absolute from repo root.

**Type & name consistency:** All five removed method names and the helper name `SetLastSyncTime` are spelled identically every time they appear (interface declaration, implementation body, test usages, grep verifications, commit message). All retained method names (`GetByIdWithSyncHistoryAsync`, `GetSyncStatsAsync`, `GetPaginatedAsync`, `GetHeadersByDateAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `ApplySorting`, `CreateTestSyncData`) likewise spelled identically across tasks.
