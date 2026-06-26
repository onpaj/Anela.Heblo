# Batch SaveChangesAsync in MarketingInvoiceImportService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the `SaveChangesAsync` flush out of the per-transaction loop in `MarketingInvoiceImportService.ImportAsync` so a single batched flush persists the whole import run, with truthful `MarketingImportResult` counts and a guard against intra-batch duplicate `TransactionId`s.

**Architecture:** The change is entirely inside the body of `ImportAsync`. The per-transaction `try/catch` and skip logic stay in the loop; `AddAsync` only stages entities. After the loop, a single `SaveChangesAsync` (wrapped in its own `try/catch`) flushes everything. A local `stagedCount` tracks how many entities were staged so `result.Imported` only ever holds a true value — it is set after a successful flush, and on flush failure the staged count moves to `result.Failed` instead. A run-scoped `HashSet<string>` of staged `TransactionId`s detects source rows duplicated *within the same run* (which `ExistsAsync`, a SQL query, cannot see) — without it a duplicated source row would violate the unique index and fail the entire batched flush.

**Tech Stack:** .NET 8, EF Core (change tracking + implicit transaction on a single `SaveChangesAsync`), xUnit + Moq for tests.

---

## Background & Context

`MarketingInvoiceImportService` is constructed directly (`new MarketingInvoiceImportService(...)`) inside `GoogleAdsInvoiceImportJob` and `MetaAdsInvoiceImportJob`, which run twice daily. Callers only read `MarketingImportResult` counts for logging — no caller depends on per-record commit timing. The repository is registered `AddScoped`, so each job run gets a fresh `ApplicationDbContext`; on a flush failure the orphaned `Added` entities are discarded when the job scope disposes.

The all-or-nothing guarantee comes from EF Core wrapping the single `SaveChangesAsync` over N `Added` entities in an implicit database transaction — **no explicit `IDbContextTransaction` is needed** (it is out of scope).

**Files in scope (only two change):**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — the refactor (only the `ImportAsync` method body changes).
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — update 2 existing tests, add 3 new tests.

**Explicitly NOT touched:** `IImportedMarketingTransactionRepository`, the repository implementation, `MarketingImportResult`, `ImportedMarketingTransaction`, the EF configuration, `MarketingInvoicesModule`, the import jobs, and the database schema. No migration.

---

## Task 1: Update and extend the test suite to the batched contract (RED)

This task rewrites the test file to encode the batched-flush contract. After this task the test project compiles but tests fail against the current per-record code — that is expected and proves the tests exercise the new behavior.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

- [ ] **Step 1: Replace the entire test file with the batched-contract version**

Overwrite `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` with the following. Changes from the current file: (1) `ImportAsync_NewTransactions_ArePersistedAndCounted` verifies `SaveChangesAsync` `Times.Once` instead of `Times.Exactly(2)`; (2) `ImportAsync_DuplicateTransaction_IsSkipped` adds a `SaveChangesAsync` `Times.Never` verification; (3) `ImportAsync_PerTransactionError_CountsAsFailed_DoesNotAbortRun` is unchanged in assertions; (4) three new tests are added. The `Assert.*` (xUnit) style is kept to match the existing file — do not switch to FluentAssertions.

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;

public class MarketingInvoiceImportServiceTests
{
    private readonly Mock<IMarketingTransactionSource> _mockSource;
    private readonly Mock<IImportedMarketingTransactionRepository> _mockRepository;
    private readonly Mock<ILogger<MarketingInvoiceImportService>> _mockLogger;
    private readonly MarketingInvoiceImportService _service;

    public MarketingInvoiceImportServiceTests()
    {
        _mockSource = new Mock<IMarketingTransactionSource>();
        _mockRepository = new Mock<IImportedMarketingTransactionRepository>();
        _mockLogger = new Mock<ILogger<MarketingInvoiceImportService>>();

        _mockSource.Setup(x => x.Platform).Returns("TestPlatform");

        _service = new MarketingInvoiceImportService(
            _mockSource.Object,
            _mockRepository.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task ImportAsync_NewTransactions_ArePersistedAndCounted()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Platform = "TestPlatform", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Platform = "TestPlatform", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportAsync(from, to);

        // Assert
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_DuplicateTransaction_IsSkipped()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Platform = "TestPlatform", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        // Already exists in DB
        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", "TX-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ImportAsync(from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_PerTransactionError_CountsAsFailed_DoesNotAbortRun()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Platform = "TestPlatform", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Platform = "TestPlatform", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // First transaction succeeds
        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-001"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Second transaction throws on AddAsync
        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-002"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB write failed"));

        // Act
        var result = await _service.ImportAsync(from, to);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task ImportAsync_FinalSaveChangesThrows_ReportsAllStagedAsFailed_DoesNotThrow()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Platform = "TestPlatform", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Platform = "TestPlatform", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // The single post-loop flush fails — none of the staged records are persisted
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("flush failed"));

        // Act
        var result = await _service.ImportAsync(from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(2, result.Failed);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_EmptyInput_DoesNotCallSaveChanges()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingTransaction>());

        // Act
        var result = await _service.ImportAsync(from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_DuplicateTransactionIdWithinSameRun_StagesOnlyOnce()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        // Same TransactionId returned twice by the source in one run
        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-DUP", Platform = "TestPlatform", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-DUP", Platform = "TestPlatform", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        // Not present in the DB — ExistsAsync cannot see un-flushed staged entities
        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportAsync(from, to);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Build the test project to confirm it compiles**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: Build succeeds (the new tests reference only existing public APIs — `ImportAsync`, `MarketingImportResult`, the repository interface, `MarketingTransaction`).

- [ ] **Step 3: Run the test class to confirm the expected failures**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"`
Expected: FAIL. Against the current per-record code:
- `ImportAsync_NewTransactions_ArePersistedAndCounted` fails — `SaveChangesAsync` is called twice (per record), not once.
- `ImportAsync_DuplicateTransactionIdWithinSameRun_StagesOnlyOnce` fails — current code calls `AddAsync` twice (no intra-batch dedup) and `SaveChangesAsync` once per record.
- `ImportAsync_FinalSaveChangesThrows_...` fails — current per-record code catches the throw inside the loop, so it would report `Failed == 2` only if both records' saves throw, but `Imported` handling differs; the count expectations will not match the per-record behavior.
- `ImportAsync_DuplicateTransaction_IsSkipped` and `ImportAsync_EmptyInput_...` and `ImportAsync_PerTransactionError_...` may pass already — that is fine.

This RED state confirms the tests pin the new contract. Do not commit yet — the commit happens in Task 2 with the implementation.

---

## Task 2: Refactor `ImportAsync` to a single batched flush (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`

- [ ] **Step 1: Replace the `ImportAsync` method body**

In `MarketingInvoiceImportService.cs`, replace the entire `ImportAsync` method (currently lines 22-76) with the version below. Constructor, fields, `using` directives, and namespace stay exactly as they are. Changes: a local `stagedCount` and a `stagedIds` `HashSet<string>` are introduced; the inline `await _repository.SaveChangesAsync(ct)` and the inline `result.Imported++` are removed from the loop; the skip condition also checks `stagedIds`; a single guarded `SaveChangesAsync` runs after the loop.

```csharp
    public async Task<MarketingImportResult> ImportAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting marketing invoice import for platform {Platform} from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            _source.Platform, from, to);

        var transactions = await _source.GetTransactionsAsync(from, to, ct);

        var result = new MarketingImportResult();
        var stagedCount = 0;
        var stagedIds = new HashSet<string>();

        foreach (var transaction in transactions)
        {
            try
            {
                // Duplicate: already persisted in the DB, OR already staged earlier in this run.
                // ExistsAsync runs a SQL query and cannot see entities staged via AddAsync but not yet flushed.
                if (stagedIds.Contains(transaction.TransactionId) ||
                    await _repository.ExistsAsync(_source.Platform, transaction.TransactionId, ct))
                {
                    _logger.LogDebug(
                        "Transaction {TransactionId} for {Platform} already imported — skipping",
                        transaction.TransactionId, _source.Platform);
                    result.Skipped++;
                    continue;
                }

                var entity = new ImportedMarketingTransaction
                {
                    TransactionId = transaction.TransactionId,
                    Platform = _source.Platform,
                    Amount = transaction.Amount,
                    TransactionDate = transaction.TransactionDate,
                    ImportedAt = DateTime.UtcNow,
                    IsSynced = false,
                };

                await _repository.AddAsync(entity, ct);
                stagedIds.Add(transaction.TransactionId);
                stagedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to import transaction {TransactionId} for {Platform}",
                    transaction.TransactionId, _source.Platform);
                result.Failed++;
            }
        }

        if (stagedCount > 0)
        {
            try
            {
                await _repository.SaveChangesAsync(ct);
                result.Imported = stagedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist {Count} marketing transactions for {Platform}",
                    stagedCount, _source.Platform);
                result.Failed += stagedCount;
                // result.Imported intentionally stays 0 — nothing was committed.
            }
        }

        _logger.LogInformation(
            "Marketing invoice import complete for {Platform}: Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
            _source.Platform, result.Imported, result.Skipped, result.Failed);

        return result;
    }
```

- [ ] **Step 2: Build the application project**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeds with no warnings introduced by the change.

- [ ] **Step 3: Run the test class to verify all tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"`
Expected: PASS — all 6 tests green:
- `ImportAsync_NewTransactions_ArePersistedAndCounted` — `Imported == 2`, `SaveChangesAsync` once.
- `ImportAsync_DuplicateTransaction_IsSkipped` — `Skipped == 1`, `SaveChangesAsync` never.
- `ImportAsync_PerTransactionError_CountsAsFailed_DoesNotAbortRun` — `Imported == 1`, `Failed == 1` (TX-001 staged then flushed once, TX-002 throws on `AddAsync`).
- `ImportAsync_FinalSaveChangesThrows_ReportsAllStagedAsFailed_DoesNotThrow` — `Imported == 0`, `Failed == 2`, no exception.
- `ImportAsync_EmptyInput_DoesNotCallSaveChanges` — all counts 0, `SaveChangesAsync` never.
- `ImportAsync_DuplicateTransactionIdWithinSameRun_StagesOnlyOnce` — `Imported == 1`, `Skipped == 1`, `AddAsync` once, `SaveChangesAsync` once.

- [ ] **Step 4: Run `dotnet format` on the changed files**

Run: `dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`
Expected: Completes with no remaining formatting changes (or applies whitespace fixes only).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs
git commit -m "refactor: batch SaveChangesAsync in MarketingInvoiceImportService

Move the EF Core flush out of the per-transaction loop so a single
SaveChangesAsync persists the whole import run as one unit of work.
Add a run-scoped HashSet to skip source rows duplicated within the
same run (ExistsAsync cannot see un-flushed staged entities). Track a
local stagedCount so result.Imported only reports records after a
successful flush; on flush failure the staged count moves to Failed."
```

---

## Task 3: Full validation

**Files:** None — verification only.

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeds with no errors.

- [ ] **Step 2: Run the full test project**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: All tests pass. The change is confined to `MarketingInvoiceImportService`; no other test should be affected. If an unrelated test fails, confirm it is pre-existing (run it on `origin/main`) before proceeding.

- [ ] **Step 3: Confirm `dotnet format` reports no outstanding changes**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`
Expected: Exit code 0 — no formatting changes needed.

If any step fails, fix the cause and re-run that step before declaring the task complete.

---

## Spec Coverage Check

| Spec requirement | Implemented by |
|---|---|
| FR-1: single batched flush per run | Task 2 Step 1 — inline `SaveChangesAsync` removed from loop; guarded single `SaveChangesAsync` after loop on `stagedCount > 0` |
| FR-2: preserve `Imported`/`Skipped`/`Failed` semantics | Task 2 Step 1 — `Skipped++`/`Failed++` unchanged in loop; `result.Imported = stagedCount` after successful flush; Task 1 tests pin counts |
| FR-3: handle final flush failure | Task 2 Step 1 — `try/catch` around `SaveChangesAsync`, `LogError` with platform + count, `Failed += stagedCount`, `Imported` stays 0, no rethrow; Task 1 `ImportAsync_FinalSaveChangesThrows_...` test |
| FR-4: update affected unit tests | Task 1 — `NewTransactions` → `Times.Once`; `DuplicateTransaction` → `Times.Never`; `PerTransactionError` unchanged; new flush-failure + empty-input tests |
| Arch amendment 1+2: intra-batch duplicate `TransactionId` handling + test | Task 2 Step 1 — `stagedIds` `HashSet` check; Task 1 `ImportAsync_DuplicateTransactionIdWithinSameRun_StagesOnlyOnce` test |
| Arch Decision 2: local `stagedCount`, not `result.Imported`, as flush guard | Task 2 Step 1 — `stagedCount` declared and incremented; `result.Imported` set only after successful flush |
| NFR-1: performance (N round-trips → 1) | Task 2 Step 1 — flush moved outside loop |
| NFR-2: security unchanged | No new inputs/endpoints/secrets; `LogError` passes only structured properties (platform, count) |
| Data model / API unchanged | No files other than the service and its test are modified (Task 1 + Task 2 file lists) |
