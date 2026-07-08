# Specification: Push MaxOrder computation into the database for InvoiceClassification rules

## Summary
`CreateClassificationRuleHandler` currently loads every `ClassificationRule` row from the database via `GetAllAsync()` just to compute the next `Order` value in memory. This change adds a targeted `GetMaxOrderAsync()` query to `IClassificationRuleRepository` / `ClassificationRuleRepository` and updates the handler to use it, eliminating the unnecessary full-table transfer. This is a small, self-contained tech-debt fix from an automated arch-review finding.

## Background
The arch-review finding (`artifacts/feat-3545/brief.md`) identified that `CreateClassificationRuleHandler.Handle` (lines 29-30) fetches the entire `ClassificationRules` table — every column of every row, ordered — solely to compute `Max(r => r.Order)` and discards the data immediately after. The fix is to move the aggregation into the database using EF Core's `MaxAsync`, which SQL Server/the underlying provider can execute as a single scalar aggregate query instead of a full table scan and network transfer.

The brief also notes that this pattern creates a race window where two concurrent `CreateClassificationRule` calls could read the same max and assign duplicate `Order` values. That race condition is **pre-existing and out of scope for this fix** — see Out of Scope / Open Questions.

## Functional Requirements

### FR-1: Add `GetMaxOrderAsync()` to `IClassificationRuleRepository`
Add a new method to the repository interface at `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRuleRepository.cs`:

```csharp
Task<int> GetMaxOrderAsync();
```

This returns the current maximum `Order` value across all `ClassificationRule` rows, or `0` if no rules exist.

**Acceptance criteria:**
- `IClassificationRuleRepository` declares `Task<int> GetMaxOrderAsync();`.
- Method signature returns `Task<int>` (not `Task`), matching the brief's intent despite the brief's pseudocode omitting the generic type.

### FR-2: Implement `GetMaxOrderAsync()` in `ClassificationRuleRepository`
Implement the method in `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleRepository.cs` as a targeted EF Core aggregate query:

```csharp
public async Task<int> GetMaxOrderAsync()
{
    return await _context.ClassificationRules.MaxAsync(r => (int?)r.Order) ?? 0;
}
```

The nullable cast (`(int?)r.Order`) is required so `MaxAsync` does not throw `InvalidOperationException` when the table is empty; the `?? 0` fallback preserves the existing behavior of `CreateClassificationRuleHandler` (empty table → order starts at 1, i.e., `maxOrder + 1`).

**Acceptance criteria:**
- The implementation issues a single aggregate query (e.g., `SELECT MAX(Order) FROM ClassificationRules`) rather than loading full rows.
- Returns `0` when the `ClassificationRules` table is empty.
- Returns the correct maximum `Order` value when rows exist.
- No change to any other repository method.

### FR-3: Update `CreateClassificationRuleHandler` to use the new method
Replace lines 29-30 in `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/CreateClassificationRule/CreateClassificationRuleHandler.cs`:

```csharp
var allRules = await _ruleRepository.GetAllAsync();
var maxOrder = allRules.Count > 0 ? allRules.Max(r => r.Order) : 0;
```

with:

```csharp
var maxOrder = await _ruleRepository.GetMaxOrderAsync();
```

The rest of the handler (rule construction, `SetOrder(maxOrder + 1)`, `AddAsync`, response mapping) is unchanged.

**Acceptance criteria:**
- `CreateClassificationRuleHandler.Handle` no longer calls `GetAllAsync()`.
- Behavior of the handler is otherwise identical: a newly created rule receives `Order = maxOrder + 1`, where `maxOrder` is the highest existing `Order` value (or 0 if none exist).
- `GetAllAsync()` remains on the interface/implementation unchanged (still used elsewhere, e.g. rule listing) — it is not removed.

## Non-Functional Requirements

### NFR-1: Performance
`GetMaxOrderAsync()` must execute as a single scalar aggregate query against the database (e.g., `SELECT MAX([Order]) FROM ClassificationRules`), not as an in-memory aggregation over a materialized list. This removes the O(n) row/column transfer that scaled with the total number of classification rules on every rule-creation call.

### NFR-2: Security
No change. This fix does not alter authentication, authorization, or data sensitivity — it is a read-path optimization on an already-accessible repository method.

## Data Model
No schema changes. Uses the existing `ClassificationRule.Order` (int) property on the existing `ClassificationRules` table via `ApplicationDbContext`.

## API / Interface Design
Internal repository interface change only — no public API, controller, or contract changes:

- New interface member: `Task<int> GetMaxOrderAsync()` on `IClassificationRuleRepository`.
- No changes to `CreateClassificationRuleRequest`, `CreateClassificationRuleResponse`, or any HTTP-facing contract.

## Dependencies
- Entity Framework Core `MaxAsync` extension (`Microsoft.EntityFrameworkCore`), already referenced in `ClassificationRuleRepository.cs`.
- No new packages or external services.

## Out of Scope
- Fixing the concurrent-duplicate-`Order` race condition mentioned in the brief's "Why it matters" section (two concurrent `CreateClassificationRule` calls could both read the same max and assign the same `Order`). This is pre-existing behavior, not introduced or worsened by this change, and is not part of this fix.
- Any change to `GetAllAsync()`, `GetActiveRulesOrderedAsync()`, `ReorderRulesAsync()`, or other repository methods.
- Any change to unrelated `CreateClassificationRuleHandler` logic (validation, user resolution, mapping).
- Adding database-level uniqueness constraints or locking/transaction changes to `Order` assignment.
- Unit/integration test additions beyond what's needed to cover the new method, if the team's existing test suite pattern requires one (see Open Questions).

## Open Questions
- Should a unit/integration test be added for `GetMaxOrderAsync()` (e.g., empty table → 0, populated table → correct max) as part of this change, or is this fix small enough to rely on existing `CreateClassificationRuleHandler` test coverage exercising the new code path indirectly? Recommend adding a minimal repository-level test if the project's existing test conventions cover other `ClassificationRuleRepository` methods (check `backend/test` for existing patterns) — otherwise proportional to the fix's size, no new test infra is warranted.
- The concurrent-duplicate-`Order` race condition is explicitly out of scope per task framing, but should it be filed as a separate follow-up arch-review/tech-debt item so it isn't lost? (No action required for this spec; flagging for visibility only.)

## Status: HAS_QUESTIONS
