# Design — InvoiceClassification: rule reorder collides with the unique `Order` index

No UI section — this is a backend-only fix. The frontend (`RulesList.tsx`, `useReorderClassificationRules`) already sends the correct request; nothing about its shape, its call site, or its error handling changes. The only observable difference to a user is that the drag-and-drop reorder they already trigger today (and which currently fails) will succeed.

## Component design

### `ClassificationRuleRepository.ReorderRulesAsync` (rewritten)

Sole change target. Current signature is kept exactly:

```csharp
Task ReorderRulesAsync(List<Guid> ruleIds)
```

Responsibility: given a list of rule ids in the caller's desired display sequence, persist that sequence as each row's `Order`, without ever writing a value that collides — even transiently — with a row's current `Order` (including rows *not* in `ruleIds`, e.g. inactive rows per FR-2).

**Algorithm** (permute-in-place via a disjoint temporary range, one explicit transaction, two `SaveChangesAsync` round-trips):

1. `var rules = await _context.ClassificationRules.Where(r => ruleIds.Contains(r.Id)).ToListAsync();` — unchanged fetch.
2. Build `orderedRules`: for each id in `ruleIds`, look up the matching row in `rules`; skip ids with no match (`FirstOrDefault` → `null` → skip). This preserves FR-3 (tolerant of unknown ids) and fixes the display order the caller asked for.
3. `var valuesToRedistribute = orderedRules.Select(r => r.Order).OrderBy(o => o).ToList();` — the *fixed set* of `Order` values already legally owned by exactly these rows. Reusing this set (instead of `1..N`) is what makes every write in step 5 collision-free against untouched rows (FR-2), because no value outside this set is ever touched.
4. `await using var transaction = await _context.Database.BeginTransactionAsync();`
5. **Phase 1 (temporary offset):** for `i` in `0..orderedRules.Count-1`, `orderedRules[i].SetOrder(-(i + 1))`. Then `await _context.SaveChangesAsync();`. Negative values are never used elsewhere in this codebase (`Order` is always assigned from `GetMaxOrderAsync() + 1`, i.e. ≥ 1), so `-(i+1)` for `i ≥ 0` can never collide with any existing row, active or inactive, at either phase.
6. **Phase 2 (final values):** for `i` in `0..orderedRules.Count-1`, `orderedRules[i].SetOrder(valuesToRedistribute[i])` — i.e., the row now in position `i` of the caller's requested sequence gets the `i`-th smallest value from the set collected in step 3. Then `await _context.SaveChangesAsync();`.
7. `await transaction.CommitAsync();`

No new public members, no new interface, no DI changes — `IClassificationRuleRepository` is untouched (FR-6).

```csharp
public async Task ReorderRulesAsync(List<Guid> ruleIds)
{
    var rules = await _context.ClassificationRules
        .Where(r => ruleIds.Contains(r.Id))
        .ToListAsync();

    var orderedRules = ruleIds
        .Select(id => rules.FirstOrDefault(r => r.Id == id))
        .Where(r => r != null)
        .Select(r => r!)
        .ToList();

    if (orderedRules.Count == 0)
    {
        return;
    }

    var valuesToRedistribute = orderedRules
        .Select(r => r.Order)
        .OrderBy(o => o)
        .ToList();

    await using var transaction = await _context.Database.BeginTransactionAsync();

    for (int i = 0; i < orderedRules.Count; i++)
    {
        orderedRules[i].SetOrder(-(i + 1));
    }
    await _context.SaveChangesAsync();

    for (int i = 0; i < orderedRules.Count; i++)
    {
        orderedRules[i].SetOrder(valuesToRedistribute[i]);
    }
    await _context.SaveChangesAsync();

    await transaction.CommitAsync();
}
```

The `orderedRules.Count == 0` short-circuit is a plain edge case (all-unknown-ids or empty `ruleIds`) — without it, `BeginTransactionAsync` would open and commit a no-op transaction; skipping it is cheaper and avoids an empty-list `SaveChangesAsync` round-trip.

Everything else in the class — `GetAllAsync`, `GetActiveRulesOrderedAsync`, `GetMaxOrderAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync` — is untouched (FR-5).

### No changes to any other component

`ReorderClassificationRulesHandler`, `ReorderClassificationRulesRequest`/`Response`, `InvoiceClassificationController`, `ClassificationRuleConfiguration`, the entity `ClassificationRule`, and every frontend file are unchanged. The fix is fully contained inside the repository method body.

### New test component: `ClassificationRuleRepositoryReorderIntegrationTests`

New file: `backend/test/Anela.Heblo.Tests/Persistence/InvoiceClassification/ClassificationRuleRepositoryReorderIntegrationTests.cs`.

Mirrors `GridLayoutRepositoryUpsertIntegrationTests.cs` structurally (same fixture, same manual-table-creation approach — `EnsureCreatedAsync`/`MigrateAsync` is not used because the full schema depends on the `vector` extension, unavailable on a plain `postgres:16` testcontainer image):

- `[Collection("PostgresIntegration")]`, `[Trait("Category", "Integration")]`, `IAsyncLifetime`.
- Constructor takes `PostgresSharedContainerFixture`.
- `InitializeAsync`: `_fixture.CreateDatabaseAsync("classificationrules")`, then raw-SQL `CREATE TABLE public."ClassificationRules"` with the columns and unique index from `ClassificationRuleConfiguration`/the applied migrations (see Data schema below), then build an `ApplicationDbContext` against that connection string.
- `DisposeAsync`: dispose the context.
- A private `SeedRuleAsync(...)` / raw-SQL insert helper to create rows with specific `Id`/`Order`/`IsActive` directly (bypassing the domain constructor, which always sets `Order = 0`) — needed to arrange rows at arbitrary starting `Order` values for the derangement and inactive-row scenarios.
- A private raw-SQL `ReadOrderAsync(Guid id)` / `ReadAllAsync()` helper to assert persisted state independent of the EF change tracker.

**Test cases** (map 1:1 to plan FR-1/FR-2/FR-3):

1. `ReorderRulesAsync_FullDerangement_PersistsRequestedSequenceWithoutThrowing` — seed 3 rows `A=1, B=2, C=3`; call `ReorderRulesAsync([C.Id, A.Id, B.Id])`; assert no exception, and re-read shows `C.Order=1, A.Order=2, B.Order=3`.
2. `ReorderRulesAsync_WhenAnInactiveRowHoldsAnIntermediateOrderValue_NeverCollidesWithIt` — seed 5 rows `Order=1..5`, mark the `Order=2` row `IsActive=false` (raw SQL `UPDATE`, matching how `UpdateClassificationRuleHandler` would flip the flag without changing `Order`); call `ReorderRulesAsync` with the 4 active ids in a new (non-identity) sequence; assert no exception, the inactive row's `Order` is still exactly `2`, and the 4 reordered rows' persisted `Order` values are exactly `{1,3,4,5}` matching the requested sequence (not `1..4`).
3. `ReorderRulesAsync_WithAnUnknownRuleId_SkipsItAndReordersTheRest` — seed 3 rows `A=1,B=2,C=3`; call `ReorderRulesAsync([Guid.NewGuid(), C.Id, A.Id])` (unknown id mixed in); assert no exception, and `C`/`A` end up holding the two smallest of their own `{1,3}` value set in the requested relative order, `B` untouched.

Each test asserts by re-reading via a fresh raw SQL query (not through the `_context` change tracker) to prove the values actually landed in Postgres and the unique index was never violated — this is precisely the class of defect the existing InMemory-provider test suite cannot detect (ticket's explicit ask), so no test is added to `ClassificationRuleRepositoryTests.cs`.

## Data schema

No entity, DTO, or migration changes. For the new test's manual table bootstrap, the schema to reproduce (column names/types from `ClassificationRuleConfiguration.cs` + the applied migrations, notably `RenameAccountingPrescriptionToAccountingTemplateCode` and `AddDepartmentToClassificationRules`, which post-date the original `CreateTable` migration):

```sql
CREATE SCHEMA IF NOT EXISTS public;
CREATE TABLE IF NOT EXISTS public."ClassificationRules" (
    "Id"                     uuid                          PRIMARY KEY,
    "Name"                   character varying(255)        NOT NULL,
    "RuleTypeIdentifier"     character varying(100)        NOT NULL,
    "Pattern"                character varying(1000)       NOT NULL,
    "AccountingTemplateCode" character varying(255)        NOT NULL,
    "Department"             character varying(255)        NULL,
    "Order"                  integer                       NOT NULL,
    "IsActive"               boolean                       NOT NULL DEFAULT true,
    "CreatedAt"              timestamp without time zone   NOT NULL,
    "UpdatedAt"              timestamp without time zone   NOT NULL,
    "CreatedBy"              character varying(255)        NOT NULL,
    "UpdatedBy"              character varying(255)        NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ClassificationRules_Order"
    ON public."ClassificationRules" ("Order");
```

(`RuleTypeIdentifier`/`Pattern` composite index and the `ClassificationHistory` table/FK are irrelevant to this test and intentionally omitted, matching the GridLayouts test's precedent of creating only the table(s) under test.)

No request/response DTO changes — `ReorderClassificationRulesRequest { List<Guid> RuleIds }` / `ReorderClassificationRulesResponse { bool Success }` and the `PUT api/InvoiceClassification/rules/reorder` wire contract are unchanged (FR-6).

## Key interactions

```
Drag-drop in RulesList.tsx (unchanged)
  → PUT api/InvoiceClassification/rules/reorder { ruleIds: [...] }  (unchanged)
  → ReorderClassificationRulesHandler.Handle (unchanged)
  → IClassificationRuleRepository.ReorderRulesAsync(ruleIds)         (body rewritten)
       BEGIN TRANSACTION
       UPDATE ClassificationRules SET "Order" = -1 WHERE "Id" = ...   ─┐
       UPDATE ClassificationRules SET "Order" = -2 WHERE "Id" = ...    │ Phase 1 (N statements,
       ...                                                             │ one SaveChangesAsync)
                                                                       ─┘
       UPDATE ClassificationRules SET "Order" = <redistributed> ...   ─┐
       ...                                                             │ Phase 2 (N statements,
                                                                       ─┘ one SaveChangesAsync)
       COMMIT
  → 200 OK { success: true }                                         (unchanged)
```

Both phases run inside the same explicit `IDbContextTransaction` (FR-4): if phase 2 throws for any reason, the transaction is never committed and Postgres rolls back phase 1's temporary negative values along with it — no row is ever left holding a temporary value once the call returns, whether it succeeds or throws. `await using` on the transaction guarantees rollback-on-dispose if `CommitAsync()` is never reached.

## Notes carried from the planning step (unresolved, do not require design changes)

- FR-2 (inactive-row collision) is included in this design as agreed scope; if a reviewer wants it split out, only test case 2 and the "own existing values" framing (vs. plain `1..N`) would be dropped — the transaction/two-phase mechanics are identical either way.
- FR-3's silent-skip-unknown-ids behavior is preserved as-is, not re-litigated here.
