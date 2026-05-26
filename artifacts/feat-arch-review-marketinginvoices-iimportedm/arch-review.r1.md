# Architecture Review: Align `IImportedMarketingTransactionRepository.AddAsync` with Base Repository Contract

## Skip Design: true

Backend-only refactor of an interface signature and its concrete implementation. No UI, no API surface change visible to clients, no new visual components.

## Architectural Fit Assessment

The refactor aligns the `MarketingInvoices` repository with the canonical `BaseRepository<TEntity, TKey>` contract defined in `Anela.Heblo.Persistence.Repositories.BaseRepository`. The base contract returns `Task<TEntity>` from `AddAsync` (see `BaseRepository.cs:57`), which is the contract every repository that inherits from it must honor.

**Confirmed integration points:**
- `Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs:6` — interface declaration.
- `Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs:21` — shadowed `AddAsync`.
- `Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs:80` — single production call site.
- `test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — Moq setups at lines 48, 113, 118, 149, 208, 256 use `.Returns(Task.CompletedTask)`.

**Pattern consistency note (out of scope but worth recording):**
A repository-wide grep shows three other repositories also declare `Task AddAsync` (void) in their domain interfaces — `IMeetingTranscriptRepository`, `IPackageRepository`, `IArticleRepository`. **However, those three do not inherit from `BaseRepository<TEntity, TKey>`**, so they don't have the shadowing problem. The architectural deviation is local to `ImportedMarketingTransactionRepository`. Aligning only this repository is correct; do not expand scope.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────┐
│ Application Layer                                │
│   MarketingInvoiceImportService                  │
│     └─ await _repository.AddAsync(entity, ct);   │  ← return value implicitly dropped
└────────────────────┬────────────────────────────┘
                     │ depends on
                     ▼
┌─────────────────────────────────────────────────┐
│ Domain Layer                                     │
│   IImportedMarketingTransactionRepository        │
│     Task<ImportedMarketingTransaction>           │  ← signature aligned
│       AddAsync(entity, ct);                      │
└────────────────────┬────────────────────────────┘
                     │ implemented by
                     ▼
┌─────────────────────────────────────────────────┐
│ Persistence Layer                                │
│   ImportedMarketingTransactionRepository         │
│     : BaseRepository<ImportedMarketingTransaction│
│                      , int>                      │
│     // AddAsync inherited verbatim — no member   │
└─────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Remove the concrete `AddAsync` member entirely (do not `override`)
**Options considered:**
- A. Keep an `override async Task<ImportedMarketingTransaction> AddAsync(...)` that delegates to `base.AddAsync(...)`.
- B. Remove the concrete `AddAsync` member; let the inherited base implementation satisfy the realigned interface.

**Chosen approach:** B.

**Rationale:** The base implementation already returns `result.Entity` from `DbSet.AddAsync` — no domain-specific behavior is added by the shadow. An empty `override` that just calls `base.AddAsync` is dead code and identical cognitive noise to the current `new` shadow. Removing the member entirely is the simplest expression of "this repository uses the base behavior unchanged" and removes the LSP risk by construction. Aligns with the project's surgical-change principle.

#### Decision 2: Keep call-site signature `await _repository.AddAsync(entity, ct);` — do not introduce a discard
**Options considered:**
- A. Add explicit `_ = await _repository.AddAsync(entity, ct);` discard.
- B. Leave the call as `await _repository.AddAsync(entity, ct);` — implicit drop.

**Chosen approach:** B.

**Rationale:** Awaiting `Task<T>` as an expression-statement is legal C# and produces no warning under this project's analyzer settings (verified by inspection of analogous patterns elsewhere). Adding `_ =` is noise without benefit. If a future static-analysis rule (e.g., `CA1806`) is enabled, that's a separate cross-cutting cleanup, not part of this refactor.

#### Decision 3: Test mocks return the input entity via `ReturnsAsync` callback
**Options considered:**
- A. `.ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e)` — round-trips the passed entity.
- B. `.ReturnsAsync(new ImportedMarketingTransaction { ... })` — returns a synthetic instance.

**Chosen approach:** A.

**Rationale:** Round-tripping mirrors EF Core's real behavior (the change-tracker returns the same instance). It avoids per-test entity construction and keeps the mock setup terse. Existing assertions only check call counts and a single `Callback` captures the entity for value assertions in the currency-persistence test (line 256–258), which is compatible with a `ReturnsAsync` lambda.

## Implementation Guidance

### Directory / Module Structure

No new files. Modifications only:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs` | Change return type of `AddAsync` to `Task<ImportedMarketingTransaction>`. Keep parameter name `ct`. |
| `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs` | **Delete** lines 21–24 (the `public new async Task AddAsync(...)` member). |
| `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` | No change. The expression-statement `await _repository.AddAsync(entity, ct);` is valid for `Task<T>`. |
| `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` | Replace every `.Returns(Task.CompletedTask)` on `AddAsync` setups (lines 49, 114, 150, 209, 258) with `.ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e)`. Leave `.ThrowsAsync(...)` (line 119) and `.Callback(...)` (lines 257–258 ordering) untouched in semantics. |

### Interfaces and Contracts

**After refactor — `IImportedMarketingTransactionRepository`:**

```csharp
public interface IImportedMarketingTransactionRepository
{
    Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);
    Task<ImportedMarketingTransaction> AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
    Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

**Interface satisfaction by base class:**
`BaseRepository<TEntity, TKey>.AddAsync(TEntity, CancellationToken = default)` has signature `Task<TEntity> AddAsync(TEntity, CancellationToken)`. With `TEntity = ImportedMarketingTransaction`, this becomes `Task<ImportedMarketingTransaction> AddAsync(ImportedMarketingTransaction, CancellationToken)` — an exact match for the realigned interface member. C# interface implementation matches by signature (the default parameter on the base method does not break the match; an explicit argument is always provided at the call site). No explicit interface implementation or wrapper is required.

### Data Flow

Identical to current behavior. `MarketingInvoiceImportService` iterates source transactions, calls `AddAsync` per non-duplicate entity (staging in EF's change tracker via `BaseRepository.AddAsync` → `DbSet.AddAsync`), then flushes once with `SaveChangesAsync`. The only observable difference is that `await _repository.AddAsync(...)` now resolves to `Task<ImportedMarketingTransaction>` instead of `Task`; the awaited value is discarded.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden caller depends on the current `Task` (void) signature in a way that won't compile after the change. | LOW | Repository-wide grep (`IImportedMarketingTransactionRepository`, `ImportedMarketingTransactionRepository.AddAsync`) confirms exactly one production call site and one test file. `dotnet build` will catch any miss. |
| Moq setup migration silently breaks an assertion because `.Returns(Task.CompletedTask)` is changed to `.ReturnsAsync(...)` with the wrong shape. | LOW | All existing assertions are call-count (`Times.Exactly`, `Times.Never`, `Times.Once`) and a single entity-capture via `Callback`. None depend on the return value. The lambda `(e, _) => e` round-trips the entity and preserves the `Callback` ordering. |
| Test file uses `.Returns(Task.CompletedTask)` after a `.Callback(...)` (line 257–258); reordering when migrating to `ReturnsAsync` could change Moq invocation order. | LOW | Moq's `.Callback(...).ReturnsAsync(...)` chain is order-equivalent to `.Callback(...).Returns(Task.CompletedTask)`. Preserve the `.Callback(...)` before `.ReturnsAsync(...)` and behavior is unchanged. |
| Other repositories with similar `Task AddAsync` voids (Article, Package, MeetingTranscript) get flagged as the same problem. | NONE for this PR | Out of scope per spec. Those repositories do not inherit `BaseRepository<TEntity, TKey>` and therefore do not exhibit the shadowing problem. File a separate review if a broader audit is desired. |
| `dotnet format` reorders or trims surrounding members and creates churn unrelated to the refactor. | LOW | The change touches three files in narrow, bounded regions. Run `dotnet format` once and inspect the diff before committing. |

## Specification Amendments

None substantive. Two clarifications worth folding into the spec:

1. **FR-2 wording:** Spec says "no override or replacement member is required." Confirm explicitly that the inherited base member satisfies the realigned interface contract **by signature match alone**, not by explicit interface implementation. This avoids any future reader thinking an `override` is needed.
2. **FR-4 example:** Recommend pinning the test mock setup style to `.ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e)` (per Decision 3) so all six call sites are migrated consistently. The spec already suggests this; promote it from "Prefer" to "Use" for consistency across the test file.

## Prerequisites

None. No migrations, no configuration, no infrastructure changes. The work is a single PR touching three production files and one test file, gated only by:

- `dotnet build` — must succeed for `Anela.Heblo.Domain`, `Anela.Heblo.Persistence`, `Anela.Heblo.Application`, `Anela.Heblo.Tests`.
- `dotnet format` — produces no diff.
- `dotnet test --filter FullyQualifiedName~MarketingInvoiceImportServiceTests` — all green.