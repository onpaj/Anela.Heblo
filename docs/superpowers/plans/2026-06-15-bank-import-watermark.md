# Watermark-Based Bank Statement Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make daily bank-statement imports self-healing by persisting a per-account "last valid import date" watermark and importing everything since that watermark, so a skipped day is never silently lost.

**Architecture:** A new `BankImportState` entity stores a per-account watermark + audit fields. Each daily job reads its watermark, imports the range `[watermark .. targetDate]`, and advances the watermark only on a fully-successful run (a 0-document run is a success). The import handler becomes idempotent (dedup already-succeeded statements, upsert retried failures) so overlapping re-scans never re-push to FlexiBee or crash on the unique `TransferId` index. Staleness is surfaced via logs, a new read endpoint, and a UI badge.

**Tech Stack:** .NET 8, MediatR, EF Core (PostgreSQL), Hangfire recurring jobs, xUnit + FluentAssertions + Moq; React + react-query (TypeScript).

---

## Background / Context

Today each job hardcodes its window in `GetParameters()`:
- `ComgateCzkImportJob` / `ComgateEurImportJob` → **yesterday only** (`DateTime.Today.AddDays(-1)`).
- `ShoptetPayImportJob` → **today only** (`DateTime.Today`).

If a run never executes (app down, deploy window, crash), that day is lost forever and nobody is told. The handler also has **no dedup** — it blindly `AddAsync`es every fetched statement and relies on the unique `IX_BankStatements_TransferId` index to throw (the exception is caught and stored as an error row). Re-scanning overlapping ranges (which the watermark approach requires) would therefore re-push to FlexiBee and spam duplicate error rows, and retrying a previously-failed `TransferId` would crash on the unique index. So **idempotent dedup + upsert-on-retry is a prerequisite**, not optional.

### Confirmed decisions
1. **Observability:** logs (Warning when behind, Error when clamped at the cap) **+** new `GET /api/bank-statements/import-state` endpoint **+** small per-account UI badge on the Import tab.
2. **Backfill cap:** **14 days**. Clamp `DateFrom` and emit an Error log when exceeded.
3. **First-run bootstrap (null watermark):** derive `DateFrom` from the max already-imported `StatementDate` for that account; fall back to the target date if no history.
4. **Comgate per-day loop:** keep current abort-on-failure behavior; rely on watermark retry + the 14-day cap.

### Key facts confirmed during research
- Entities use the mutate-via-methods pattern (`RecurringJobConfiguration`), `Entity<string>` with `Id` = natural key. Follow that — do NOT introduce a value-object immutability pattern here.
- **No global snake_case naming convention.** The model snapshot + `BankStatementImportConfiguration` use PascalCase columns with explicit `HasColumnName`/`HasColumnType`. The snake_case in `20260105125530_AddRecurringJobConfigurations.cs` is a stale artifact — do not copy it.
- `ImportStatus.Success == "OK"`, `ImportStatus.ProcessingError == "PROCESSING_ERROR"`, `ImportStatus.UnknownError == "UNKNOWN_ERROR"` (`backend/src/Anela.Heblo.Domain/Features/Bank/ImportStatus.cs`).
- Jobs are auto-discovered & registered Scoped via reflection in `AddRecurringJobs()` — any new ctor dependency must be DI-registered.
- Frontend bank hooks use **manual fetch with absolute URLs** (`${apiClient.baseUrl}${relativeUrl}`), not the generated client. Follow that pattern (see CLAUDE.md absolute-URL rule).
- FE tests run via `react-scripts test`, not `npx jest`.

### Validation gates (run before declaring done)
- BE: `cd backend && dotnet build && dotnet format` and `dotnet test` (Bank tests).
- FE: `cd frontend && npm run build && npm run lint`.

---

## Task 1: `BankImportState` domain entity

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Bank/BankImportState.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/BankImportStateTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Bank/BankImportStateTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Bank;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public sealed class BankImportStateTests
{
    [Fact]
    public void Constructor_SetsAccountAndId()
    {
        var state = new BankImportState("ComgateCZK");

        state.Account.Should().Be("ComgateCZK");
        state.Id.Should().Be("ComgateCZK");
        state.LastValidImportDate.Should().BeNull();
        state.ConsecutiveFailureCount.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_Throws_WhenAccountMissing(string? account)
    {
        var act = () => new BankImportState(account!);
        act.Should().Throw<ArgumentException>().WithParameterName("account");
    }

    [Fact]
    public void RecordSuccess_AdvancesWatermark_ToDateOnly_AndClearsFailure()
    {
        var state = new BankImportState("ComgateCZK");
        state.RecordFailure("boom", new DateTime(2026, 6, 1), new DateTime(2026, 6, 1));

        var watermark = new DateTime(2026, 6, 14, 4, 30, 0);
        var start = new DateTime(2026, 6, 15, 4, 30, 0);
        var finish = new DateTime(2026, 6, 15, 4, 31, 0);
        state.RecordSuccess(watermark, start, finish);

        state.LastValidImportDate.Should().Be(new DateTime(2026, 6, 14)); // time stripped
        state.LastRunStatus.Should().Be("OK");
        state.LastErrorMessage.Should().BeNull();
        state.ConsecutiveFailureCount.Should().Be(0);
        state.LastRunStartedAt.Should().Be(start);
        state.LastRunFinishedAt.Should().Be(finish);
    }

    [Fact]
    public void RecordFailure_DoesNotAdvanceWatermark_AndIncrementsCount()
    {
        var state = new BankImportState("ComgateCZK");
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);

        state.RecordFailure("first", DateTime.UtcNow, DateTime.UtcNow);
        state.RecordFailure("second", DateTime.UtcNow, DateTime.UtcNow);

        state.LastValidImportDate.Should().Be(new DateTime(2026, 6, 10)); // unchanged
        state.LastRunStatus.Should().Be("ERROR");
        state.LastErrorMessage.Should().Be("second");
        state.ConsecutiveFailureCount.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~BankImportStateTests`
Expected: FAIL — `BankImportState` does not exist (compile error).

- [ ] **Step 3: Write the entity**

Create `backend/src/Anela.Heblo.Domain/Features/Bank/BankImportState.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Bank;

/// <summary>
/// Per-account watermark for incremental bank statement import.
/// LastValidImportDate is the date (inclusive) through which all statements for the
/// account have been successfully imported. A run importing zero documents still
/// advances it. Null until the first successful run.
/// </summary>
public class BankImportState : Entity<string>
{
    public const string StatusOk = "OK";
    public const string StatusError = "ERROR";

    [Required]
    [MaxLength(100)]
    public string Account { get; private set; }

    public DateTime? LastValidImportDate { get; private set; }
    public DateTime? LastRunStartedAt { get; private set; }
    public DateTime? LastRunFinishedAt { get; private set; }

    [MaxLength(20)]
    public string? LastRunStatus { get; private set; }

    [MaxLength(2000)]
    public string? LastErrorMessage { get; private set; }

    public int ConsecutiveFailureCount { get; private set; }

    // Private constructor for EF Core
    private BankImportState()
    {
        Account = string.Empty;
    }

    public BankImportState(string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required", nameof(account));

        Account = account;
        Id = account; // Account is the primary key
    }

    public void RecordSuccess(DateTime watermark, DateTime runStartedAt, DateTime runFinishedAt)
    {
        LastValidImportDate = watermark.Date;
        LastRunStartedAt = runStartedAt;
        LastRunFinishedAt = runFinishedAt;
        LastRunStatus = StatusOk;
        LastErrorMessage = null;
        ConsecutiveFailureCount = 0;
    }

    public void RecordFailure(string error, DateTime runStartedAt, DateTime runFinishedAt)
    {
        LastRunStartedAt = runStartedAt;
        LastRunFinishedAt = runFinishedAt;
        LastRunStatus = StatusError;
        LastErrorMessage = error;
        ConsecutiveFailureCount += 1;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~BankImportStateTests`
Expected: PASS (all 6 cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Bank/BankImportState.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/BankImportStateTests.cs
git commit -m "feat(bank): add BankImportState watermark entity"
```

---

## Task 2: `IBankImportStateRepository` (Domain)

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Bank/IBankImportStateRepository.cs`

- [ ] **Step 1: Write the interface** (no test — pure interface)

Create `backend/src/Anela.Heblo.Domain/Features/Bank/IBankImportStateRepository.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Bank;

public interface IBankImportStateRepository
{
    Task<BankImportState?> GetByAccountAsync(string account, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BankImportState>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(BankImportState state, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Domain`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Bank/IBankImportStateRepository.cs
git commit -m "feat(bank): add IBankImportStateRepository abstraction"
```

---

## Task 3: `BankImportState` persistence (EF config + repository + DbSet)

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Bank/BankImportStateConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Features/Bank/BankImportStateRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/BankImportStateRepositoryTests.cs`

- [ ] **Step 1: Write the failing repository test**

Create `backend/test/Anela.Heblo.Tests/Features/Bank/BankImportStateRepositoryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Bank;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public sealed class BankImportStateRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly BankImportStateRepository _repository;

    public BankImportStateRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"BankImportStateTests_{Guid.NewGuid()}")
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new BankImportStateRepository(_context);
    }

    [Fact]
    public async Task GetByAccountAsync_ReturnsNull_WhenMissing()
    {
        var result = await _repository.GetByAccountAsync("ComgateCZK");
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_InsertsThenUpdates_SameRow()
    {
        var state = new BankImportState("ComgateCZK");
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);
        await _repository.UpsertAsync(state);

        var loaded = await _repository.GetByAccountAsync("ComgateCZK");
        loaded!.LastValidImportDate.Should().Be(new DateTime(2026, 6, 10));

        loaded.RecordSuccess(new DateTime(2026, 6, 11), DateTime.UtcNow, DateTime.UtcNow);
        await _repository.UpsertAsync(loaded);

        var all = await _repository.GetAllAsync();
        all.Should().HaveCount(1);
        all[0].LastValidImportDate.Should().Be(new DateTime(2026, 6, 11));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~BankImportStateRepositoryTests`
Expected: FAIL — `BankImportStateRepository` / `DbSet` not defined (compile error).

- [ ] **Step 3: Write the EF configuration**

Create `backend/src/Anela.Heblo.Persistence/Features/Bank/BankImportStateConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Bank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Bank;

/// <summary>
/// EF Core configuration for the BankImportState watermark entity.
/// PascalCase columns to match the model snapshot / BankStatementImportConfiguration.
/// </summary>
public class BankImportStateConfiguration : IEntityTypeConfiguration<BankImportState>
{
    public void Configure(EntityTypeBuilder<BankImportState> builder)
    {
        builder.ToTable("BankImportStates", "public");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .HasColumnType("character varying(100)");

        builder.Property(e => e.Account)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("Account")
            .HasColumnType("character varying(100)");

        builder.Property(e => e.LastValidImportDate)
            .HasColumnName("LastValidImportDate")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.LastRunStartedAt)
            .HasColumnName("LastRunStartedAt")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.LastRunFinishedAt)
            .HasColumnName("LastRunFinishedAt")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.LastRunStatus)
            .HasColumnName("LastRunStatus")
            .HasColumnType("character varying(20)");

        builder.Property(e => e.LastErrorMessage)
            .HasColumnName("LastErrorMessage")
            .HasColumnType("character varying(2000)");

        builder.Property(e => e.ConsecutiveFailureCount)
            .IsRequired()
            .HasColumnName("ConsecutiveFailureCount")
            .HasColumnType("integer");

        builder.HasIndex(e => e.Account)
            .IsUnique()
            .HasDatabaseName("IX_BankImportStates_Account");
    }
}
```

- [ ] **Step 4: Write the repository**

Create `backend/src/Anela.Heblo.Persistence/Features/Bank/BankImportStateRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.Bank;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Bank;

public class BankImportStateRepository : IBankImportStateRepository
{
    private readonly ApplicationDbContext _context;

    public BankImportStateRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BankImportState?> GetByAccountAsync(string account, CancellationToken cancellationToken = default)
        => await _context.BankImportStates.FirstOrDefaultAsync(s => s.Account == account, cancellationToken);

    public async Task<IReadOnlyList<BankImportState>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.BankImportStates.AsNoTracking().ToListAsync(cancellationToken);

    public async Task UpsertAsync(BankImportState state, CancellationToken cancellationToken = default)
    {
        var existing = await _context.BankImportStates
            .FirstOrDefaultAsync(s => s.Account == state.Account, cancellationToken);

        if (existing == null)
        {
            await _context.BankImportStates.AddAsync(state, cancellationToken);
        }
        // When `state` was loaded via GetByAccountAsync it is already tracked; EF detects changes.

        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Add the DbSet to ApplicationDbContext**

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, near the other `DbSet` declarations (the Bank one is `public DbSet<BankStatementImport> BankStatements ...`), add:

```csharp
    public DbSet<BankImportState> BankImportStates { get; set; } = null!;
```

Ensure `using Anela.Heblo.Domain.Features.Bank;` is present (it already is, since `BankStatementImport` is referenced). The `OnModelCreating` reflection loop auto-applies `BankImportStateConfiguration` — no manual registration needed.

- [ ] **Step 6: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~BankImportStateRepositoryTests`
Expected: PASS (3 cases).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Bank/BankImportStateConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/Features/Bank/BankImportStateRepository.cs \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/BankImportStateRepositoryTests.cs
git commit -m "feat(bank): persist BankImportState (config, repository, DbSet)"
```

---

## Task 4: Generate the EF migration for `BankImportStates`

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddBankImportState.cs` (+ `.Designer.cs`)
- Modify: `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (auto-updated)

- [ ] **Step 1: Generate the migration**

Run:
```bash
cd backend && dotnet ef migrations add AddBankImportState \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```
Expected: new migration files created under `src/Anela.Heblo.Persistence/Migrations/`, snapshot updated.

- [ ] **Step 2: Verify the migration uses PascalCase**

Open the generated `*_AddBankImportState.cs`. Confirm the `CreateTable` uses table name `"BankImportStates"`, schema `"public"`, and PascalCase columns (`Account`, `LastValidImportDate`, `ConsecutiveFailureCount`, etc.) plus the unique index `IX_BankImportStates_Account`. If anything is off, fix the EF config in Task 3 and regenerate (`dotnet ef migrations remove` then re-add).

- [ ] **Step 3: Build to verify**

Run: `cd backend && dotnet build src/Anela.Heblo.Persistence`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(bank): add migration for BankImportStates table"
```

> Note: migrations are applied **manually** in this project (not in deployment). Application to staging/prod happens in the Verification section.

---

## Task 5: Watermark options

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportWatermarkOptions.cs`

- [ ] **Step 1: Write the options class** (config POCO — no separate test)

Create `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportWatermarkOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public sealed class BankImportWatermarkOptions
{
    public const string SectionName = "BankImportWatermark";

    /// <summary>Maximum number of days a stale watermark may look back. Older data is not imported.</summary>
    public int MaxBackfillDays { get; set; } = 14;

    /// <summary>Watermark lag (in days) above which a Warning is logged.</summary>
    public int StaleWarningDays { get; set; } = 3;
}
```

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportWatermarkOptions.cs
git commit -m "feat(bank): add BankImportWatermarkOptions (14-day cap)"
```

---

## Task 6: Extend `IBankStatementImportRepository` with dedup / bootstrap / upsert methods

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementImport.cs` (add `UpdateImportOutcome`)
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` (append)

- [ ] **Step 1: Write the failing tests** (append to existing `BankStatementImportRepositoryTests.cs`)

Add these methods inside the existing `BankStatementImportRepositoryTests` class:

```csharp
    [Fact]
    public async Task GetExistingTransfersAsync_ReturnsTransferIdToResultMap_InRangeForAccount()
    {
        await SeedAsync("OK1", "ComgateCZK", new DateTime(2026, 6, 10), ImportStatus.Success);
        await SeedAsync("ERR1", "ComgateCZK", new DateTime(2026, 6, 11), $"{ImportStatus.ProcessingError}: x");
        await SeedAsync("OUT", "ComgateCZK", new DateTime(2026, 6, 20), ImportStatus.Success); // out of range
        await SeedAsync("OTHER", "ComgateEUR", new DateTime(2026, 6, 10), ImportStatus.Success); // other account

        var map = await _repository.GetExistingTransfersAsync(
            "ComgateCZK", new DateTime(2026, 6, 10), new DateTime(2026, 6, 12));

        map.Should().HaveCount(2);
        map["OK1"].Should().Be(ImportStatus.Success);
        map["ERR1"].Should().StartWith(ImportStatus.ProcessingError);
    }

    [Fact]
    public async Task GetMaxStatementDateAsync_ReturnsMax_OrNull()
    {
        (await _repository.GetMaxStatementDateAsync("ComgateCZK")).Should().BeNull();

        await SeedAsync("A", "ComgateCZK", new DateTime(2026, 6, 10), ImportStatus.Success);
        await SeedAsync("B", "ComgateCZK", new DateTime(2026, 6, 13), ImportStatus.Success);

        (await _repository.GetMaxStatementDateAsync("ComgateCZK"))!.Value.Date
            .Should().Be(new DateTime(2026, 6, 13));
    }

    [Fact]
    public async Task UpdateAsync_UpdatesExistingRow_InPlace()
    {
        var saved = await SeedAsync("RETRY", "ComgateCZK", new DateTime(2026, 6, 10),
            $"{ImportStatus.ProcessingError}: first");

        var existing = await _repository.GetByTransferIdAsync("RETRY");
        existing!.UpdateImportOutcome(7, ImportStatus.Success);
        await _repository.UpdateAsync(existing);

        var reloaded = await _repository.GetByTransferIdAsync("RETRY");
        reloaded!.Id.Should().Be(saved.Id); // same row, not a new insert
        reloaded.ImportResult.Should().Be(ImportStatus.Success);
        reloaded.ItemCount.Should().Be(7);
    }

    private async Task<BankStatementImport> SeedAsync(
        string transferId, string account, DateTime statementDate, string result)
    {
        var import = new BankStatementImport(transferId, statementDate)
        {
            Account = account,
            Currency = CurrencyCode.CZK,
            ItemCount = 1,
            ImportResult = result,
        };
        return await _repository.AddAsync(import);
    }
```

Add `using Anela.Heblo.Domain.Features.Bank;` and `using FluentAssertions;` to the file's usings if not present. (`CurrencyCode` is in `Anela.Heblo.Domain.Shared` — already imported in the existing file.)

- [ ] **Step 2: Run to verify failure**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~BankStatementImportRepositoryTests`
Expected: FAIL — `GetExistingTransfersAsync` / `GetMaxStatementDateAsync` / `GetByTransferIdAsync` / `UpdateAsync` / `UpdateImportOutcome` not defined.

- [ ] **Step 3: Add `UpdateImportOutcome` to the entity**

In `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementImport.cs`, add this method to the `BankStatementImport` class (after the constructor):

```csharp
    /// <summary>Re-stamp an existing row when a previously-failed statement is retried.</summary>
    public void UpdateImportOutcome(int itemCount, string importResult)
    {
        ItemCount = itemCount;       // validated setter (>= 0)
        ImportResult = importResult; // validated setter (non-null)
        ImportDate = DateTime.UtcNow;
    }
```

- [ ] **Step 4: Extend the interface**

In `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`, add to the interface body:

```csharp
    Task<IReadOnlyDictionary<string, string>> GetExistingTransfersAsync(
        string account, DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default);

    Task<DateTime?> GetMaxStatementDateAsync(string account, CancellationToken cancellationToken = default);

    Task<BankStatementImport?> GetByTransferIdAsync(string transferId, CancellationToken cancellationToken = default);

    Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement);
```

- [ ] **Step 5: Implement in the repository**

In `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`, add these methods (before the private `EscapeLike`):

```csharp
    public async Task<IReadOnlyDictionary<string, string>> GetExistingTransfersAsync(
        string account, DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default)
    {
        return await _context.BankStatements
            .AsNoTracking()
            .Where(bs => bs.Account == account
                && bs.StatementDate.Date >= dateFrom.Date
                && bs.StatementDate.Date <= dateTo.Date)
            .Select(bs => new { bs.TransferId, bs.ImportResult })
            .ToDictionaryAsync(x => x.TransferId, x => x.ImportResult, cancellationToken);
    }

    public async Task<DateTime?> GetMaxStatementDateAsync(string account, CancellationToken cancellationToken = default)
    {
        var query = _context.BankStatements.AsNoTracking().Where(bs => bs.Account == account);
        if (!await query.AnyAsync(cancellationToken))
            return null;
        return await query.MaxAsync(bs => bs.StatementDate, cancellationToken);
    }

    public async Task<BankStatementImport?> GetByTransferIdAsync(
        string transferId, CancellationToken cancellationToken = default)
        => await _context.BankStatements.FirstOrDefaultAsync(bs => bs.TransferId == transferId, cancellationToken);

    public async Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement)
    {
        _context.BankStatements.Update(bankStatement);
        await _context.SaveChangesAsync();
        return bankStatement;
    }
```

- [ ] **Step 6: Run to verify pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~BankStatementImportRepositoryTests`
Expected: PASS (existing + 3 new).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs \
        backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementImport.cs \
        backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs
git commit -m "feat(bank): repository dedup, bootstrap, and upsert helpers"
```

---

## Task 7: Idempotent import handler (dedup + upsert + counts)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` (append)

- [ ] **Step 1: Add counts to the response**

Replace the body of `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementResponse.cs` with:

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;

public class ImportBankStatementResponse : BaseResponse
{
    public List<BankStatementImportDto> Statements { get; set; } = new List<BankStatementImportDto>();

    /// <summary>Newly-attempted statements that imported successfully this run.</summary>
    public int SuccessCount { get; set; }

    /// <summary>Newly-attempted statements that failed this run.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Statements skipped because they were already imported successfully.</summary>
    public int SkippedCount { get; set; }

    public bool HasErrors => ErrorCount > 0;
}
```

- [ ] **Step 2: Write the failing handler tests** (append methods to `ImportBankStatementHandlerTests`)

Add to `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Handle_SkipsAlreadySucceededStatements_NoFlexiBeePush()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 10);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>
            {
                new BankStatementHeader { StatementId = "DONE", Date = from },
            });
        _mockRepository.Setup(r => r.GetExistingTransfersAsync("ComgateCZK", from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["DONE"] = ImportStatus.Success });

        var response = await _handler.Handle(request, CancellationToken.None);

        response.SkippedCount.Should().Be(1);
        response.SuccessCount.Should().Be(0);
        response.ErrorCount.Should().Be(0);
        _mockBankClient.Verify(x => x.GetStatementAsync(It.IsAny<string>()), Times.Never);
        _mockImportService.Verify(
            x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RetriesPreviouslyFailedStatement_ViaUpdateNotAdd()
    {
        var from = new DateTime(2026, 6, 10);
        var to = new DateTime(2026, 6, 10);
        var request = new ImportBankStatementRequest("ComgateCZK", from, to);
        var existingRow = new BankStatementImport("RETRY", from);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", from, to))
            .ReturnsAsync(new List<BankStatementHeader>
            {
                new BankStatementHeader { StatementId = "RETRY", Date = from },
            });
        _mockRepository.Setup(r => r.GetExistingTransfersAsync("ComgateCZK", from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["RETRY"] = $"{ImportStatus.ProcessingError}: old" });
        _mockBankClient.Setup(x => x.GetStatementAsync("RETRY"))
            .ReturnsAsync(new BankStatementData { Data = "abo", ItemCount = 3 });
        _mockImportService.Setup(x => x.ImportStatementAsync(1, "abo"))
            .ReturnsAsync(Result<bool>.Success(true));
        _mockRepository.Setup(r => r.GetByTransferIdAsync("RETRY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRow);
        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<BankStatementImport>()))
            .ReturnsAsync((BankStatementImport b) => b);
        _mockMapper.Setup(m => m.Map<Anela.Heblo.Application.Features.Bank.Contracts.BankStatementImportDto>(
                It.IsAny<BankStatementImport>()))
            .Returns(new Anela.Heblo.Application.Features.Bank.Contracts.BankStatementImportDto
            {
                TransferId = "RETRY", ImportResult = ImportStatus.Success,
            });

        var response = await _handler.Handle(request, CancellationToken.None);

        response.SuccessCount.Should().Be(1);
        response.ErrorCount.Should().Be(0);
        _mockRepository.Verify(r => r.UpdateAsync(It.IsAny<BankStatementImport>()), Times.Once);
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<BankStatementImport>()), Times.Never);
    }
```

Add `using FluentAssertions;` to the test file's usings (the existing file uses raw `Assert`; FluentAssertions is available project-wide). `Result<bool>` and `BankStatementData`/`BankStatementHeader` live in `Anela.Heblo.Domain.Features.Bank` / `Anela.Heblo.Domain.Shared` — both already imported in the file. Verify the `Result<bool>.Success(...)` factory name by opening `backend/src/Anela.Heblo.Domain/Shared/Result.cs`; adjust the call if the factory differs.

- [ ] **Step 3: Run to verify failure**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~ImportBankStatementHandlerTests`
Expected: FAIL — handler doesn't dedup/upsert/set counts yet (and `GetExistingTransfersAsync` isn't called).

- [ ] **Step 4: Rewrite the handler**

Replace the body of `Handle` and add the private helpers in `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`. The class fields/constructor stay unchanged. Replace from `public async Task<ImportBankStatementResponse> Handle(...)` through the end of the class with:

```csharp
    public async Task<ImportBankStatementResponse> Handle(ImportBankStatementRequest request, CancellationToken cancellationToken)
    {
        var totalSw = Stopwatch.StartNew();

        _logger.LogInformation(
            "Bank import START - Account: {AccountName}, DateFrom: {DateFrom}, DateTo: {DateTo}",
            request.AccountName, request.DateFrom, request.DateTo);

        var accountSetting = _bankSettings.Accounts?.SingleOrDefault(a => a.Name == request.AccountName);
        if (accountSetting == null)
        {
            var availableAccounts = _bankSettings.Accounts != null
                ? string.Join(", ", _bankSettings.Accounts.Select(a => a.Name))
                : "None";

            _logger.LogError(
                "Bank import FAILED - Account not found: {AccountName}. Available accounts: {AvailableAccounts}",
                request.AccountName, availableAccounts);

            throw new ArgumentException(
                $"Account name {request.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration. Available accounts: {availableAccounts}");
        }

        var client = _factory.GetClient(accountSetting);

        var statements = await client.GetStatementsAsync(accountSetting.AccountNumber, request.DateFrom, request.DateTo);

        _logger.LogInformation(
            "Bank client returned {StatementCount} statements - Account: {AccountName}",
            statements.Count, request.AccountName);

        var existingTransfers = await _repository.GetExistingTransfersAsync(
            accountSetting.Name, request.DateFrom, request.DateTo, cancellationToken);

        var imports = new List<BankStatementImportDto>();
        var skippedCount = 0;

        foreach (var statement in statements)
        {
            if (existingTransfers.TryGetValue(statement.StatementId, out var existingResult)
                && existingResult == ImportStatus.Success)
            {
                skippedCount++;
                _logger.LogDebug("Skipping already-imported statement {StatementId}", statement.StatementId);
                continue;
            }

            var isRetry = existingTransfers.ContainsKey(statement.StatementId);
            imports.Add(await ProcessStatementAsync(client, statement, accountSetting, isRetry, cancellationToken));
        }

        totalSw.Stop();

        var successCount = imports.Count(i => i.ImportResult == ImportStatus.Success);
        var errorCount = imports.Count - successCount;

        _logger.LogInformation(
            "Bank import COMPLETED - Account: {AccountName}, Attempted: {Attempted}, Success: {SuccessCount}, Errors: {ErrorCount}, Skipped: {SkippedCount}, Duration: {Duration}ms",
            request.AccountName, imports.Count, successCount, errorCount, skippedCount, totalSw.ElapsedMilliseconds);

        return new ImportBankStatementResponse
        {
            Statements = imports,
            SuccessCount = successCount,
            ErrorCount = errorCount,
            SkippedCount = skippedCount,
        };
    }

    private async Task<BankStatementImportDto> ProcessStatementAsync(
        IBankClient client,
        BankStatementHeader statement,
        BankAccountConfiguration accountSetting,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing statement {StatementId} (retry={IsRetry})", statement.StatementId, isRetry);

            var aboData = await client.GetStatementAsync(statement.StatementId);
            var importResult = await _bankStatementImportService.ImportStatementAsync(accountSetting.FlexiBeeId, aboData.Data);
            var resultStatus = importResult.IsSuccess
                ? ImportStatus.Success
                : importResult.ErrorMessage ?? ImportStatus.UnknownError;

            var saved = isRetry
                ? await UpsertExistingAsync(statement, accountSetting, aboData.ItemCount, resultStatus, cancellationToken)
                : await InsertNewAsync(statement, accountSetting, aboData.ItemCount, resultStatus);

            _logger.LogInformation("Processed statement {StatementId} with result: {Result}",
                statement.StatementId, resultStatus);
            return _mapper.Map<BankStatementImportDto>(saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing statement {StatementId}", statement.StatementId);
            var errorStatus = $"{ImportStatus.ProcessingError}: {ex.Message}";
            var saved = isRetry
                ? await UpsertExistingAsync(statement, accountSetting, 0, errorStatus, cancellationToken)
                : await InsertNewAsync(statement, accountSetting, 0, errorStatus);
            return _mapper.Map<BankStatementImportDto>(saved);
        }
    }

    private async Task<BankStatementImport> InsertNewAsync(
        BankStatementHeader statement,
        BankAccountConfiguration accountSetting,
        int itemCount,
        string resultStatus)
    {
        var import = new BankStatementImport(statement.StatementId, statement.Date)
        {
            Account = accountSetting.Name,
            Currency = accountSetting.Currency,
            ItemCount = itemCount,
            ImportResult = resultStatus,
        };
        return await _repository.AddAsync(import);
    }

    private async Task<BankStatementImport> UpsertExistingAsync(
        BankStatementHeader statement,
        BankAccountConfiguration accountSetting,
        int itemCount,
        string resultStatus,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByTransferIdAsync(statement.StatementId, cancellationToken);
        if (existing == null)
            return await InsertNewAsync(statement, accountSetting, itemCount, resultStatus);

        existing.Account = accountSetting.Name;
        existing.Currency = accountSetting.Currency;
        existing.UpdateImportOutcome(itemCount, resultStatus);
        return await _repository.UpdateAsync(existing);
    }
```

Add `using Anela.Heblo.Domain.Features.Bank;` if not already present (it is — `IBankStatementImportRepository` etc. come from there). `IBankClient`, `BankStatementHeader`, `BankAccountConfiguration` are all in `Anela.Heblo.Domain.Features.Bank`.

- [ ] **Step 5: Run to verify pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~ImportBankStatementHandlerTests`
Expected: PASS (existing + 2 new).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ \
        backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs
git commit -m "feat(bank): idempotent import — dedup succeeded, upsert retried, expose counts"
```

---

## Task 8: Make `BankImportJobBase` watermark-aware + simplify the 3 jobs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs`
- Modify: `.../Jobs/ComgateCzkImportJob.cs`, `ComgateEurImportJob.cs`, `ShoptetPayImportJob.cs`
- Delete: `.../Jobs/BankImportJobParameters.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` (register repo + options)
- Test: rewrite `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/BankImportJobBaseTests.cs` and the 3 job tests

> This task changes the base class's public surface (removes `GetParameters()`/`BankImportJobParameters`, adds 3 ctor deps), so the impl and the affected tests change together.

- [ ] **Step 1: Rewrite the base-class test first (RED)**

Replace the entire contents of `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/BankImportJobBaseTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank.Infrastructure.Jobs;

public sealed class BankImportJobBaseTests
{
    private const string TestJobName = "test-bank-import-job";
    private const string TestAccountName = "TestAccount";

    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();
    private readonly Mock<IBankImportStateRepository> _stateRepo = new();
    private readonly Mock<IBankStatementImportRepository> _statementRepo = new();
    private readonly BankImportWatermarkOptions _options = new() { MaxBackfillDays = 14, StaleWarningDays = 3 };

    public BankImportJobBaseTests()
    {
        _statusChecker.Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportBankStatementResponse());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEarly_WhenJobIsDisabled()
    {
        _statusChecker.Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        _mediator.Verify(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _stateRepo.Verify(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UsesWatermarkAsDateFrom_AndTargetAsDateTo()
    {
        var state = new BankImportState(TestAccountName);
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        ImportBankStatementRequest? captured = null;
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ImportBankStatementResponse>, CancellationToken>((req, _) => captured = (ImportBankStatementRequest)req)
            .ReturnsAsync(new ImportBankStatementResponse { SuccessCount = 1 });

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        captured!.AccountName.Should().Be(TestAccountName);
        captured.DateFrom.Should().Be(new DateTime(2026, 6, 10));
        captured.DateTo.Should().Be(new DateTime(2026, 6, 14));
    }

    [Fact]
    public async Task ExecuteAsync_Bootstraps_FromMaxStatementDate_WhenNoWatermark()
    {
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankImportState?)null);
        _statementRepo.Setup(r => r.GetMaxStatementDateAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateTime(2026, 6, 12));

        ImportBankStatementRequest? captured = null;
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ImportBankStatementResponse>, CancellationToken>((req, _) => captured = (ImportBankStatementRequest)req)
            .ReturnsAsync(new ImportBankStatementResponse());

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        captured!.DateFrom.Should().Be(new DateTime(2026, 6, 12));
    }

    [Fact]
    public async Task ExecuteAsync_ClampsDateFrom_ToMaxBackfillDays()
    {
        var state = new BankImportState(TestAccountName);
        state.RecordSuccess(new DateTime(2026, 5, 1), DateTime.UtcNow, DateTime.UtcNow); // ~44 days behind
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        ImportBankStatementRequest? captured = null;
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ImportBankStatementResponse>, CancellationToken>((req, _) => captured = (ImportBankStatementRequest)req)
            .ReturnsAsync(new ImportBankStatementResponse());

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        captured!.DateFrom.Should().Be(new DateTime(2026, 6, 14).AddDays(-14)); // clamped
    }

    [Fact]
    public async Task ExecuteAsync_AdvancesWatermark_OnZeroErrorRun()
    {
        var state = new BankImportState(TestAccountName);
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportBankStatementResponse { SuccessCount = 0, ErrorCount = 0 }); // 0 docs = valid

        BankImportState? saved = null;
        _stateRepo.Setup(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()))
            .Callback<BankImportState, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        saved!.LastValidImportDate.Should().Be(new DateTime(2026, 6, 14));
        saved.LastRunStatus.Should().Be(BankImportState.StatusOk);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotAdvanceWatermark_OnErrorRun()
    {
        var state = new BankImportState(TestAccountName);
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);
        _stateRepo.Setup(r => r.GetByAccountAsync(TestAccountName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mediator.Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportBankStatementResponse { SuccessCount = 1, ErrorCount = 2 });

        BankImportState? saved = null;
        _stateRepo.Setup(r => r.UpsertAsync(It.IsAny<BankImportState>(), It.IsAny<CancellationToken>()))
            .Callback<BankImportState, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);

        await CreateJob(targetEnd: new DateTime(2026, 6, 14)).ExecuteAsync(CancellationToken.None);

        saved!.LastValidImportDate.Should().Be(new DateTime(2026, 6, 10)); // unchanged
        saved.LastRunStatus.Should().Be(BankImportState.StatusError);
    }

    [Fact]
    public void Constructor_Throws_WhenStateRepositoryIsNull()
    {
        var act = () => new TestBankImportJob(
            _mediator.Object, NullLoggerFactory.Instance, _statusChecker.Object,
            stateRepository: null!, _statementRepo.Object, Options.Create(_options),
            TestAccountName, new DateTime(2026, 6, 14), TestJobName);
        act.Should().Throw<ArgumentNullException>().WithParameterName("stateRepository");
    }

    private TestBankImportJob CreateJob(DateTime targetEnd) => new(
        _mediator.Object, NullLoggerFactory.Instance, _statusChecker.Object,
        _stateRepo.Object, _statementRepo.Object, Options.Create(_options),
        TestAccountName, targetEnd, TestJobName);

    private sealed class TestBankImportJob : BankImportJobBase
    {
        private readonly string _accountName;
        private readonly DateTime _targetEnd;

        public TestBankImportJob(
            IMediator mediator,
            ILoggerFactory loggerFactory,
            IRecurringJobStatusChecker statusChecker,
            IBankImportStateRepository stateRepository,
            IBankStatementImportRepository statementRepository,
            IOptions<BankImportWatermarkOptions> options,
            string accountName,
            DateTime targetEnd,
            string jobName)
            : base(mediator, loggerFactory, statusChecker, stateRepository, statementRepository, options)
        {
            _accountName = accountName;
            _targetEnd = targetEnd;
            Metadata = new RecurringJobMetadata
            {
                JobName = jobName,
                DisplayName = "Test Bank Import Job",
                Description = "Test job for BankImportJobBase",
                CronExpression = "0 0 * * *",
                DefaultIsEnabled = true,
            };
        }

        public override RecurringJobMetadata Metadata { get; }
        protected override string AccountName => _accountName;
        protected override DateTime GetTargetEndDate(DateTime today) => _targetEnd;
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~BankImportJobBaseTests`
Expected: FAIL (compile) — new ctor/members don't exist yet.

- [ ] **Step 3: Rewrite `BankImportJobBase`**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public abstract class BankImportJobBase : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IBankImportStateRepository _stateRepository;
    private readonly IBankStatementImportRepository _statementRepository;
    private readonly BankImportWatermarkOptions _options;
    private readonly ILogger _logger;

    public abstract RecurringJobMetadata Metadata { get; }

    /// <summary>Account name (must match BankAccountSettings.Accounts[].Name).</summary>
    protected abstract string AccountName { get; }

    /// <summary>Inclusive end of the import window for this job (e.g. yesterday or today).</summary>
    protected abstract DateTime GetTargetEndDate(DateTime today);

    protected BankImportJobBase(
        IMediator mediator,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker,
        IBankImportStateRepository stateRepository,
        IBankStatementImportRepository statementRepository,
        IOptions<BankImportWatermarkOptions> options)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(statusChecker);
        ArgumentNullException.ThrowIfNull(stateRepository);
        ArgumentNullException.ThrowIfNull(statementRepository);
        ArgumentNullException.ThrowIfNull(options);

        _mediator = mediator;
        _statusChecker = statusChecker;
        _stateRepository = stateRepository;
        _statementRepository = statementRepository;
        _options = options.Value;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        var runStartedAt = DateTime.UtcNow;
        var targetEnd = GetTargetEndDate(DateTime.Today);
        var state = await _stateRepository.GetByAccountAsync(AccountName, cancellationToken)
                    ?? new BankImportState(AccountName);
        var dateFrom = await ResolveDateFromAsync(state, targetEnd, cancellationToken);

        try
        {
            _logger.LogInformation(
                "Starting {JobName} - Account: {Account}, DateFrom: {DateFrom}, DateTo: {DateTo}",
                Metadata.JobName, AccountName, dateFrom, targetEnd);

            var response = await _mediator.Send(
                new ImportBankStatementRequest(AccountName, dateFrom, targetEnd), cancellationToken);

            if (response.HasErrors)
            {
                state.RecordFailure(
                    $"{response.ErrorCount} statement(s) failed in range {dateFrom:yyyy-MM-dd}..{targetEnd:yyyy-MM-dd}",
                    runStartedAt, DateTime.UtcNow);
                _logger.LogError(
                    "{JobName} completed WITH ERRORS - Success: {Success}, Errors: {Errors}, Skipped: {Skipped}. Watermark NOT advanced (stuck at {Watermark:yyyy-MM-dd}).",
                    Metadata.JobName, response.SuccessCount, response.ErrorCount, response.SkippedCount, state.LastValidImportDate);
            }
            else
            {
                state.RecordSuccess(targetEnd, runStartedAt, DateTime.UtcNow);
                _logger.LogInformation(
                    "{JobName} completed - Success: {Success}, Skipped: {Skipped}. Watermark advanced to {Watermark:yyyy-MM-dd}.",
                    Metadata.JobName, response.SuccessCount, response.SkippedCount, targetEnd);
            }

            await _stateRepository.UpsertAsync(state, cancellationToken);
        }
        catch (Exception ex)
        {
            state.RecordFailure(ex.Message, runStartedAt, DateTime.UtcNow);
            await _stateRepository.UpsertAsync(state, cancellationToken);
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }

    private async Task<DateTime> ResolveDateFromAsync(
        BankImportState state, DateTime targetEnd, CancellationToken cancellationToken)
    {
        DateTime dateFrom;
        if (state.LastValidImportDate.HasValue)
        {
            dateFrom = state.LastValidImportDate.Value.Date;
        }
        else
        {
            var maxExisting = await _statementRepository.GetMaxStatementDateAsync(AccountName, cancellationToken);
            dateFrom = maxExisting?.Date ?? targetEnd;
            _logger.LogInformation(
                "{JobName} bootstrap - no watermark; derived DateFrom {DateFrom:yyyy-MM-dd} from existing data.",
                Metadata.JobName, dateFrom);
        }

        if (dateFrom > targetEnd)
        {
            dateFrom = targetEnd;
        }

        var span = (targetEnd.Date - dateFrom.Date).Days;
        if (span > _options.MaxBackfillDays)
        {
            var capped = targetEnd.Date.AddDays(-_options.MaxBackfillDays);
            _logger.LogError(
                "{JobName} watermark is {Span} days behind (>{Cap}). Clamping DateFrom {Original:yyyy-MM-dd} -> {Capped:yyyy-MM-dd}. Earlier data will NOT be imported.",
                Metadata.JobName, span, _options.MaxBackfillDays, dateFrom, capped);
            dateFrom = capped;
        }
        else if (span > _options.StaleWarningDays)
        {
            _logger.LogWarning(
                "{JobName} watermark is {Span} days behind; importing range {DateFrom:yyyy-MM-dd}..{DateTo:yyyy-MM-dd}.",
                Metadata.JobName, span, dateFrom, targetEnd);
        }

        return dateFrom;
    }
}
```

- [ ] **Step 4: Delete `BankImportJobParameters.cs`**

Run: `git rm backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobParameters.cs`

- [ ] **Step 5: Simplify the 3 concrete jobs**

Replace `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateCzkImportJob.cs`:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public sealed class ComgateCzkImportJob : BankImportJobBase
{
    public override RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-comgate-czk-import",
        DisplayName = "Daily Comgate CZK Import",
        Description = "Imports Comgate CZK payment statements from previous day",
        CronExpression = "30 4 * * *",
        DefaultIsEnabled = true,
    };

    public ComgateCzkImportJob(
        IMediator mediator,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker,
        IBankImportStateRepository stateRepository,
        IBankStatementImportRepository statementRepository,
        IOptions<BankImportWatermarkOptions> options)
        : base(mediator, loggerFactory, statusChecker, stateRepository, statementRepository, options)
    {
    }

    protected override string AccountName => BankAccountNames.ComgateCzk;
    protected override DateTime GetTargetEndDate(DateTime today) => today.AddDays(-1);
}
```

Replace `ComgateEurImportJob.cs` identically but with the EUR metadata (`daily-comgate-eur-import`, `Daily Comgate EUR Import`, `Imports Comgate EUR payment statements from previous day`, `40 4 * * *`), `AccountName => BankAccountNames.ComgateEur`, and `GetTargetEndDate(today) => today.AddDays(-1)`.

Replace `ShoptetPayImportJob.cs` identically but with the ShoptetPay metadata (`daily-shoptetpay-czk-import`, `Daily ShoptetPay CZK Import`, `Imports ShoptetPay CZK payment statements from current day`, `50 4 * * *`), `AccountName => BankAccountNames.ShoptetPayCzk`, and `GetTargetEndDate(today) => today` (today, NOT yesterday).

- [ ] **Step 6: Register the new dependencies in `BankModule`**

In `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs`, add the options/repo registrations. Add `using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;` to the usings, then after the existing `services.AddScoped<IBankStatementImportRepository, BankStatementImportRepository>();` line add:

```csharp
        services.AddScoped<IBankImportStateRepository, BankImportStateRepository>();
        services.Configure<BankImportWatermarkOptions>(
            configuration.GetSection(BankImportWatermarkOptions.SectionName));
```

(`BankImportStateRepository` is in `Anela.Heblo.Persistence.Features.Bank`, already imported in this file.)

- [ ] **Step 7: Rewrite the 3 job tests**

Replace `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/ComgateCzkImportJobTests.cs`:

```csharp
using System.Reflection;
using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank.Infrastructure.Jobs;

public sealed class ComgateCzkImportJobTests
{
    [Fact]
    public void Metadata_MatchesContractValues()
    {
        var job = CreateJob();

        job.Metadata.JobName.Should().Be("daily-comgate-czk-import");
        job.Metadata.DisplayName.Should().Be("Daily Comgate CZK Import");
        job.Metadata.Description.Should().Be("Imports Comgate CZK payment statements from previous day");
        job.Metadata.CronExpression.Should().Be("30 4 * * *");
        job.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public void AccountName_IsComgateCzk_WireContractStable()
    {
        GetAccountName(CreateJob()).Should().Be(BankAccountNames.ComgateCzk).And.Be("ComgateCZK");
    }

    [Fact]
    public void GetTargetEndDate_ReturnsYesterday()
    {
        var today = new DateTime(2026, 6, 15);
        InvokeGetTargetEndDate(CreateJob(), today).Should().Be(new DateTime(2026, 6, 14));
    }

    private static ComgateCzkImportJob CreateJob() => new(
        Mock.Of<IMediator>(),
        NullLoggerFactory.Instance,
        Mock.Of<IRecurringJobStatusChecker>(),
        Mock.Of<IBankImportStateRepository>(),
        Mock.Of<IBankStatementImportRepository>(),
        Options.Create(new BankImportWatermarkOptions()));

    private static string GetAccountName(BankImportJobBase job) =>
        (string)typeof(BankImportJobBase)
            .GetProperty("AccountName", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(job)!;

    private static DateTime InvokeGetTargetEndDate(BankImportJobBase job, DateTime today) =>
        (DateTime)typeof(BankImportJobBase)
            .GetMethod("GetTargetEndDate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(job, new object[] { today })!;
}
```

Replace `ComgateEurImportJobTests.cs` the same way, asserting the EUR metadata, `BankAccountNames.ComgateEur`/`"ComgateEUR"`, and `GetTargetEndDate(2026-06-15) == 2026-06-14`.

Replace `ShoptetPayImportJobTests.cs` the same way, asserting the ShoptetPay metadata, `BankAccountNames.ShoptetPayCzk`/`"ShoptetPay-CZK"`, and `GetTargetEndDate(2026-06-15) == 2026-06-15` (today).

- [ ] **Step 8: Run all Bank job + handler tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Bank"`
Expected: PASS. If `BankImportJobDiscoveryTests` fails to construct jobs, update its job instantiation to pass the new ctor args (mirror `CreateJob` above).

- [ ] **Step 9: Build + format**

Run: `cd backend && dotnet build && dotnet format`
Expected: Build succeeded; format clean.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/ \
        backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/
git commit -m "feat(bank): watermark-aware import jobs (import since last valid date)"
```

---

## Task 9: `GetBankImportState` read use case + endpoint

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankImportStateDto.cs`
- Create: `.../UseCases/GetBankImportState/GetBankImportStateRequest.cs`
- Create: `.../UseCases/GetBankImportState/GetBankImportStateResponse.cs`
- Create: `.../UseCases/GetBankImportState/GetBankImportStateHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankImportStateHandlerTests.cs`

- [ ] **Step 1: Write the DTO + request + response**

Create `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankImportStateDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Bank.Contracts;

public class BankImportStateDto
{
    public string Account { get; set; } = null!;
    public DateTime? LastValidImportDate { get; set; }
    public int? StaleDays { get; set; }

    /// <summary>"Fresh" | "Behind" | "Stale" | "Unknown".</summary>
    public string Status { get; set; } = null!;

    public DateTime? LastRunFinishedAt { get; set; }
    public string? LastErrorMessage { get; set; }
    public int ConsecutiveFailureCount { get; set; }
}
```

Create `.../UseCases/GetBankImportState/GetBankImportStateRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankImportState;

public class GetBankImportStateRequest : IRequest<GetBankImportStateResponse>
{
}
```

Create `.../UseCases/GetBankImportState/GetBankImportStateResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankImportState;

public class GetBankImportStateResponse : BaseResponse
{
    public List<BankImportStateDto> Accounts { get; set; } = new List<BankImportStateDto>();
}
```

- [ ] **Step 2: Write the failing handler test**

Create `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankImportStateHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankImportState;
using Anela.Heblo.Domain.Features.Bank;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public sealed class GetBankImportStateHandlerTests
{
    private readonly Mock<IBankImportStateRepository> _stateRepo = new();
    private readonly BankAccountSettings _settings = new()
    {
        Accounts = new List<BankAccountConfiguration>
        {
            new() { Name = "ComgateCZK", Provider = BankClientProvider.Comgate, AccountNumber = "1", FlexiBeeId = 1, Currency = Anela.Heblo.Domain.Shared.CurrencyCode.CZK },
            new() { Name = "ComgateEUR", Provider = BankClientProvider.Comgate, AccountNumber = "2", FlexiBeeId = 2, Currency = Anela.Heblo.Domain.Shared.CurrencyCode.EUR },
        }
    };
    private readonly BankImportWatermarkOptions _options = new() { MaxBackfillDays = 14, StaleWarningDays = 3 };

    private GetBankImportStateHandler CreateHandler() => new(
        _stateRepo.Object, Options.Create(_settings), Options.Create(_options));

    [Fact]
    public async Task Handle_ReturnsUnknown_ForAccountWithoutState()
    {
        _stateRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BankImportState>());

        var response = await CreateHandler().Handle(new GetBankImportStateRequest(), CancellationToken.None);

        response.Accounts.Should().HaveCount(2);
        response.Accounts.Should().OnlyContain(a => a.Status == "Unknown");
    }

    [Fact]
    public async Task Handle_ComputesStaleStatus_FromWatermarkLag()
    {
        var fresh = new BankImportState("ComgateCZK");
        fresh.RecordSuccess(DateTime.Today.AddDays(-1), DateTime.UtcNow, DateTime.UtcNow);
        var stale = new BankImportState("ComgateEUR");
        stale.RecordSuccess(DateTime.Today.AddDays(-20), DateTime.UtcNow, DateTime.UtcNow);

        _stateRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BankImportState> { fresh, stale });

        var response = await CreateHandler().Handle(new GetBankImportStateRequest(), CancellationToken.None);

        response.Accounts.Single(a => a.Account == "ComgateCZK").Status.Should().Be("Fresh");
        response.Accounts.Single(a => a.Account == "ComgateEUR").Status.Should().Be("Stale");
        response.Accounts.Single(a => a.Account == "ComgateEUR").StaleDays.Should().Be(20);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~GetBankImportStateHandlerTests`
Expected: FAIL — handler not defined.

- [ ] **Step 4: Write the handler**

Create `.../UseCases/GetBankImportState/GetBankImportStateHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.Bank;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankImportState;

public class GetBankImportStateHandler : IRequestHandler<GetBankImportStateRequest, GetBankImportStateResponse>
{
    private const string StatusFresh = "Fresh";
    private const string StatusBehind = "Behind";
    private const string StatusStale = "Stale";
    private const string StatusUnknown = "Unknown";

    private readonly IBankImportStateRepository _stateRepository;
    private readonly BankAccountSettings _bankSettings;
    private readonly BankImportWatermarkOptions _options;

    public GetBankImportStateHandler(
        IBankImportStateRepository stateRepository,
        IOptions<BankAccountSettings> bankSettings,
        IOptions<BankImportWatermarkOptions> options)
    {
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
        _bankSettings = bankSettings?.Value ?? throw new ArgumentNullException(nameof(bankSettings));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<GetBankImportStateResponse> Handle(
        GetBankImportStateRequest request, CancellationToken cancellationToken)
    {
        var states = (await _stateRepository.GetAllAsync(cancellationToken))
            .ToDictionary(s => s.Account, StringComparer.Ordinal);

        var today = DateTime.Today;
        var accounts = (_bankSettings.Accounts ?? new List<BankAccountConfiguration>())
            .Select(a => MapState(a.Name, states.GetValueOrDefault(a.Name), today))
            .ToList();

        return new GetBankImportStateResponse { Accounts = accounts };
    }

    private BankImportStateDto MapState(string account, BankImportState? state, DateTime today)
    {
        if (state?.LastValidImportDate is null)
        {
            return new BankImportStateDto
            {
                Account = account,
                Status = StatusUnknown,
                LastRunFinishedAt = state?.LastRunFinishedAt,
                LastErrorMessage = state?.LastErrorMessage,
                ConsecutiveFailureCount = state?.ConsecutiveFailureCount ?? 0,
            };
        }

        var staleDays = (today.Date - state.LastValidImportDate.Value.Date).Days;
        var status = staleDays > _options.MaxBackfillDays
            ? StatusStale
            : staleDays > _options.StaleWarningDays
                ? StatusBehind
                : StatusFresh;

        return new BankImportStateDto
        {
            Account = account,
            LastValidImportDate = state.LastValidImportDate,
            StaleDays = staleDays,
            Status = status,
            LastRunFinishedAt = state.LastRunFinishedAt,
            LastErrorMessage = state.LastErrorMessage,
            ConsecutiveFailureCount = state.ConsecutiveFailureCount,
        };
    }
}
```

- [ ] **Step 5: Add the controller endpoint**

In `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`, add `using Anela.Heblo.Application.Features.Bank.UseCases.GetBankImportState;` to the usings, and add this action after `GetAccounts`:

```csharp
    /// <summary>
    /// Get per-account import watermark state (last valid import date + staleness).
    /// </summary>
    [HttpGet("import-state")]
    public async Task<ActionResult<IEnumerable<BankImportStateDto>>> GetImportState(CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetBankImportStateRequest(), cancellationToken);
        return Ok(response.Accounts);
    }
```

- [ ] **Step 6: Run to verify pass + build**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter FullyQualifiedName~GetBankImportStateHandlerTests`
Expected: PASS (2 cases).
Run: `cd backend && dotnet build`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankImportStateDto.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankImportState/ \
        backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/GetBankImportStateHandlerTests.cs
git commit -m "feat(bank): expose per-account import watermark state via API"
```

---

## Task 10: Frontend — watermark hook + staleness badge

**Files:**
- Modify: `frontend/src/api/hooks/useBankStatements.ts`
- Create: `frontend/src/components/customer/BankImportStateBadges.tsx`
- Modify: `frontend/src/components/customer/tabs/ImportTab.tsx`

- [ ] **Step 1: Add the hook**

In `frontend/src/api/hooks/useBankStatements.ts`, append after `useBankStatementAccounts`:

```typescript
export interface BankImportStateDto {
  account: string;
  lastValidImportDate: string | null;
  staleDays: number | null;
  status: 'Fresh' | 'Behind' | 'Stale' | 'Unknown';
  lastRunFinishedAt: string | null;
  lastErrorMessage: string | null;
  consecutiveFailureCount: number;
}

// Get per-account import watermark state from the backend
export const useBankImportState = () => {
  return useQuery({
    queryKey: [...QUERY_KEYS.bankStatements, 'import-state'],
    queryFn: async (): Promise<BankImportStateDto[]> => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/bank-statements/import-state`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return await response.json();
    },
    staleTime: 5 * 60 * 1000,
  });
};
```

- [ ] **Step 2: Create the badge component**

Create `frontend/src/components/customer/BankImportStateBadges.tsx`:

```typescript
import React from 'react';
import { useBankImportState, BankImportStateDto } from '../../api/hooks/useBankStatements';

const STATUS_STYLES: Record<BankImportStateDto['status'], string> = {
  Fresh: 'bg-green-100 text-green-800 border-green-200',
  Behind: 'bg-amber-100 text-amber-800 border-amber-200',
  Stale: 'bg-red-100 text-red-800 border-red-200',
  Unknown: 'bg-gray-100 text-gray-600 border-gray-200',
};

const STATUS_LABEL: Record<BankImportStateDto['status'], string> = {
  Fresh: 'Aktuální',
  Behind: 'Zpožděno',
  Stale: 'Zastaralé',
  Unknown: 'Neznámé',
};

function formatDate(value: string | null): string {
  if (!value) return '—';
  return new Date(value).toLocaleDateString('cs-CZ');
}

export function BankImportStateBadges() {
  const { data, isLoading, isError } = useBankImportState();

  if (isLoading || isError || !data || data.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap gap-2 mt-2" data-testid="bank-import-state-badges">
      {data.map((s) => (
        <span
          key={s.account}
          title={s.lastErrorMessage ?? undefined}
          className={`inline-flex items-center gap-1 rounded-md border px-2 py-1 text-xs font-medium ${STATUS_STYLES[s.status]}`}
        >
          <span className="font-semibold">{s.account}</span>
          <span>· {formatDate(s.lastValidImportDate)}</span>
          <span>· {STATUS_LABEL[s.status]}</span>
          {s.staleDays != null && s.staleDays > 0 && <span>({s.staleDays} d)</span>}
        </span>
      ))}
    </div>
  );
}
```

- [ ] **Step 3: Render the badges in ImportTab**

In `frontend/src/components/customer/tabs/ImportTab.tsx`:
1. Add the import near the top: `import { BankImportStateBadges } from '../BankImportStateBadges';`
2. In the header block (around lines 278–317), immediately after the subtitle paragraph (`<p>` under the "Přehled bankovních výpisů" title, ~line 288), insert:

```tsx
          <BankImportStateBadges />
```

(Read the surrounding JSX first to place it inside the header container, not inside a button row.)

- [ ] **Step 4: Build + lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: build + lint succeed (no type errors, no console.log).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useBankStatements.ts \
        frontend/src/components/customer/BankImportStateBadges.tsx \
        frontend/src/components/customer/tabs/ImportTab.tsx
git commit -m "feat(bank): show per-account import watermark staleness badge"
```

---

## Task 11: Full verification

- [ ] **Step 1: Backend gate**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Bank"`
Expected: build OK, format clean, all Bank tests pass.

- [ ] **Step 2: Frontend gate**

Run: `cd frontend && npm run build && npm run lint`
Expected: pass.

- [ ] **Step 3: Apply the migration to staging (manual)**

Connect to the staging DB (per CLAUDE.md, `Heblo_TST` via `heblosql`; swap `Default` in secrets.json) and run:
```bash
cd backend && dotnet ef database update \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```
Verify the `BankImportStates` table exists with PascalCase columns and the unique `IX_BankImportStates_Account` index.

- [ ] **Step 4: Behavioral smoke test (manual)**

With `BankImportStates` empty, trigger one job (Hangfire dashboard "Trigger now" for `daily-comgate-czk-import`, or POST a manual import does NOT count — must be the job). Confirm in logs: bootstrap derives `DateFrom` from existing data, watermark advances to yesterday, and a row appears in `BankImportStates`. Trigger again immediately → confirm `Skipped` count > 0, `Success`/`Error` = 0, watermark re-stamped to the same date.

- [ ] **Step 5: Gap-heal + staleness check (manual)**

Manually set `LastValidImportDate` 5 days back for one account (`UPDATE "BankImportStates" SET "LastValidImportDate" = now() - interval '5 days' WHERE "Account" = 'ComgateCZK';`). Trigger the job → confirm it imports the 5-day range, dedup-skips already-present statements, advances the watermark, and the log shows the "behind" Warning. Open the bank statements page and confirm the badge shows the account (and an account set 20 days back shows "Zastaralé" / Stale).

- [ ] **Step 6 (optional): E2E**

Run: `./scripts/run-playwright-tests.sh` (bank statements module) against staging.

---

## Self-Review Notes (verified during planning)

- **Spec coverage:** watermark persistence (Tasks 1–4), 0-doc-valid advancement (Task 8 + base test), import-since-last (Task 8 `ResolveDateFromAsync`), dedup/idempotency prerequisite (Tasks 6–7), 14-day cap (Task 5/8), bootstrap-from-existing (Task 8), observability via logs+API+badge (Tasks 8–10). All four user decisions are reflected.
- **Type consistency:** `RecordSuccess`/`RecordFailure`/`UpdateImportOutcome`/`GetExistingTransfersAsync`/`GetMaxStatementDateAsync`/`GetByTransferIdAsync`/`UpdateAsync`/`GetTargetEndDate`/`AccountName` names are used identically across impl and tests. Response counts `SuccessCount`/`ErrorCount`/`SkippedCount`/`HasErrors` consumed by the base exactly as defined.
- **Watch-outs for the implementer:** (1) confirm `Result<bool>` factory name in `Domain/Shared/Result.cs` for the handler test; (2) `BankImportJobDiscoveryTests` may need the new ctor args; (3) place the badge inside the ImportTab header container, not a button row; (4) regenerate the migration if any column casing is off.
```