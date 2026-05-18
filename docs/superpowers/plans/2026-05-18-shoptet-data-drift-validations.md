# Shoptet ↔ Heblo Data-Drift Validations — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the DQT framework with two new validation routines — product-pairing drift and stock write-back reconciliation — that discover and report mismatches on the existing `/data-quality` UI without auto-remediation.

**Architecture:** Two new `DqtTestType` values share one generic result table (`DqtDriftResult`). A generic `DriftDqtJobRunner` dispatches by test type to the appropriate `IDriftDqtComparer` implementation. The existing invoice DQT code is left completely untouched. Each new check gets its own Hangfire recurring job auto-discovered via reflection (same mechanism as `InvoiceDqtJob`).

**Tech Stack:** .NET 8, EF Core 8 (PostgreSQL + snake_case naming), Hangfire, MediatR, AutoMapper, Moq, FluentAssertions, React 18 + TypeScript, Vite

---

## File Structure

### New files to create

| File | Responsibility |
|------|---------------|
| `...Domain/DataQuality/DqtDriftResult.cs` | Generic result entity shared by both new checks |
| `...Domain/DataQuality/ProductPairingMismatch.cs` | `[Flags]` enum for product-pairing mismatch codes |
| `...Domain/DataQuality/StockWriteBackMismatch.cs` | `[Flags]` enum for write-back mismatch codes |
| `...Persistence/DataQuality/DqtDriftResultConfiguration.cs` | EF Core table/index mapping for `DqtDriftResult` |
| `...Application/DataQuality/Services/IDriftDqtComparer.cs` | Plug-in comparer contract |
| `...Application/DataQuality/Services/DriftComparisonResult.cs` | Result DTO returned by comparers (includes `DriftMismatch`) |
| `...Application/DataQuality/Services/IDriftDqtJobRunner.cs` | Generic runner contract |
| `...Application/DataQuality/Services/DriftDqtJobRunner.cs` | Picks comparer by `TestType`, persists results, completes run |
| `...Application/DataQuality/Services/ProductPairingDqtComparer.cs` | Check A: Shoptet ↔ ERP product-pairing snapshot |
| `...Application/DataQuality/Services/StockWriteBackDqtComparer.cs` | Check B: failed/stuck write-back operations |
| `...Application/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs` | Hangfire daily snapshot job |
| `...Application/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs` | Hangfire daily write-back job |
| `...Contracts/DataQuality/DqtDriftResultDto.cs` | API response DTO |
| `...Tests/DataQuality/DriftDqtJobRunnerTests.cs` | Unit tests for the runner |
| `...Tests/DataQuality/ProductPairingDqtComparerTests.cs` | Unit tests for comparer A |
| `...Tests/DataQuality/StockWriteBackDqtComparerTests.cs` | Unit tests for comparer B |
| EF migration (generated) | Adds `dqt_drift_results` table |

### Files to modify

| File | Change |
|------|--------|
| `...Domain/DataQuality/DqtTestType.cs` | Add `ProductPairing = 2`, `StockWriteBackReconciliation = 3` |
| `...Domain/DataQuality/IDqtRunRepository.cs` | Add `AddDriftResultsAsync`, `GetDriftResultsAsync` |
| `...Domain/Catalog/Stock/IStockTakingRepository.cs` | Add `GetByDateRangeAsync` |
| `...Persistence/ApplicationDbContext.cs` | Add `DbSet<DqtDriftResult>` |
| `...Persistence/DataQuality/DqtRunRepository.cs` | Implement two new repository methods |
| `...Persistence/...StockTakingRepository.cs` | Implement `GetByDateRangeAsync` |
| `...Application/DataQuality/DataQualityModule.cs` | Register three new services |
| `...Application/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` | Dispatch by `TestType` |
| `...Application/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs` | Branch results by `TestType` |
| `...Application/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailResponse.cs` | Add `DriftResults` + `TotalDriftResults` |
| `...Application/DataQuality/DataQualityMappingProfile.cs` | Add `DqtDriftResult → DqtDriftResultDto` mapping |
| `frontend/src/components/data-quality/DqtRunsTable.tsx` | Add labels for two new test types |
| `frontend/src/components/data-quality/DqtRunDetail.tsx` | Render drift-result rows when present |
| `frontend/src/components/data-quality/RunDqtButton.tsx` | Add test-type selector |
| `frontend/src/i18n.ts` | Add i18n strings for new types and mismatch codes |

---

## Task 1: Domain — Add new DqtTestType values

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtTestType.cs`

- [ ] **Step 1: Read the current file**

```
backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtTestType.cs
```

Current content:
```csharp
namespace Anela.Heblo.Domain.Features.DataQuality;

public enum DqtTestType
{
    IssuedInvoiceComparison = 1
}
```

- [ ] **Step 2: Add the two new values**

```csharp
namespace Anela.Heblo.Domain.Features.DataQuality;

public enum DqtTestType
{
    IssuedInvoiceComparison = 1,
    ProductPairing = 2,
    StockWriteBackReconciliation = 3
}
```

- [ ] **Step 3: Verify build**

```bash
cd backend && dotnet build --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtTestType.cs
git commit -m "feat(data-quality): add ProductPairing and StockWriteBackReconciliation test types"
```

---

## Task 2: Domain — DqtDriftResult entity and mismatch enums

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/DataQuality/ProductPairingMismatch.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/DataQuality/StockWriteBackMismatch.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtDriftResult.cs`

- [ ] **Step 1: Create `ProductPairingMismatch.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.DataQuality;

[Flags]
public enum ProductPairingMismatch
{
    None = 0,
    MissingInErp = 1,
    MissingInShoptet = 2,
    PairCodeUnresolved = 4
}
```

- [ ] **Step 2: Create `StockWriteBackMismatch.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.DataQuality;

[Flags]
public enum StockWriteBackMismatch
{
    None = 0,
    OperationFailed = 1,
    OperationStuck = 2,
    StockTakingErrored = 4
}
```

- [ ] **Step 3: Create `DqtDriftResult.cs`**

First, open `InvoiceDqtResult.cs` to confirm the `Entity<Guid>` base class and its namespace (should be `Anela.Heblo.Xcc.Domain`). Then create:

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.DataQuality;

public class DqtDriftResult : Entity<Guid>
{
    public Guid DqtRunId { get; private set; }
    public DqtTestType TestType { get; private set; }
    public string EntityKey { get; private set; } = string.Empty;
    public int MismatchCode { get; private set; }
    public string? HebloValue { get; private set; }
    public string? ShoptetValue { get; private set; }
    public string? Details { get; private set; }

    private DqtDriftResult() { }

    public static DqtDriftResult Create(
        Guid dqtRunId,
        DqtTestType testType,
        string entityKey,
        int mismatchCode,
        string? hebloValue,
        string? shoptetValue,
        string? details)
    {
        return new DqtDriftResult
        {
            Id = Guid.NewGuid(),
            DqtRunId = dqtRunId,
            TestType = testType,
            EntityKey = entityKey,
            MismatchCode = mismatchCode,
            HebloValue = hebloValue,
            ShoptetValue = shoptetValue,
            Details = details
        };
    }
}
```

- [ ] **Step 4: Verify build**

```bash
cd backend && dotnet build --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtDriftResult.cs \
        backend/src/Anela.Heblo.Domain/Features/DataQuality/ProductPairingMismatch.cs \
        backend/src/Anela.Heblo.Domain/Features/DataQuality/StockWriteBackMismatch.cs
git commit -m "feat(data-quality): add DqtDriftResult entity and mismatch enums"
```

---

## Task 3: Domain — Extend IDqtRunRepository and IStockTakingRepository

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IStockTakingRepository.cs`

- [ ] **Step 1: Add two new methods to `IDqtRunRepository.cs`**

Open the file, find the interface body, and append inside the interface:

```csharp
Task AddDriftResultsAsync(IEnumerable<DqtDriftResult> results, CancellationToken ct = default);
Task<(List<DqtDriftResult> Items, int TotalCount)> GetDriftResultsAsync(
    Guid runId, int page, int pageSize, CancellationToken ct = default);
```

Add the required using if missing: `using System.Collections.Generic;` (may already be there).

- [ ] **Step 2: Add `GetByDateRangeAsync` to `IStockTakingRepository.cs`**

Current:
```csharp
public interface IStockTakingRepository : IRepository<StockTakingRecord, int>
{
}
```

New:
```csharp
public interface IStockTakingRepository : IRepository<StockTakingRecord, int>
{
    Task<List<StockTakingRecord>> GetByDateRangeAsync(
        DateTime from, DateTime to, CancellationToken ct = default);
}
```

- [ ] **Step 3: Verify — expect compile errors on unimplemented interface members in Persistence**

```bash
cd backend && dotnet build --no-restore 2>&1 | grep "error CS" | head -10
```

Expected: errors about `DqtRunRepository` and `StockTakingRepository` not implementing new methods. This is correct.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs \
        backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IStockTakingRepository.cs
git commit -m "feat(data-quality): extend IDqtRunRepository and IStockTakingRepository interfaces"
```

---

## Task 4: Persistence — EF configuration and DbContext

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/DataQuality/DqtDriftResultConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Open `DqtRunConfiguration.cs` to verify naming conventions**

```bash
cat backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunConfiguration.cs
```

Confirm: table name format (`"dqt_runs"` snake_case), schema if any, namespace (`Anela.Heblo.Persistence.DataQuality`).

- [ ] **Step 2: Create `DqtDriftResultConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.DataQuality;

public class DqtDriftResultConfiguration : IEntityTypeConfiguration<DqtDriftResult>
{
    public void Configure(EntityTypeBuilder<DqtDriftResult> builder)
    {
        builder.ToTable("dqt_drift_results");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TestType).HasConversion<int>();
        builder.Property(x => x.EntityKey).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(4000);
        builder.HasIndex(x => x.DqtRunId);
        builder.HasIndex(x => new { x.TestType, x.EntityKey });
    }
}
```

- [ ] **Step 3: Add `DbSet<DqtDriftResult>` to `ApplicationDbContext.cs`**

Open the file, find the DataQuality DbSet section (near `DqtRuns` and `InvoiceDqtResults`), and add:

```csharp
public DbSet<DqtDriftResult> DqtDriftResults { get; set; } = null!;
```

Ensure `using Anela.Heblo.Domain.Features.DataQuality;` is present.

- [ ] **Step 4: Verify build — still errors from Repository unimplemented methods**

```bash
cd backend && dotnet build --no-restore 2>&1 | grep "error CS" | head -10
```

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/DataQuality/DqtDriftResultConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat(data-quality): add EF configuration and DbSet for DqtDriftResult"
```

---

## Task 5: Persistence — Implement new repository methods

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunRepository.cs`
- Modify: `StockTakingRepository.cs` (find with: `grep -rl "IStockTakingRepository" backend/src/Anela.Heblo.Persistence/`)

- [ ] **Step 1: Add `AddDriftResultsAsync` to `DqtRunRepository.cs`**

Open the file and find the existing `AddResultsAsync` (for invoice results) — add the new method immediately after it, following the same pattern:

```csharp
public async Task AddDriftResultsAsync(IEnumerable<DqtDriftResult> results, CancellationToken ct = default)
{
    await Context.Set<DqtDriftResult>().AddRangeAsync(results, ct);
}
```

- [ ] **Step 2: Add `GetDriftResultsAsync` to `DqtRunRepository.cs`**

Add after `AddDriftResultsAsync`:

```csharp
public async Task<(List<DqtDriftResult> Items, int TotalCount)> GetDriftResultsAsync(
    Guid runId, int page, int pageSize, CancellationToken ct = default)
{
    var query = Context.Set<DqtDriftResult>()
        .Where(r => r.DqtRunId == runId)
        .OrderBy(r => r.EntityKey);

    var totalCount = await query.CountAsync(ct);
    var items = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(ct);

    return (items, totalCount);
}
```

Add `using Microsoft.EntityFrameworkCore;` if not already present.

- [ ] **Step 3: Find the `StockTakingRepository` and implement `GetByDateRangeAsync`**

```bash
grep -rl "IStockTakingRepository" backend/src/Anela.Heblo.Persistence/ --include="*.cs"
```

Open the found file and add:

```csharp
public async Task<List<StockTakingRecord>> GetByDateRangeAsync(
    DateTime from, DateTime to, CancellationToken ct = default)
{
    return await Context.Set<StockTakingRecord>()
        .Where(r => r.Date >= from && r.Date <= to)
        .ToListAsync(ct);
}
```

Ensure `using Microsoft.EntityFrameworkCore;` and `using Anela.Heblo.Domain.Features.Catalog.Stock;` are present.

- [ ] **Step 4: Verify build succeeds**

```bash
cd backend && dotnet build --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
# Add DqtRunRepository and the StockTakingRepository file you found
git add backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunRepository.cs
# Also add the StockTakingRepository file path from step 3
git commit -m "feat(data-quality): implement AddDriftResultsAsync, GetDriftResultsAsync, GetByDateRangeAsync"
```

---

## Task 6: Persistence — EF migration

**Files:**
- Create: new EF migration (generated)

- [ ] **Step 1: Generate the migration**

```bash
cd backend && dotnet ef migrations add AddDqtDriftResults \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Expected: Creates `Migrations/YYYYMMDD_AddDqtDriftResults.cs` and updates the snapshot.

- [ ] **Step 2: Verify the generated `Up()` method**

Open the generated migration file (newest file in `backend/src/Anela.Heblo.Persistence/Migrations/`). Confirm it creates `dqt_drift_results` table with all columns. If EF uses camelCase column names, that is also fine — EF applies the snake_case convention from the global naming strategy.

- [ ] **Step 3: Find the previous migration name for the SQL script**

```bash
ls backend/src/Anela.Heblo.Persistence/Migrations/*.cs | grep -v Designer | sort | tail -3
```

The second-to-last file (before `AddDqtDriftResults`) is the `<prev-migration>` name.

- [ ] **Step 4: Extract migration SQL**

```bash
PREV=$(ls backend/src/Anela.Heblo.Persistence/Migrations/*.cs | grep -v Designer | sort | tail -2 | head -1 | xargs basename | sed 's/.cs//')
cd backend && dotnet ef migrations script "$PREV" AddDqtDriftResults \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API \
  --output /tmp/drift-migration.sql && echo "SQL written to /tmp/drift-migration.sql"
```

- [ ] **Step 5: Apply migration to dev DB manually**

Connect to the local PostgreSQL dev DB and run the SQL from `/tmp/drift-migration.sql`. Then verify:

```sql
SELECT table_name FROM information_schema.tables WHERE table_name = 'dqt_drift_results';
-- Expected: 1 row returned
```

- [ ] **Step 6: Commit migration files**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(data-quality): add EF migration for dqt_drift_results table"
```

---

## Task 7: Application — Generic drift abstractions

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDriftDqtComparer.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftComparisonResult.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDriftDqtJobRunner.cs`

- [ ] **Step 1: Check the Services folder namespace**

```bash
head -5 backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs
```

Confirm namespace (likely `Anela.Heblo.Application.Features.DataQuality.Services`).

- [ ] **Step 2: Create `IDriftDqtComparer.cs`**

```csharp
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public interface IDriftDqtComparer
{
    DqtTestType TestType { get; }
    Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `DriftComparisonResult.cs`** (includes `DriftMismatch` in the same file)

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class DriftComparisonResult
{
    public IReadOnlyList<DriftMismatch> Mismatches { get; init; } = Array.Empty<DriftMismatch>();
    public int TotalChecked { get; init; }
}

public class DriftMismatch
{
    public string EntityKey { get; init; } = string.Empty;
    public int MismatchCode { get; init; }
    public string? HebloValue { get; init; }
    public string? ShoptetValue { get; init; }
    public string? Details { get; init; }
}
```

- [ ] **Step 4: Create `IDriftDqtJobRunner.cs`**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Services;

public interface IDriftDqtJobRunner
{
    Task RunAsync(Guid runId, CancellationToken ct = default);
}
```

- [ ] **Step 5: Verify build**

```bash
cd backend && dotnet build --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDriftDqtComparer.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftComparisonResult.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDriftDqtJobRunner.cs
git commit -m "feat(data-quality): add generic drift DQT interfaces and result DTOs"
```

---

## Task 8: Application — Failing tests for DriftDqtJobRunner

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DriftDqtJobRunnerTests.cs`

- [ ] **Step 1: Check existing test namespace**

```bash
head -10 backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtJobRunnerTests.cs
```

Confirm namespace and using patterns.

- [ ] **Step 2: Create `DriftDqtJobRunnerTests.cs`**

Before writing, read `DqtRun.cs` to confirm: public property names `DateFrom`, `DateTo`, `TestType`, `Status`, `TotalChecked`, `TotalMismatches`, `ErrorMessage`. Adjust property names below if they differ.

```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class DriftDqtJobRunnerTests
{
    private readonly Mock<IDqtRunRepository> _repoMock = new();
    private readonly Mock<IDriftDqtComparer> _comparerMock = new();

    public DriftDqtJobRunnerTests()
    {
        _comparerMock.Setup(c => c.TestType).Returns(DqtTestType.ProductPairing);
    }

    private DriftDqtJobRunner CreateSut() =>
        new(_repoMock.Object, new[] { _comparerMock.Object }, NullLogger<DriftDqtJobRunner>.Instance);

    [Fact]
    public async Task RunAsync_PersistsDriftResultsAndCompletesRun_WhenComparerSucceeds()
    {
        // Arrange
        var run = DqtRun.Start(
            DqtTestType.ProductPairing,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today),
            DqtTriggerType.Scheduled);

        _repoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        _comparerMock
            .Setup(c => c.CompareAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriftComparisonResult
            {
                TotalChecked = 10,
                Mismatches = new[]
                {
                    new DriftMismatch
                    {
                        EntityKey = "P001",
                        MismatchCode = (int)ProductPairingMismatch.MissingInErp,
                        ShoptetValue = "Eshop Product"
                    }
                }
            });

        // Act
        await CreateSut().RunAsync(run.Id);

        // Assert
        _repoMock.Verify(r => r.AddDriftResultsAsync(
            It.Is<IEnumerable<DqtDriftResult>>(e => e.Count() == 1 && e.First().EntityKey == "P001"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        run.Status.Should().Be(DqtRunStatus.Completed);
        run.TotalChecked.Should().Be(10);
        run.TotalMismatches.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_FailsRun_WhenComparerThrows()
    {
        // Arrange
        var run = DqtRun.Start(
            DqtTestType.ProductPairing,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today),
            DqtTriggerType.Scheduled);

        _repoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        _comparerMock
            .Setup(c => c.CompareAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("External API down"));

        // Act
        await CreateSut().RunAsync(run.Id);

        // Assert
        run.Status.Should().Be(DqtRunStatus.Failed);
        run.ErrorMessage.Should().Contain("External API down");
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_FailsRun_WhenNoComparerRegisteredForTestType()
    {
        // Arrange — run is StockWriteBack, but only ProductPairing comparer is registered
        var run = DqtRun.Start(
            DqtTestType.StockWriteBackReconciliation,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today),
            DqtTriggerType.Scheduled);

        _repoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        // Act — runner catches the InvalidOperationException internally
        var act = async () => await CreateSut().RunAsync(run.Id);

        // Assert
        await act.Should().NotThrowAsync();
        run.Status.Should().Be(DqtRunStatus.Failed);
    }
}
```

- [ ] **Step 3: Run tests — expect compilation error (DriftDqtJobRunner not created yet)**

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/ --no-restore 2>&1 | grep "error CS" | head -5
```

Expected: `error CS0246: The type or namespace name 'DriftDqtJobRunner' could not be found`.

- [ ] **Step 4: Commit test file**

```bash
git add backend/test/Anela.Heblo.Tests/Features/DataQuality/DriftDqtJobRunnerTests.cs
git commit -m "test(data-quality): write failing tests for DriftDqtJobRunner"
```

---

## Task 9: Application — Implement DriftDqtJobRunner

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs`

- [ ] **Step 1: Read `InvoiceDqtJobRunner.cs` to confirm `DqtRun` property names**

```bash
cat backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs
```

Note the exact property names used (`run.DateFrom`, `run.DateTo`, `run.TestType`, etc.) and how `GetByIdAsync` is called. Adjust the implementation below if property names differ.

- [ ] **Step 2: Create `DriftDqtJobRunner.cs`**

```csharp
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class DriftDqtJobRunner : IDriftDqtJobRunner
{
    private readonly IDqtRunRepository _repository;
    private readonly IEnumerable<IDriftDqtComparer> _comparers;
    private readonly ILogger<DriftDqtJobRunner> _logger;

    public DriftDqtJobRunner(
        IDqtRunRepository repository,
        IEnumerable<IDriftDqtComparer> comparers,
        ILogger<DriftDqtJobRunner> logger)
    {
        _repository = repository;
        _comparers = comparers;
        _logger = logger;
    }

    public async Task RunAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _repository.GetByIdAsync(runId, ct)
            ?? throw new InvalidOperationException($"DqtRun {runId} not found");

        try
        {
            var comparer = _comparers.SingleOrDefault(c => c.TestType == run.TestType)
                ?? throw new InvalidOperationException(
                    $"No IDriftDqtComparer registered for {run.TestType}");

            var result = await comparer.CompareAsync(run.DateFrom, run.DateTo, ct);

            var entities = result.Mismatches.Select(m => DqtDriftResult.Create(
                run.Id, run.TestType, m.EntityKey, m.MismatchCode,
                m.HebloValue, m.ShoptetValue, m.Details)).ToList();

            await _repository.AddDriftResultsAsync(entities, ct);
            run.Complete(result.TotalChecked, result.Mismatches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Drift DQT run {RunId} ({TestType}) failed", runId, run.TestType);
            run.Fail(ex.Message);
        }
        finally
        {
            await _repository.SaveChangesAsync(ct);
        }
    }
}
```

- [ ] **Step 3: Run tests — expect 3 passing**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~DriftDqtJobRunnerTests" 2>&1 | tail -10
```

Expected: `3 passed, 0 failed`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs
git commit -m "feat(data-quality): implement DriftDqtJobRunner"
```

---

## Task 10: Application — Failing tests for ProductPairingDqtComparer

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs`

- [ ] **Step 1: Confirm `IErpStockClient.ListAsync` return type**

```bash
grep -n "ListAsync" backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IErpStockClient.cs
```

Confirm whether it returns `Task<IReadOnlyList<ErpStock>>` or `Task<List<ErpStock>>`. Adjust the mock `.ReturnsAsync(...)` cast accordingly.

- [ ] **Step 2: Create `ProductPairingDqtComparerTests.cs`**

```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class ProductPairingDqtComparerTests
{
    private readonly Mock<IEshopStockClient> _eshopMock = new();
    private readonly Mock<IErpStockClient> _erpMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private ProductPairingDqtComparer CreateSut() =>
        new(_eshopMock.Object, _erpMock.Object);

    private void SetupEshop(params EshopStock[] products) =>
        _eshopMock.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products.ToList());

    private void SetupErp(params ErpStock[] products) =>
        _erpMock.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ErpStock>)products.ToList());

    [Fact]
    public async Task CompareAsync_ReturnsEmpty_WhenAllProductsPaired()
    {
        // Arrange
        SetupEshop(new EshopStock { Code = "P001", PairCode = "", Name = "Product 1" });
        SetupErp(new ErpStock { ProductCode = "P001", ProductName = "Product 1", ProductTypeId = 1 }); // Goods=1

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
        result.TotalChecked.Should().Be(1);
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingInErp_WhenShoptetProductNotInErp()
    {
        // Arrange
        SetupEshop(new EshopStock { Code = "ESHOP_ONLY", PairCode = "", Name = "Eshop Only" });
        SetupErp(); // Empty ERP

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("ESHOP_ONLY");
        ((ProductPairingMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(ProductPairingMismatch.MissingInErp);
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingInErpAndPairCodeUnresolved_WhenPairCodeNotInErp()
    {
        // Arrange
        SetupEshop(new EshopStock { Code = "ESHOP001", PairCode = "ERP001", Name = "Pair Code Product" });
        SetupErp(); // ERP001 not in ERP

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        var mismatch = (ProductPairingMismatch)result.Mismatches.Single().MismatchCode;
        mismatch.Should().HaveFlag(ProductPairingMismatch.MissingInErp);
        mismatch.Should().HaveFlag(ProductPairingMismatch.PairCodeUnresolved);
    }

    [Fact]
    public async Task CompareAsync_ReturnsMissingInShoptet_OnlyForSellableErpProducts()
    {
        // Arrange
        SetupEshop(); // Empty Shoptet
        SetupErp(
            new ErpStock { ProductCode = "PROD001", ProductName = "Sellable", ProductTypeId = 8 },  // Product=8
            new ErpStock { ProductCode = "MAT001",  ProductName = "Material",  ProductTypeId = 3 }   // Material=3, not sellable
        );

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert — only PROD001 is flagged; MAT001 is non-sellable and must be ignored
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("PROD001");
        ((ProductPairingMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(ProductPairingMismatch.MissingInShoptet);
    }
}
```

- [ ] **Step 3: Run — expect compile error**

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/ --no-restore 2>&1 | grep "error CS" | head -5
```

Expected: `ProductPairingDqtComparer` not found.

- [ ] **Step 4: Commit test file**

```bash
git add backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs
git commit -m "test(data-quality): write failing tests for ProductPairingDqtComparer"
```

---

## Task 11: Application — Implement ProductPairingDqtComparer

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`

- [ ] **Step 1: Confirm `ProductType` namespace**

```bash
head -5 backend/src/Anela.Heblo.Domain/Features/Catalog/ProductType.cs
```

Note the namespace (likely `Anela.Heblo.Domain.Features.Catalog`) and enum values (`Goods = 1`, `Product = 8`).

- [ ] **Step 2: Create `ProductPairingDqtComparer.cs`**

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class ProductPairingDqtComparer : IDriftDqtComparer
{
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IErpStockClient _erpStockClient;

    public DqtTestType TestType => DqtTestType.ProductPairing;

    public ProductPairingDqtComparer(IEshopStockClient eshopStockClient, IErpStockClient erpStockClient)
    {
        _eshopStockClient = eshopStockClient;
        _erpStockClient = erpStockClient;
    }

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Date range is intentionally unused — product pairing is a current-state snapshot
        var eshopProducts = await _eshopStockClient.ListAsync(ct);
        var erpProducts = await _erpStockClient.ListAsync(ct);

        var sellableErpProducts = erpProducts.Where(IsSellable).ToList();

        var erpCodeSet = sellableErpProducts
            .Select(p => p.ProductCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // All Shoptet identifiers (Code + PairCode) used when checking ERP → Shoptet direction
        var shoptetIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in eshopProducts)
        {
            shoptetIdentifiers.Add(p.Code);
            if (!string.IsNullOrWhiteSpace(p.PairCode))
                shoptetIdentifiers.Add(p.PairCode);
        }

        var mismatches = new List<DriftMismatch>();

        // Check A: each Shoptet product must resolve to an ERP code
        foreach (var eshopProduct in eshopProducts)
        {
            var hasPairCode = !string.IsNullOrWhiteSpace(eshopProduct.PairCode);
            var resolvedCode = hasPairCode ? eshopProduct.PairCode : eshopProduct.Code;

            if (erpCodeSet.Contains(resolvedCode))
                continue;

            var mismatch = ProductPairingMismatch.MissingInErp;
            if (hasPairCode)
                mismatch |= ProductPairingMismatch.PairCodeUnresolved;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = eshopProduct.Code,
                MismatchCode = (int)mismatch,
                ShoptetValue = eshopProduct.Name,
                HebloValue = null,
                Details = hasPairCode
                    ? $"Shoptet product '{eshopProduct.Code}' PairCode '{eshopProduct.PairCode}' not found in ERP"
                    : $"Shoptet product '{eshopProduct.Code}' not found in ERP"
            });
        }

        // Check B: each sellable ERP product must appear in Shoptet
        foreach (var erpProduct in sellableErpProducts)
        {
            if (shoptetIdentifiers.Contains(erpProduct.ProductCode))
                continue;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = erpProduct.ProductCode,
                MismatchCode = (int)ProductPairingMismatch.MissingInShoptet,
                HebloValue = erpProduct.ProductName,
                ShoptetValue = null,
                Details = $"Sellable ERP product '{erpProduct.ProductCode}' not in Shoptet catalog"
            });
        }

        var totalChecked = shoptetIdentifiers
            .Union(erpCodeSet, StringComparer.OrdinalIgnoreCase)
            .Count();

        return new DriftComparisonResult { Mismatches = mismatches, TotalChecked = totalChecked };
    }

    private static bool IsSellable(ErpStock product) =>
        product.ProductTypeId == (int)ProductType.Goods ||
        product.ProductTypeId == (int)ProductType.Product;
}
```

- [ ] **Step 3: Run tests — expect 4 passing**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~ProductPairingDqtComparerTests" 2>&1 | tail -10
```

Expected: `4 passed, 0 failed`

- [ ] **Step 4: Full build check**

```bash
cd backend && dotnet build --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs
git commit -m "feat(data-quality): implement ProductPairingDqtComparer"
```

---

## Task 12: Application — Failing tests for StockWriteBackDqtComparer

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtComparerTests.cs`

- [ ] **Step 1: Confirm `StockUpOperation` public constructor signature**

```bash
grep -n "public StockUpOperation(" backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs
```

The constructor is: `StockUpOperation(string documentNumber, string productCode, int amount, StockUpSourceType sourceType, int sourceId)`. It sets `CreatedAt = DateTime.UtcNow` and `State = StockUpOperationState.Pending`. Operations will have today's timestamp — tests must use today's date range.

- [ ] **Step 2: Create `StockWriteBackDqtComparerTests.cs`**

```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class StockWriteBackDqtComparerTests
{
    private readonly Mock<IStockUpOperationRepository> _operationRepoMock = new();
    private readonly Mock<IStockTakingRepository> _stockTakingRepoMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private StockWriteBackDqtComparer CreateSut(TimeSpan? stuckThreshold = null) =>
        new(_operationRepoMock.Object, _stockTakingRepoMock.Object, stuckThreshold);

    private void SetupNoStockTaking() =>
        _stockTakingRepoMock.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>());

    [Fact]
    public async Task CompareAsync_ReturnsEmpty_WhenAllOperationsCompleted()
    {
        // Arrange
        var completedOp = new StockUpOperation("OP001", "P001", 1, StockUpSourceType.TransportBox, 1);
        completedOp.MarkAsSubmitted(DateTime.UtcNow);
        completedOp.MarkAsCompleted(DateTime.UtcNow);

        _operationRepoMock.Setup(r => r.GetAll()).Returns(new[] { completedOp }.AsQueryable());
        SetupNoStockTaking();

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ReturnsOperationFailed_WhenOperationInFailedState()
    {
        // Arrange
        var failedOp = new StockUpOperation("OP002", "P002", 5, StockUpSourceType.TransportBox, 1);
        failedOp.MarkAsFailed(DateTime.UtcNow, "HTTP 500 from Shoptet");

        _operationRepoMock.Setup(r => r.GetAll()).Returns(new[] { failedOp }.AsQueryable());
        SetupNoStockTaking();

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("P002");
        ((StockWriteBackMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(StockWriteBackMismatch.OperationFailed);
        result.Mismatches[0].Details.Should().Contain("HTTP 500 from Shoptet");
    }

    [Fact]
    public async Task CompareAsync_ReturnsOperationStuck_WhenPendingOperationExceedsThreshold()
    {
        // Arrange — use TimeSpan.Zero threshold so any Pending operation is "stuck"
        var pendingOp = new StockUpOperation("OP003", "P003", 2, StockUpSourceType.TransportBox, 1);
        // Stays Pending (no state transition called)

        _operationRepoMock.Setup(r => r.GetAll()).Returns(new[] { pendingOp }.AsQueryable());
        SetupNoStockTaking();

        // Act
        var result = await CreateSut(stuckThreshold: TimeSpan.Zero).CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        ((StockWriteBackMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(StockWriteBackMismatch.OperationStuck);
    }

    [Fact]
    public async Task CompareAsync_ReturnsStockTakingErrored_WhenRecordHasError()
    {
        // Arrange
        _operationRepoMock.Setup(r => r.GetAll()).Returns(Array.Empty<StockUpOperation>().AsQueryable());
        _stockTakingRepoMock.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>
            {
                new() { Code = "P004", Error = "Shoptet API timeout", Date = DateTime.UtcNow, AmountNew = 10, AmountOld = 8 }
            });

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("P004");
        ((StockWriteBackMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(StockWriteBackMismatch.StockTakingErrored);
        result.Mismatches[0].Details.Should().Contain("Shoptet API timeout");
    }
}
```

- [ ] **Step 3: Run — expect compile error**

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/ --no-restore 2>&1 | grep "error CS" | head -5
```

Expected: `StockWriteBackDqtComparer` not found.

- [ ] **Step 4: Commit test file**

```bash
git add backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtComparerTests.cs
git commit -m "test(data-quality): write failing tests for StockWriteBackDqtComparer"
```

---

## Task 13: Application — Implement StockWriteBackDqtComparer

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs`

- [ ] **Step 1: Create `StockWriteBackDqtComparer.cs`**

The `stuckThreshold` parameter defaults to 1 hour in production; tests inject `TimeSpan.Zero` to make any non-completed operation count as stuck.

```csharp
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class StockWriteBackDqtComparer : IDriftDqtComparer
{
    private static readonly TimeSpan DefaultStuckThreshold = TimeSpan.FromHours(1);

    private readonly IStockUpOperationRepository _operationRepository;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly TimeSpan _stuckThreshold;

    public DqtTestType TestType => DqtTestType.StockWriteBackReconciliation;

    public StockWriteBackDqtComparer(
        IStockUpOperationRepository operationRepository,
        IStockTakingRepository stockTakingRepository,
        TimeSpan? stuckThreshold = null)
    {
        _operationRepository = operationRepository;
        _stockTakingRepository = stockTakingRepository;
        _stuckThreshold = stuckThreshold ?? DefaultStuckThreshold;
    }

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var stuckCutoff = DateTime.UtcNow - _stuckThreshold;

        var operations = _operationRepository.GetAll()
            .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc)
            .ToList();

        var stockTakingRecords = await _stockTakingRepository.GetByDateRangeAsync(fromUtc, toUtc, ct);

        var mismatches = new List<DriftMismatch>();

        foreach (var op in operations)
        {
            var mismatch = StockWriteBackMismatch.None;

            if (op.State == StockUpOperationState.Failed)
                mismatch |= StockWriteBackMismatch.OperationFailed;

            if ((op.State == StockUpOperationState.Pending || op.State == StockUpOperationState.Submitted)
                && op.CreatedAt <= stuckCutoff)
                mismatch |= StockWriteBackMismatch.OperationStuck;

            if (mismatch == StockWriteBackMismatch.None)
                continue;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = op.ProductCode,
                MismatchCode = (int)mismatch,
                HebloValue = op.Amount.ToString(),
                ShoptetValue = null,
                Details = BuildOperationDetails(op)
            });
        }

        foreach (var record in stockTakingRecords.Where(r => r.Error != null))
        {
            mismatches.Add(new DriftMismatch
            {
                EntityKey = record.Code,
                MismatchCode = (int)StockWriteBackMismatch.StockTakingErrored,
                HebloValue = record.AmountNew.ToString("F2"),
                ShoptetValue = null,
                Details = $"Stock-taking error: {record.Error}"
            });
        }

        return new DriftComparisonResult
        {
            Mismatches = mismatches,
            TotalChecked = operations.Count + stockTakingRecords.Count
        };
    }

    private static string BuildOperationDetails(StockUpOperation op)
    {
        var parts = new List<string> { $"Doc: {op.DocumentNumber}", $"State: {op.State}" };
        if (!string.IsNullOrWhiteSpace(op.ErrorMessage))
            parts.Add($"Error: {op.ErrorMessage}");
        return string.Join(" | ", parts);
    }
}
```

- [ ] **Step 2: Run tests — expect 4 passing**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~StockWriteBackDqtComparerTests" 2>&1 | tail -10
```

Expected: `4 passed, 0 failed`

- [ ] **Step 3: Run all DataQuality tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~DataQuality" 2>&1 | tail -15
```

Expected: All 11+ tests pass.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs
git commit -m "feat(data-quality): implement StockWriteBackDqtComparer"
```

---

## Task 14: Application — Wire services, RunDqtHandler dispatch, and recurring jobs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs`

- [ ] **Step 1: Register new services in `DataQualityModule.cs`**

Open the file. Find the existing `services.AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>();` and add after it:

```csharp
services.AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>();
services.AddScoped<IDriftDqtComparer, ProductPairingDqtComparer>();
services.AddScoped<IDriftDqtComparer, StockWriteBackDqtComparer>();
```

Registering both comparers against `IDriftDqtComparer` means `IEnumerable<IDriftDqtComparer>` in `DriftDqtJobRunner` resolves both.

- [ ] **Step 2: Update `RunDqtHandler.cs` to dispatch by TestType**

Find the fire-and-forget `Task.Run` block (currently calls `IInvoiceDqtJobRunner`). Replace only that inner block:

```csharp
_ = Task.Run(async () =>
{
    using var scope = _scopeFactory.CreateScope();
    if (request.TestType == DqtTestType.IssuedInvoiceComparison)
    {
        var runner = scope.ServiceProvider.GetRequiredService<IInvoiceDqtJobRunner>();
        await runner.RunAsync(run.Id);
    }
    else
    {
        var runner = scope.ServiceProvider.GetRequiredService<IDriftDqtJobRunner>();
        await runner.RunAsync(run.Id);
    }
}, CancellationToken.None);
```

Add `using Anela.Heblo.Application.Features.DataQuality.Services;` if needed.

- [ ] **Step 3: Read `InvoiceDqtJob.cs` to confirm interface shape**

```bash
cat backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/InvoiceDqtJob.cs
```

Note: exact `IRecurringJob` interface methods, `RecurringJobMetadata` property type, `IRecurringJobStatusChecker` usage, required namespaces.

- [ ] **Step 4: Create `ProductPairingDqtJob.cs`**

```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;

public class ProductPairingDqtJob : IRecurringJob
{
    private readonly IDqtRunRepository _repository;
    private readonly IDriftDqtJobRunner _jobRunner;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-product-pairing-dqt",
        DisplayName = "Daily Product Pairing Data Quality Test",
        Description = "Checks which Shoptet products lack an ERP match and vice versa",
        CronExpression = "0 6 * * *", // Daily at 06:00 (after invoice DQT at 05:00)
        DefaultIsEnabled = true
    };

    public ProductPairingDqtJob(
        IDqtRunRepository repository,
        IDriftDqtJobRunner jobRunner,
        IRecurringJobStatusChecker statusChecker)
    {
        _repository = repository;
        _jobRunner = jobRunner;
        _statusChecker = statusChecker;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
            return;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var run = DqtRun.Start(DqtTestType.ProductPairing, today, today, DqtTriggerType.Scheduled);

        await _repository.AddAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
    }
}
```

- [ ] **Step 5: Create `StockWriteBackDqtJob.cs`**

```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;

public class StockWriteBackDqtJob : IRecurringJob
{
    private readonly IDqtRunRepository _repository;
    private readonly IDriftDqtJobRunner _jobRunner;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-stock-writeback-dqt",
        DisplayName = "Daily Stock Write-Back Reconciliation",
        Description = "Detects failed, stuck, or errored stock write-back operations from the previous day",
        CronExpression = "0 7 * * *", // Daily at 07:00
        DefaultIsEnabled = true
    };

    public StockWriteBackDqtJob(
        IDqtRunRepository repository,
        IDriftDqtJobRunner jobRunner,
        IRecurringJobStatusChecker statusChecker)
    {
        _repository = repository;
        _jobRunner = jobRunner;
        _statusChecker = statusChecker;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
            return;

        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        var run = DqtRun.Start(DqtTestType.StockWriteBackReconciliation, yesterday, yesterday, DqtTriggerType.Scheduled);

        await _repository.AddAsync(run, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _jobRunner.RunAsync(run.Id, cancellationToken);
    }
}
```

**Note:** `IRecurringJob`, `RecurringJobMetadata`, `IRecurringJobStatusChecker`, `IDqtRunRepository.AddAsync` — all must match what `InvoiceDqtJob.cs` uses. Adjust namespaces and method signatures if they differ.

- [ ] **Step 6: Verify build**

```bash
cd backend && dotnet build --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/StockWriteBackDqtJob.cs
git commit -m "feat(data-quality): wire DriftDqtJobRunner, update RunDqtHandler dispatch, add recurring jobs"
```

---

## Task 15: API & Contracts — DqtDriftResultDto and GetDqtRunDetail branching

**Files:**
- Create: `backend/src/Anela.Heblo.Contracts/Features/DataQuality/DqtDriftResultDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityMappingProfile.cs`

- [ ] **Step 1: Check Contracts project namespace**

```bash
head -5 backend/src/Anela.Heblo.Contracts/Features/DataQuality/InvoiceDqtResultDto.cs
```

Confirm namespace (likely `Anela.Heblo.Contracts.Features.DataQuality`).

- [ ] **Step 2: Create `DqtDriftResultDto.cs`**

```csharp
namespace Anela.Heblo.Contracts.Features.DataQuality;

public class DqtDriftResultDto
{
    public string EntityKey { get; set; } = string.Empty;
    public int MismatchCode { get; set; }
    public string TestType { get; set; } = string.Empty;
    public string? HebloValue { get; set; }
    public string? ShoptetValue { get; set; }
    public string? Details { get; set; }
}
```

- [ ] **Step 3: Add fields to `GetDqtRunDetailResponse.cs`**

Read the file first. Then append after the existing `Results` property:

```csharp
public List<DqtDriftResultDto>? DriftResults { get; set; }
public int TotalDriftResults { get; set; }
```

Add `using Anela.Heblo.Contracts.Features.DataQuality;` if not present.

- [ ] **Step 4: Update `GetDqtRunDetailHandler.cs` to branch by TestType**

Read the full handler file. Find the block that returns the response and replace with a branch. The run is still fetched using the existing `GetWithResultsAsync` (it returns the run object for all test types; invoice results are empty for non-invoice runs).

```csharp
// Inside the try block, after fetching the run:

if (run.TestType == DqtTestType.IssuedInvoiceComparison)
{
    return new GetDqtRunDetailResponse
    {
        Success = true,
        Run = _mapper.Map<DqtRunDto>(run),
        Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results)
    };
}

var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(
    run.Id, request.ResultPage, request.ResultPageSize, cancellationToken);

return new GetDqtRunDetailResponse
{
    Success = true,
    Run = _mapper.Map<DqtRunDto>(run),
    DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems),
    TotalDriftResults = driftTotal
};
```

Add required using statements at the top of the file.

- [ ] **Step 5: Add `DqtDriftResult → DqtDriftResultDto` mapping to `DataQualityMappingProfile.cs`**

Find the existing `CreateMap` calls and append:

```csharp
CreateMap<DqtDriftResult, DqtDriftResultDto>()
    .ForMember(dest => dest.TestType, opt => opt.MapFrom(src => src.TestType.ToString()));
```

Add `using Anela.Heblo.Domain.Features.DataQuality;` and `using Anela.Heblo.Contracts.Features.DataQuality;` if not present.

- [ ] **Step 6: Verify build and run all DQT tests**

```bash
cd backend && dotnet build --no-restore 2>&1 | tail -5
cd backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~DataQuality" 2>&1 | tail -10
```

Expected: Build succeeded, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Contracts/Features/DataQuality/DqtDriftResultDto.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailResponse.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityMappingProfile.cs
git commit -m "feat(data-quality): add DqtDriftResultDto, branch GetDqtRunDetail by TestType, update mapping"
```

---

## Task 16: Frontend — i18n strings and DqtRunsTable labels

**Files:**
- Modify: `frontend/src/i18n.ts`
- Modify: `frontend/src/components/data-quality/DqtRunsTable.tsx`

- [ ] **Step 1: Find existing test-type label location in `i18n.ts`**

```bash
grep -n "IssuedInvoiceComparison" frontend/src/i18n.ts
```

Note the exact structure (key path, surrounding object) for both `cs` and `en` locales. Add the new values at the same level:

```typescript
// In cs locale — add alongside IssuedInvoiceComparison:
ProductPairing: 'Párování produktů',
StockWriteBackReconciliation: 'Zpětný zápis skladu',

// Also add mismatch labels — add in a logical place near dataQuality section:
productPairingMismatches: {
    MissingInErp: 'Chybí v ERP',
    MissingInShoptet: 'Chybí v Shoptet',
    PairCodeUnresolved: 'Nespárovaný párový kód',
},
stockWriteBackMismatches: {
    OperationFailed: 'Operace selhala',
    OperationStuck: 'Operace zaseknutá',
    StockTakingErrored: 'Chyba inventury',
},
```

```typescript
// In en locale — same keys:
ProductPairing: 'Product Pairing',
StockWriteBackReconciliation: 'Stock Write-Back Reconciliation',
productPairingMismatches: {
    MissingInErp: 'Missing in ERP',
    MissingInShoptet: 'Missing in Shoptet',
    PairCodeUnresolved: 'Unresolved pair code',
},
stockWriteBackMismatches: {
    OperationFailed: 'Operation failed',
    OperationStuck: 'Operation stuck',
    StockTakingErrored: 'Stock-taking errored',
},
```

- [ ] **Step 2: Add test-type labels in `DqtRunsTable.tsx`**

Search the file:
```bash
grep -n "IssuedInvoiceComparison\|testType\|TEST_TYPE" frontend/src/components/data-quality/DqtRunsTable.tsx
```

Find the test-type label map or inline label logic. Add the two new entries in exactly the same pattern:
```typescript
ProductPairing: 'Párování produktů',
StockWriteBackReconciliation: 'Zpětný zápis skladu',
```

- [ ] **Step 3: Verify frontend build**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: No errors.

- [ ] **Step 4: Lint**

```bash
cd frontend && npm run lint 2>&1 | tail -10
```

Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/i18n.ts frontend/src/components/data-quality/DqtRunsTable.tsx
git commit -m "feat(data-quality): add i18n strings and test-type labels for ProductPairing and StockWriteBack"
```

---

## Task 17: Frontend — DqtRunDetail drift results rendering

**Files:**
- Modify: `frontend/src/components/data-quality/DqtRunDetail.tsx`

- [ ] **Step 1: Read the current DqtRunDetail component**

```bash
cat frontend/src/components/data-quality/DqtRunDetail.tsx
```

Note: how props are typed, where the existing result table JSX sits, how `run` and `results` are received, and the className conventions used in the rest of the file.

- [ ] **Step 2: Add mismatch decoder helpers at the top of the file (before the component)**

```typescript
const PRODUCT_PAIRING_FLAGS: Record<number, string> = {
    1: 'Chybí v ERP',
    2: 'Chybí v Shoptet',
    4: 'Nespárovaný párový kód',
};

const STOCK_WRITE_BACK_FLAGS: Record<number, string> = {
    1: 'Operace selhala',
    2: 'Operace zaseknutá',
    4: 'Chyba inventury',
};

function decodeMismatchFlags(code: number, labels: Record<number, string>): string[] {
    return Object.entries(labels)
        .filter(([flag]) => (code & Number(flag)) !== 0)
        .map(([, label]) => label);
}
```

- [ ] **Step 3: Add drift results rendering**

In the component JSX, find the existing invoice result table. Wrap the invoice table in the `IssuedInvoiceComparison` branch and add the drift branch:

```tsx
{run.testType === 'ProductPairing' || run.testType === 'StockWriteBackReconciliation' ? (
    <table className="w-full text-sm">
        <thead>
            <tr className="border-b text-left">
                <th className="py-2 pr-4 font-medium">Entita</th>
                <th className="py-2 pr-4 font-medium">Neshoda</th>
                <th className="py-2 pr-4 font-medium">Heblo</th>
                <th className="py-2 pr-4 font-medium">Shoptet</th>
                <th className="py-2 font-medium">Detail</th>
            </tr>
        </thead>
        <tbody>
            {(driftResults ?? []).map((row, i) => {
                const flagLabels = run.testType === 'ProductPairing'
                    ? decodeMismatchFlags(row.mismatchCode, PRODUCT_PAIRING_FLAGS)
                    : decodeMismatchFlags(row.mismatchCode, STOCK_WRITE_BACK_FLAGS);
                return (
                    <tr key={i} className="border-b last:border-0">
                        <td className="py-1.5 pr-4 font-mono text-xs">{row.entityKey}</td>
                        <td className="py-1.5 pr-4">
                            {flagLabels.map(label => (
                                <span
                                    key={label}
                                    className="inline-block mr-1 px-1.5 py-0.5 rounded text-xs bg-yellow-100 text-yellow-800"
                                >
                                    {label}
                                </span>
                            ))}
                        </td>
                        <td className="py-1.5 pr-4 text-gray-600 text-xs">{row.hebloValue ?? '—'}</td>
                        <td className="py-1.5 pr-4 text-gray-600 text-xs">{row.shoptetValue ?? '—'}</td>
                        <td className="py-1.5 text-gray-500 text-xs">{row.details ?? ''}</td>
                    </tr>
                );
            })}
        </tbody>
    </table>
) : (
    /* existing invoice result table — leave untouched */
)}
```

**Note on `driftResults`:** This must be sourced from the hook. Check `useDataQuality.ts` — the `useDqtRunDetail` hook returns the response from `GET /api/data-quality/runs/{id}`. After the BE change, the response includes `driftResults` and `totalDriftResults`. The OpenAPI TS client regenerates on `npm run build`, so the field will be typed automatically. Pass `driftResults` down as a prop or read it from the hook directly depending on the component pattern.

- [ ] **Step 4: Verify build and lint**

```bash
cd frontend && npm run build 2>&1 | tail -20 && npm run lint 2>&1 | tail -10
```

Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/data-quality/DqtRunDetail.tsx
git commit -m "feat(data-quality): render drift results table in DqtRunDetail"
```

---

## Task 18: Frontend — RunDqtButton test-type selector

**Files:**
- Modify: `frontend/src/components/data-quality/RunDqtButton.tsx`

- [ ] **Step 1: Read the current RunDqtButton**

```bash
cat frontend/src/components/data-quality/RunDqtButton.tsx
```

Note: current state variables, how `mutate({...})` is called, what the mutation payload looks like, and where the form inputs are rendered.

- [ ] **Step 2: Add test-type state**

Find the existing `useState` declarations and add:

```typescript
const [testType, setTestType] = useState<
    'IssuedInvoiceComparison' | 'ProductPairing' | 'StockWriteBackReconciliation'
>('IssuedInvoiceComparison');
```

- [ ] **Step 3: Add a `<select>` for test type in the form**

Find the date-picker inputs. Add the select immediately before them (or as the first field):

```tsx
<div className="mb-3">
    <label className="block text-sm font-medium text-gray-700 mb-1">Typ testu</label>
    <select
        value={testType}
        onChange={e => setTestType(e.target.value as typeof testType)}
        className="border rounded px-2 py-1 text-sm w-full"
    >
        <option value="IssuedInvoiceComparison">Fakturační porovnání</option>
        <option value="ProductPairing">Párování produktů</option>
        <option value="StockWriteBackReconciliation">Zpětný zápis skladu</option>
    </select>
</div>
```

- [ ] **Step 4: Pass `testType` to the mutation call**

Find the `mutate({...})` or equivalent call. Replace the hardcoded test type (if any) with the state variable:

```typescript
mutate({ testType, dateFrom, dateTo });
```

Check that `testType`, `dateFrom`, `dateTo` match the exact field names expected by the `RunDqtRequest` contract (look at how `InvoiceDqtResultDto` or other request types are named in the generated client).

- [ ] **Step 5: Verify build and lint**

```bash
cd frontend && npm run build 2>&1 | tail -20 && npm run lint 2>&1 | tail -10
```

Expected: No errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/data-quality/RunDqtButton.tsx
git commit -m "feat(data-quality): add test-type selector to RunDqtButton"
```

---

## Task 19: Final verification

- [ ] **Step 1: Full backend build and format**

```bash
cd backend && dotnet build 2>&1 | tail -5
cd backend && dotnet format --verify-no-changes 2>&1 | tail -5
```

If format reports violations: run `dotnet format`, rebuild, then re-run `--verify-no-changes`.

- [ ] **Step 2: Run all DataQuality unit tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~DataQuality" 2>&1 | tail -20
```

Expected: ≥11 tests pass (3 runner + 4 pairing + 4 write-back).

- [ ] **Step 3: Run full test suite — confirm no regressions**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/ 2>&1 | tail -10
```

Expected: All pre-existing tests still pass.

- [ ] **Step 4: Full frontend build and lint**

```bash
cd frontend && npm run build 2>&1 | tail -10
cd frontend && npm run lint 2>&1 | tail -5
```

Expected: No errors.

- [ ] **Step 5: Confirm migration was applied**

```bash
psql "$CONNECTION_STRING" -c "SELECT table_name FROM information_schema.tables WHERE table_name = 'dqt_drift_results';"
```

Expected: 1 row. If the migration was not yet applied, run the SQL from Task 6 Step 5 now.

- [ ] **Step 6: Manual smoke test**

Start backend:
```bash
cd backend && dotnet run --project src/Anela.Heblo.API
```

Start frontend:
```bash
cd frontend && npm run dev
```

1. Navigate to `/data-quality`
2. Click "Spustit DQT" → select "Párování produktů" → click run
3. Wait until run appears as Completed in the list
4. Open the run → confirm a table with **Entita / Neshoda / Heblo / Shoptet / Detail** columns renders
5. Repeat with "Zpětný zápis skladu"
6. Repeat with "Fakturační porovnání" — confirm original invoice rows still render correctly
7. Navigate to `/hangfire` — confirm `daily-product-pairing-dqt` and `daily-stock-writeback-dqt` appear with correct CRON schedules
