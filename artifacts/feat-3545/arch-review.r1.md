# Architecture Review: Push MaxOrder computation into the database for InvoiceClassification rules

## Skip Design: true

## Architectural Fit Assessment
This is a textbook fit for the existing pattern in this codebase and requires no new abstractions. `IClassificationRuleRepository` (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRuleRepository.cs`) already exposes a small, purpose-built set of methods (`GetAllAsync`, `GetActiveRulesOrderedAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `ReorderRulesAsync`) that mix full-collection reads, single-row lookups, and targeted mutations on the same `ApplicationDbContext.ClassificationRules` set. Adding one more targeted read method (`GetMaxOrderAsync`) is consistent with that shape — it does not introduce a new interface, a new repository, or a new DI registration; it extends the one that exists.

The change sits entirely inside the InvoiceClassification vertical slice:
- Domain layer: interface addition (`Anela.Heblo.Domain`)
- Persistence layer: implementation using the already-injected `ApplicationDbContext` (`Anela.Heblo.Persistence`)
- Application layer: one handler swaps two lines for one (`Anela.Heblo.Application`)

No module boundary is crossed, no contract (`Contracts/` DTOs) changes, no controller changes, and per `docs/architecture/development_guidelines.md` ADR-004, the repository's DI binding already lives in the feature's own module (not `PersistenceModule.cs`) — confirmed by inspection, this fix doesn't touch DI registration at all since the same `IClassificationRuleRepository` → `ClassificationRuleRepository` binding is reused unchanged.

## Proposed Architecture

### Component Overview
```
CreateClassificationRuleHandler (Application)
        │
        │  await _ruleRepository.GetMaxOrderAsync()   [NEW — replaces GetAllAsync()+Max()]
        ▼
IClassificationRuleRepository (Domain)               ← interface gains one member
        │
        ▼
ClassificationRuleRepository (Persistence)            ← implements via EF Core MaxAsync
        │
        ▼
ApplicationDbContext.ClassificationRules
        │
        ▼
SELECT MAX([Order]) FROM ClassificationRules          (single scalar aggregate)
```

No new components. This is a same-shape extension of an existing interface/implementation/consumer triad.

### Key Design Decisions

#### Decision 1: Extend the existing repository interface vs. introduce a new abstraction (e.g., a query object, a CQRS "GetMaxOrderQuery")
**Options considered:**
1. Add `GetMaxOrderAsync()` directly to `IClassificationRuleRepository` / `ClassificationRuleRepository`.
2. Introduce a separate read-model/query service for this one aggregate.

**Chosen approach:** Option 1, exactly as specified in FR-1/FR-2.

**Rationale:** The repository already owns all reads and writes against `ClassificationRules`; a second abstraction for a single scalar aggregate would violate the project's YAGNI stance (this whole fix exists *because* of a YAGNI/efficiency finding) and add an unnecessary layer for one method. Option 2 is over-engineering relative to the size of the change.

#### Decision 2: Nullable-cast pattern for `MaxAsync` on an empty table
**Options considered:**
1. `await _context.ClassificationRules.MaxAsync(r => (int?)r.Order) ?? 0` (spec's approach).
2. `_context.ClassificationRules.Any() ? await _context.ClassificationRules.MaxAsync(r => r.Order) : 0` (existence check first, two queries).

**Chosen approach:** Option 1.

**Rationale:** `Order` is a non-nullable `int` on `ClassificationRule`, and EF Core's `MaxAsync` throws `InvalidOperationException` on an empty non-nullable sequence. Casting to `int?` inside the projection lets the provider translate the whole thing into one `SELECT MAX([Order]) FROM ...` (returning `NULL` for an empty table) and `?? 0` maps that back to the handler's existing "no rules yet → start at 1" semantics. This is a single round trip; option 2 costs an extra query for no benefit and is the kind of thing this fix is explicitly trying to eliminate.

## Implementation Guidance

### Directory / Module Structure
No new files or folders. Modify these three existing files only:
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRuleRepository.cs` — add `Task<int> GetMaxOrderAsync();` to the interface (alongside the existing method declarations, order doesn't matter but keep it near `GetAllAsync`/`GetActiveRulesOrderedAsync` for readability).
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleRepository.cs` — add the `GetMaxOrderAsync()` implementation as specified in FR-2, placed near `GetAllAsync`/`GetActiveRulesOrderedAsync`.
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/CreateClassificationRule/CreateClassificationRuleHandler.cs` — replace lines 29–30 with the single-line call per FR-3.

Do not touch `GetAllAsync`, `GetActiveRulesOrderedAsync`, `ReorderRulesAsync`, or any other repository method — they are unrelated to this fix and `GetAllAsync` is still needed elsewhere (rule listing).

### Interfaces and Contracts
```csharp
// IClassificationRuleRepository.cs — new member
Task<int> GetMaxOrderAsync();
```
```csharp
// ClassificationRuleRepository.cs — new implementation
public async Task<int> GetMaxOrderAsync()
{
    return await _context.ClassificationRules.MaxAsync(r => (int?)r.Order) ?? 0;
}
```
No public/HTTP-facing contract changes — `IClassificationRuleRepository` is an internal (Domain-layer) interface, not a cross-module `Contracts/` type, so the "DTOs live in contracts/" rule from `docs/architecture/development_guidelines.md` does not apply here.

### Data Flow
1. `POST` request reaches `CreateClassificationRuleHandler.Handle` via the existing controller → MediatR pipeline (unchanged).
2. Handler resolves current user (unchanged) and now calls `_ruleRepository.GetMaxOrderAsync()` instead of `GetAllAsync()` + in-memory `Max`.
3. Repository issues `SELECT MAX([Order]) FROM ClassificationRules` (or the InMemory-provider equivalent in tests) and returns a single `int` (0 if the table is empty).
4. Handler proceeds exactly as before: constructs the new `ClassificationRule`, calls `rule.SetOrder(maxOrder + 1)`, persists via `AddAsync`, maps to `ClassificationRuleDto`, returns response.

Behavior is bit-for-bit identical from the caller's perspective; only the query shape changes.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| EF Core `MaxAsync` on an empty table with the `(int?)` cast doesn't translate as expected on some provider (e.g. InMemory provider used in tests vs. real SQL provider in production) | Low | Add the repository-level test described below to lock in empty-table → `0` behavior against the actual provider used in CI (InMemory), which is representative enough given the simplicity of the query. |
| Concurrent `CreateClassificationRule` calls can still read the same `maxOrder` and both assign the same `Order` (pre-existing race, called out in brief and spec) | Low | Explicitly out of scope for this fix (per spec's "Out of Scope" section). Pre-existing behavior, not introduced or worsened by moving the aggregation into the DB — narrowing the read from a full table scan to a scalar query does not widen the race window. Accepted as residual risk; file separately if it needs addressing. |
| Interface change (`IClassificationRuleRepository`) requires any test doubles/mocks of this interface to add the new member | Low | Search shows no existing `CreateClassificationRuleHandlerTests` or hand-rolled mock of `IClassificationRuleRepository` in `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/` today; if a mocking framework (Moq/NSubstitute) is used elsewhere for this interface, it auto-satisfies new members without extra setup unless `GetMaxOrderAsync()` is actually invoked in that test path. |

## Specification Amendments
None. FR-1 through FR-3, the acceptance criteria, and the NFRs are implementable exactly as written against the current source — verified against the live files: `IClassificationRuleRepository.cs`, `ClassificationRuleRepository.cs`, and `CreateClassificationRuleHandler.cs` (lines 29–30 match the spec's quoted snippet exactly).

One clarification for the implementer (not a spec change): the codebase's `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/` folder has no dedicated `ClassificationRuleRepositoryTests.cs` today (only `ClassificationHistoryRepositoryTests.cs` exists as a sibling repository-test pattern, using `UseInMemoryDatabase` + direct repository instantiation — no mocking). Follow that same pattern if adding a test per the spec's Open Questions: instantiate `ClassificationRuleRepository` against an `ApplicationDbContext` backed by `UseInMemoryDatabase(Guid.NewGuid())`, and assert `GetMaxOrderAsync()` returns `0` on an empty set and the correct max on a populated one. This is proportional — one small test file (or a couple of methods added to a new minimal `ClassificationRuleRepositoryTests.cs`), not new test infrastructure.

## Prerequisites
None. No schema changes, no migrations, no configuration, no infrastructure work. The fix can be implemented directly against `main`/the current worktree state.
