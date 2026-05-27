# IImportedMarketingTransactionRepository.AddAsync Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align `IImportedMarketingTransactionRepository.AddAsync` with the inherited `BaseRepository<TEntity, TKey>.AddAsync` contract (`Task<TEntity>` return type), delete the LSP-violating `new`-shadow on the concrete repository, and update Moq setups in the only consumer test file so they compile against the new signature.

**Architecture:** Pure backend refactor. Three production files touched, one test file touched. No data model, no database migration, no API surface change. The base implementation already returns the tracked entity from `DbSet.AddAsync(...).Entity`; once the interface returns `Task<ImportedMarketingTransaction>`, the inherited base method satisfies it by signature match and the concrete repository can drop its one-line shadow entirely. The single production caller (`MarketingInvoiceImportService.ImportAsync`) keeps its expression-statement `await _repository.AddAsync(entity, ct);` — awaiting a `Task<T>` as a statement implicitly discards the value and is legal C# under this project's analyzer settings.

**Tech Stack:** C# 12, .NET 8, EF Core, xUnit, Moq.

---

## File Structure

No new files. Modifications in narrow, bounded regions of four files:

| File | Responsibility | Change |
|------|---------------|--------|
| `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs` | Interface contract | Change one line: `Task AddAsync(...)` → `Task<ImportedMarketingTransaction> AddAsync(...)` |
| `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs` | EF Core repository implementation | Delete four lines: the `public new async Task AddAsync(...) { await base.AddAsync(entity, ct); }` member |
| `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` | Single production consumer | **No change.** The expression-statement `await _repository.AddAsync(entity, ct);` is valid for both `Task` and `Task<T>`. |
| `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` | Unit tests | Replace five `.Returns(Task.CompletedTask)` setups on `AddAsync` with `.ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e)`. Leave `.ThrowsAsync(...)` (line 119) unchanged. Preserve the `.Callback(...)` chain at line 257. |

**Commit strategy:** Because the interface change breaks compilation of both the concrete class and the test file simultaneously, all edits land in a **single commit**. Verification (build + format + filtered test run) happens after all four edits, before the commit.

---

## Pre-flight: Scope audit

The arch-review identified exactly one production call site and one test file. Re-verify before editing in case the worktree has drifted.

### Task 0: Confirm scope

**Files:** (read-only audit)

- [ ] **Step 1: Grep for the interface symbol across the worktree**

Run:
```bash
grep -rn "IImportedMarketingTransactionRepository" \
  backend/src backend/test
```

Expected hits (5 files, no more):
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs` — declaration
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs` — implementation
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — consumer
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingInvoicesModule.cs` — DI registration (no `AddAsync` call)
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — tests

If any additional file appears, STOP and update FR-3 / FR-5 of the spec before proceeding. The plan assumes exactly these consumers.

- [ ] **Step 2: Grep for direct calls to the typed method**

Run:
```bash
grep -rn "_repository.AddAsync\|_mockRepository.*AddAsync\|repository\.AddAsync" \
  backend/src backend/test \
  | grep -i "marketing"
```

Expected production call: exactly one — `MarketingInvoiceImportService.cs:80`.
Expected test references: six on `_mockRepository.*AddAsync` setups/verifies in `MarketingInvoiceImportServiceTests.cs`.

If more production call sites surface, add them to the list of files modified in Task 3 below (each call site is `await _repository.AddAsync(entity, ct);` and remains unchanged textually — only ensure the file still compiles after the interface change).

- [ ] **Step 3: No commit. This task is read-only.**

---

## Edits

The next three tasks (1, 2, 3) **leave the codebase in an uncompilable state between tasks**. Do not run `dotnet build` until all three are complete; the build is part of Task 4. Do not commit until Task 5.

### Task 1: Update the interface signature

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs:6`

- [ ] **Step 1: Replace the `AddAsync` declaration**

Read the file first to confirm current content matches:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public interface IImportedMarketingTransactionRepository
{
    Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);
    Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
    Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

Change line 6 from:

```csharp
    Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
```

to:

```csharp
    Task<ImportedMarketingTransaction> AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
```

Keep parameter name `ct` (consistent with the other three methods on this interface). No other change to the file.

- [ ] **Step 2: No build run yet — proceed to Task 2.**

---

### Task 2: Remove the shadowing `AddAsync` from the concrete repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs:21-24`

- [ ] **Step 1: Delete the shadow member**

Current content (lines 1–30):

```csharp
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Persistence.Repositories;

namespace Anela.Heblo.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionRepository
    : BaseRepository<ImportedMarketingTransaction, int>, IImportedMarketingTransactionRepository
{
    public ImportedMarketingTransactionRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct)
    {
        return await AnyAsync(
            x => x.Platform == platform && x.TransactionId == transactionId,
            ct);
    }

    public new async Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct)
    {
        await base.AddAsync(entity, ct);
    }

    public async Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct)
    {
        return (await FindAsync(x => !x.IsSynced, ct)).ToList();
    }
}
```

Delete lines 21–24 (the `AddAsync` member) **and** the trailing blank line at 25 so the file flows directly from `ExistsAsync` to `GetUnsyncedAsync`.

After edit, the file should be:

```csharp
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Persistence.Repositories;

namespace Anela.Heblo.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionRepository
    : BaseRepository<ImportedMarketingTransaction, int>, IImportedMarketingTransactionRepository
{
    public ImportedMarketingTransactionRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct)
    {
        return await AnyAsync(
            x => x.Platform == platform && x.TransactionId == transactionId,
            ct);
    }

    public async Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct)
    {
        return (await FindAsync(x => !x.IsSynced, ct)).ToList();
    }
}
```

**Why this works:** With `TEntity = ImportedMarketingTransaction`, the inherited `BaseRepository.AddAsync` resolves to `public virtual Task<ImportedMarketingTransaction> AddAsync(ImportedMarketingTransaction, CancellationToken)`. C# interface implementation matches by signature alone; the default-parameter on the base (`CancellationToken cancellationToken = default`) does not break the match because callers always pass `ct` explicitly. No `override` keyword, no explicit interface implementation, no wrapper required.

- [ ] **Step 2: No build run yet — proceed to Task 3.**

---

### Task 3: Update the production caller (no-op verification)

**Files:**
- Verify only: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs:80`

- [ ] **Step 1: Confirm the call site is unchanged and valid**

The current line 80 reads:

```csharp
                await _repository.AddAsync(entity, ct);
```

This expression-statement is valid C# for both `Task` and `Task<T>` returns — the value is implicitly discarded by `await` when used as a statement. **No edit required.** Do not add `_ =` discard syntax (per arch-review Decision 2 — would be noise without analyzer pressure).

- [ ] **Step 2: No commit — proceed to Task 4.**

---

### Task 4: Update unit-test mocks

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

The five `.Returns(Task.CompletedTask)` setups on `AddAsync` are at the following line ranges (per arch-review and confirmed by inspection). The `.ThrowsAsync(...)` at line 119 stays unchanged. The `.Callback(...)` chain at line 257 must precede the new `.ReturnsAsync(...)` to preserve invocation order.

- [ ] **Step 1: Replace setup at lines 48–49 (test `ImportAsync_NewTransactions_ArePersistedAndCounted`)**

Find:
```csharp
        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
```

Replace with:
```csharp
        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);
```

- [ ] **Step 2: Replace setup at lines 113–114 (test `ImportAsync_PerTransactionError_CountsAsFailed_DoesNotAbortRun`)**

Find:
```csharp
        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-001"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
```

Replace with:
```csharp
        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);
```

- [ ] **Step 3: Leave the `.ThrowsAsync(...)` setup at lines 118–119 untouched**

This setup throws on `TX-002` and must remain semantically identical. Verify it still reads:

```csharp
        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-002"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB write failed"));
```

No change.

- [ ] **Step 4: Replace setup at lines 149–150 (test `ImportAsync_FinalSaveChangesThrows_ReportsAllStagedAsFailed_DoesNotThrow`)**

Find:
```csharp
        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
```

Replace with:
```csharp
        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);
```

- [ ] **Step 5: Replace setup at lines 208–209 (test `ImportAsync_DuplicateTransactionIdWithinSameRun_StagesOnlyOnce`)**

Find:
```csharp
        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
```

Replace with:
```csharp
        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);
```

- [ ] **Step 6: Replace setup at lines 255–258 (test `ImportAsync_NewTransaction_PersistsCurrencyDescriptionAndRawData`) — preserve the `.Callback(...)` chain**

Find:
```csharp
        ImportedMarketingTransaction? captured = null;
        _mockRepository
            .Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<ImportedMarketingTransaction, CancellationToken>((entity, _) => captured = entity)
            .Returns(Task.CompletedTask);
```

Replace with:
```csharp
        ImportedMarketingTransaction? captured = null;
        _mockRepository
            .Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<ImportedMarketingTransaction, CancellationToken>((entity, _) => captured = entity)
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);
```

**Important:** keep `.Callback(...)` **before** `.ReturnsAsync(...)`. Moq's fluent chain runs the callback before producing the return value; reversing the order changes when `captured` is assigned relative to the awaited result and would risk test flakiness (per arch-review risk row 3).

- [ ] **Step 7: Verify no other `Returns(Task.CompletedTask)` on `AddAsync` remains in the file**

Run:
```bash
grep -n "AddAsync" backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs \
  | grep -B0 -A1 "Returns(Task.CompletedTask)"
```

Expected: no output. If any match returns, repeat the appropriate replacement above.

- [ ] **Step 8: Verify the `.ThrowsAsync(...)` line is still present and unchanged**

Run:
```bash
grep -n "ThrowsAsync(new InvalidOperationException(\"DB write failed\"))" \
  backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs
```

Expected: one match, on the same line as before (line 119, possibly shifted by surrounding whitespace edits).

---

### Task 5: Verify build, format, and tests

**Files:** (no edits — verification only)

- [ ] **Step 1: Build the solution**

Run from repo root:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)` for the four affected projects (`Anela.Heblo.Domain`, `Anela.Heblo.Persistence`, `Anela.Heblo.Application`, `Anela.Heblo.Tests`).

If a `CS0114` "hides inherited member" warning surfaces on the concrete repository, Task 2's deletion was incomplete — re-check.
If `CS0535` "does not implement interface member" surfaces on the concrete repository, the interface change in Task 1 didn't match the inherited base signature — re-check that the parameter list is exactly `(ImportedMarketingTransaction entity, CancellationToken ct)`.
If a Moq compile error surfaces in the test file (e.g., `CS1503 cannot convert from 'Task' to 'Task<ImportedMarketingTransaction>'`), Task 4 missed one of the five `.Returns(Task.CompletedTask)` sites — re-grep.

- [ ] **Step 2: Run `dotnet format` and verify no diff**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code 0, no output indicating changes needed.

If it reports changes, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) and inspect the diff with `git diff` — any reformatting must be limited to lines actually touched by Tasks 1, 2, and 4. Reject and revert any reformatting on adjacent unrelated lines (e.g., re-ordered usings in untouched files) per CLAUDE.md "Surgical changes."

- [ ] **Step 3: Run the affected test class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests" \
  --no-build
```

Expected: 9 tests pass (the 9 `[Fact]` methods in `MarketingInvoiceImportServiceTests.cs`). 0 failures, 0 skipped.

If any test fails:
- `ImportAsync_PerTransactionError_CountsAsFailed_DoesNotAbortRun` failing with an unexpected `result.Imported` count → check that the `.ThrowsAsync(...)` at line 119 was preserved (Task 4 Step 3).
- `ImportAsync_NewTransaction_PersistsCurrencyDescriptionAndRawData` failing with `captured == null` → the `.Callback(...).ReturnsAsync(...)` order was inverted (Task 4 Step 6).
- Any other failure → re-read the failing assertion and compare against the before/after diff of `MarketingInvoiceImportServiceTests.cs`.

- [ ] **Step 4: Run the full backend test suite to catch indirect regressions**

```bash
dotnet test backend/Anela.Heblo.sln --no-build
```

Expected: all tests pass. No previously-passing test should regress. This is the project's normal completion gate per `CLAUDE.md`.

If a test outside `MarketingInvoiceImportServiceTests` fails and references `IImportedMarketingTransactionRepository` or `ImportedMarketingTransactionRepository`, Task 0's scope audit missed a consumer — add the failing call site to the changes and re-run.

---

### Task 6: Commit

**Files:**
- All edits from Tasks 1, 2, 4

- [ ] **Step 1: Stage exactly the four touched files**

```bash
git add \
  backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs \
  backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs \
  backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs
```

Note: `MarketingInvoiceImportService.cs` is **not** staged because Task 3 made no edit. The DI module is not staged for the same reason. Do not use `git add -A`.

- [ ] **Step 2: Verify the staged diff is exactly what the plan describes**

```bash
git status
git diff --cached --stat
git diff --cached
```

Expected stat output: three files, additions roughly balanced with deletions (interface +1/-1, concrete -4, tests +5/-5).

Reject the commit and re-investigate if:
- Any file outside the three above appears in the staged set.
- The concrete-class diff shows changes beyond the deletion of the `AddAsync` member and its preceding blank line.
- The interface diff touches more than one line.
- The test-file diff modifies anything other than the five mock setups identified in Task 4.

- [ ] **Step 3: Commit with a Conventional Commits message**

```bash
git commit -m "refactor(marketing-invoices): align IImportedMarketingTransactionRepository.AddAsync with base contract

- Interface now returns Task<ImportedMarketingTransaction>, matching BaseRepository<TEntity, TKey>.AddAsync.
- Remove the LSP-violating 'public new async Task AddAsync(...)' shadow on the concrete repository; the inherited base method satisfies the realigned interface by signature match.
- Update Moq setups in MarketingInvoiceImportServiceTests to return the input entity via ReturnsAsync callback; .ThrowsAsync and .Callback chains preserved unchanged.
- No behavior change: production call site (MarketingInvoiceImportService.cs:80) awaits the result as an expression-statement, implicitly discarding the value."
```

- [ ] **Step 4: Confirm the commit landed cleanly**

```bash
git log -1 --stat
```

Expected: one commit, three files changed.

---

## Self-Review Checklist (already executed by plan author)

**Spec coverage**
- FR-1 (interface signature aligned) → Task 1.
- FR-2 (remove shadow on concrete) → Task 2.
- FR-3 (production caller updated) → Task 3 (verification — no edit required).
- FR-4 (tests updated) → Task 4 (six steps covering five `.Returns(Task.CompletedTask)` sites + preserved `.ThrowsAsync` + preserved `.Callback`).
- FR-5 (audit for other callers) → Task 0.
- NFR-1 (behavior preservation) → Task 5 Step 3 (filtered tests) + Task 5 Step 4 (full suite).
- NFR-2 (build + format hygiene) → Task 5 Steps 1 and 2.
- NFR-3 (test stability) → Task 5 Steps 3 and 4.
- NFR-4 (surgical scope) → Task 6 Step 2 (staged-diff inspection rejects anything outside the four files / specific lines).

**Placeholder scan:** No "TBD", "implement later", "appropriate error handling", or "similar to Task N" patterns. Each step shows the actual before/after code or the exact command + expected output.

**Type consistency:** Signatures are consistent across tasks. The realigned interface member, the inherited base member, and the Moq `ReturnsAsync` lambda all line up on `Task<ImportedMarketingTransaction>` with parameters `(ImportedMarketingTransaction entity, CancellationToken ct)` / `(ImportedMarketingTransaction e, CancellationToken _) => e`.
