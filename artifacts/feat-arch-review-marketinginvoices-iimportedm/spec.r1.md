# Specification: Align `IImportedMarketingTransactionRepository.AddAsync` with Base Repository Contract

## Summary
The `IImportedMarketingTransactionRepository.AddAsync` method declares a return type of `Task`, while the inherited `BaseRepository<TEntity, TKey>.AddAsync` returns `Task<TEntity>`. This signature divergence forces the concrete `ImportedMarketingTransactionRepository` to use C# `new` shadowing instead of overriding, violating the Liskov Substitution Principle and introducing a latent bug risk. This spec defines the refactor to align signatures, remove the shadow, and adjust the single call site and tests.

## Background
During the daily architecture review on 2026-05-26, the `MarketingInvoices` module was flagged for a signature mismatch in repository contracts.

Current state:
- `Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs:6` declares: `Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct)`
- `Persistence/Repositories/BaseRepository.cs:57` declares: `public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)`
- `Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs:21` uses `public new async Task AddAsync(...)` to hide the base method and satisfy the narrower interface contract.

Consequences of the current shape:
1. **LSP violation** — calling `AddAsync` through a `BaseRepository<ImportedMarketingTransaction, int>` reference dispatches to the base method, returning `Task<TEntity>`; calling through the concrete or `IImportedMarketingTransactionRepository` reference dispatches to the shadowed method returning `Task`. Static reference type silently changes runtime behavior.
2. **Cognitive noise** — the shadowing method is a one-liner that adds nothing semantically beyond hiding the return value.
3. **Ambiguity for callers** — callers cannot tell whether they should expect the added entity back. The base contract intentionally returns the tracked entity (useful for ID generation, change-tracker hooks, EF Core proxies).

Only one production call site exists today (`MarketingInvoiceImportService.cs:80`) and it discards the result. Tests mock `AddAsync` and verify call counts via `Times.Exactly(N)`.

## Functional Requirements

### FR-1: Align interface signature with base repository contract
Update `IImportedMarketingTransactionRepository.AddAsync` so its return type matches `BaseRepository<TEntity, TKey>.AddAsync`.

New signature:
```csharp
Task<ImportedMarketingTransaction> AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
```

**Acceptance criteria:**
- `IImportedMarketingTransactionRepository.AddAsync` returns `Task<ImportedMarketingTransaction>`.
- The `CancellationToken` parameter retains its current name (`ct`) to remain consistent with other methods on the interface.
- The interface file compiles without warnings.

### FR-2: Remove the shadowing `AddAsync` from the concrete repository
Eliminate the `public new async Task AddAsync(...)` member on `ImportedMarketingTransactionRepository`. The base `BaseRepository<ImportedMarketingTransaction, int>.AddAsync` already satisfies the realigned interface (covariant on `TEntity = ImportedMarketingTransaction`), so no override or replacement member is required.

**Acceptance criteria:**
- `ImportedMarketingTransactionRepository` no longer declares an `AddAsync` member.
- The class still implements `IImportedMarketingTransactionRepository` (verified by `dotnet build`).
- No `new` keyword remains on any method of `ImportedMarketingTransactionRepository`.
- The class continues to expose `ExistsAsync` and `GetUnsyncedAsync` unchanged.

### FR-3: Update the production call site
`MarketingInvoiceImportService.ImportFromSourceAsync` currently calls `await _repository.AddAsync(entity, ct);` and discards no value (the return was `Task`). After the refactor the call returns `Task<ImportedMarketingTransaction>`. Discard the return explicitly only if needed; otherwise `await` semantics already drop the value.

**Acceptance criteria:**
- `MarketingInvoiceImportService.cs:80` continues to compile and behave identically — entities are added to the change tracker and `SaveChangesAsync` persists them.
- No new variable is introduced unless a downstream change uses it. Do not add unused locals.
- Behavior for transaction-level error handling (per-item try/catch, failed counters) is unchanged.

### FR-4: Update unit tests for the new signature
`MarketingInvoiceImportServiceTests` currently uses Moq with `_mockRepository.Setup(x => x.AddAsync(...))` and `.Returns(Task.CompletedTask)` / `.ThrowsAsync(...)`. With the new `Task<ImportedMarketingTransaction>` return type, `Returns(Task.CompletedTask)` will fail to compile.

Required updates:
- Setups that previously returned `Task.CompletedTask` must return the entity passed in (or any non-null `ImportedMarketingTransaction`). Prefer `.ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e)` to round-trip the supplied entity.
- Setups that throw (e.g., for failure-path tests) keep using `.ThrowsAsync(...)`.
- `Verify(... Times.Exactly(N))` calls remain unchanged.

**Acceptance criteria:**
- All existing tests in `MarketingInvoiceImportServiceTests` compile and pass.
- No test assertions change semantics. Call-count expectations remain valid.
- No new tests are added unless required to cover a behavior gap introduced by the refactor (none expected).

### FR-5: Audit for other callers and references
Confirm there are no additional consumers of `IImportedMarketingTransactionRepository.AddAsync` beyond `MarketingInvoiceImportService` and its tests.

**Acceptance criteria:**
- Repository-wide grep for `IImportedMarketingTransactionRepository` confirms the only production call site is `MarketingInvoiceImportService.cs:80`.
- Repository-wide grep for `ImportedMarketingTransactionRepository.AddAsync` (and overload calls) returns no other call sites.
- If additional call sites are discovered during implementation, they are updated to match FR-3's contract.

## Non-Functional Requirements

### NFR-1: Behavior preservation
No runtime behavior change is permitted. The same entity instance is added to the EF Core change tracker, and `SaveChangesAsync` continues to persist staged entities in the same order and at the same boundary.

### NFR-2: Build and format hygiene
- `dotnet build` succeeds for the full solution.
- `dotnet format` produces no changes after the edit.
- No new compiler warnings appear in the affected projects (`Anela.Heblo.Domain`, `Anela.Heblo.Persistence`, `Anela.Heblo.Application`, `Anela.Heblo.Tests`).

### NFR-3: Test stability
- All existing tests in `Anela.Heblo.Tests/Features/MarketingInvoices/` pass.
- No flakiness introduced. Test runtime is unchanged.

### NFR-4: Surgical scope
- Only the four files identified below are modified.
- No adjacent style, naming, or comment cleanup. No additional methods added to the interface or base repository.

## Data Model
No data model changes. `ImportedMarketingTransaction` entity is unchanged. No database migrations.

## API / Interface Design

### Before
```csharp
// Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs
public interface IImportedMarketingTransactionRepository
{
    Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);
    Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
    Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}

// Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs
public class ImportedMarketingTransactionRepository
    : BaseRepository<ImportedMarketingTransaction, int>, IImportedMarketingTransactionRepository
{
    // ...
    public new async Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct)
    {
        await base.AddAsync(entity, ct);
    }
    // ...
}
```

### After
```csharp
// Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs
public interface IImportedMarketingTransactionRepository
{
    Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);
    Task<ImportedMarketingTransaction> AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
    Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}

// Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs
public class ImportedMarketingTransactionRepository
    : BaseRepository<ImportedMarketingTransaction, int>, IImportedMarketingTransactionRepository
{
    public ImportedMarketingTransactionRepository(ApplicationDbContext context) : base(context) { }

    public async Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct) { /* unchanged */ }
    public async Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct) { /* unchanged */ }
    // AddAsync inherited from BaseRepository — no override needed.
}
```

The call site in `MarketingInvoiceImportService.cs:80` remains `await _repository.AddAsync(entity, ct);`. The new return value is implicitly discarded by `await` on a `Task<T>` expression-statement, which is valid C# and does not produce a warning under current project settings.

## Dependencies
- `BaseRepository<TEntity, TKey>` in `Anela.Heblo.Persistence.Repositories` — used as-is, no changes.
- `Microsoft.EntityFrameworkCore` — unchanged.
- `Moq` (in tests) — used for new `ReturnsAsync` setups.

## Out of Scope
- No changes to other repositories in the codebase, even if they exhibit similar patterns. A separate review should catalogue those.
- No changes to the `IRepository<TEntity, TKey>` base contract or to `BaseRepository<TEntity, TKey>`.
- No refactor of `MarketingInvoiceImportService` beyond what FR-3 strictly requires.
- No introduction of an explicit `_ = await _repository.AddAsync(...);` discard unless a static analyzer demands it (current project does not).
- No expansion of the interface (e.g., adding `AddRangeAsync`, `UpdateAsync`, `DeleteAsync`) — this refactor is scoped strictly to the `AddAsync` signature.
- No re-architecting of the `IsSynced` flag or batch-save strategy.

## Open Questions
None.

## Status: COMPLETE