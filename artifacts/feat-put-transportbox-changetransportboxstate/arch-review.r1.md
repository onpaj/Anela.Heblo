# Architecture Review: HTTP 409 Spike on `PUT /api/transport-boxes/{id}/state`

## Architectural Fit Assessment

The proposed work is a **defensive remediation inside an existing vertical slice** (`Features/Logistics/UseCases/ChangeTransportBoxState`) and a **new constraint on an existing aggregate** (`TransportBox`). It introduces zero new modules, no new contracts, no new endpoints, and no DTO changes — it stays entirely within the boundaries Anela.Heblo's Vertical Slice Architecture already defines and respects ADR-001 (single `ApplicationDbContext`).

Key integration points:

- **Handler** (`ChangeTransportBoxStateHandler.Handle`) — receives one new `catch` arm and structured-logging enrichment. The dispatch table (`CallBackMap`) and per-state callbacks are not refactored.
- **Repository** (`TransportBoxRepository.IsBoxCodeActiveAsync`) — fast-path is retained; one enum value is added to its `activeStates`.
- **EF entity configuration** (`TransportBoxConfiguration`) — gains a filtered unique index, then a migration is generated. The codebase already has the precedent for this pattern (`LeafletDocumentConfiguration` with `HasFilter("\"GraphItemId\" IS NOT NULL")`).
- **Error pipeline** (`BaseApiController.HandleResponse` → `[HttpStatusCode(Conflict)]` on `TransportBoxDuplicateActiveBoxFound`) — unchanged. The 409 wire contract remains intact.

The fit is clean. The only structural decision worth elevating is **where the `DbUpdateException` catch lives** (see Decision 1).

## Proposed Architecture

### Component Overview

```
                  HTTP PUT /api/transport-boxes/{id}/state
                                    │
                                    ▼
                       TransportBoxController
                                    │ (MediatR)
                                    ▼
              ┌─────────────────────────────────────────┐
              │   ChangeTransportBoxStateHandler.Handle │
              │                                         │
              │   1. Load box via repository            │
              │   2. AssignBoxCodeIfAny                 │
              │   3. Resolve transition + run callback  │
              │      └─► HandleNewToOpened              │
              │           ▼                             │
              │       IsBoxCodeActiveAsync (FAST PATH)  │ ◄── FR-3: include Quarantine
              │           │                             │
              │           ├─ true  → return 409 (clean) │ ◄── FR-1: structured log
              │           └─ false → proceed            │     + lookup conflicting box
              │   4. transition.ChangeStateAsync (mut.) │
              │   5. UpdateAsync + SaveChangesAsync     │
              │      └─ DbUpdateException (PG 23505)?   │ ◄── FR-2: race-tail catch
              │         └─ classify constraint name     │
              │           └─ matches our index?         │
              │              ├─ yes → return 409        │ ◄── FR-1: structured log
              │              └─ no  → rethrow (→ 500)   │     + Source="DbConstraint"
              │                                         │
              │   ValidationException → 400 (existing)  │
              │   Other Exception     → 500 (existing)  │
              └─────────────────────────────────────────┘
                                    │
                                    ▼
                       PostgreSQL: TransportBoxes
                  ┌───────────────────────────────────┐
                  │  IX_TransportBoxes_Code_Active    │
                  │  UNIQUE (Code) WHERE State IN     │
                  │  ('New','Opened','InTransit',     │
                  │   'Received','Reserve',           │
                  │   'Quarantine')                   │
                  └───────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Place the `DbUpdateException` catch in `Handle`, not `HandleNewToOpened`

**Options considered:**
1. Wrap `IsBoxCodeActiveAsync` + `SaveChangesAsync` inside `HandleNewToOpened`.
2. Add a new try/catch around just the `await _repository.SaveChangesAsync(...)` line in `Handle`.
3. Add a third catch arm at `Handle` level, between the existing `ValidationException` and `Exception` arms.

**Chosen approach:** Option 3 — a dedicated `catch (DbUpdateException ex) when (IsDuplicateActiveBoxCodeViolation(ex))` arm at the same level as `catch (ValidationException)`.

**Rationale:** `SaveChangesAsync` is invoked in `Handle` (line 125), well after `HandleNewToOpened` has returned. The callback cannot catch what it does not own. Attempting to push the catch into the callback would require restructuring the dispatch (`CallBackMap`) — the spec's "Out of Scope" explicitly forbids that. A `when` filter keeps the duplicate path narrow: any other `DbUpdateException` (FK violation, check constraint, transient infra fault) falls through to the generic `catch (Exception)` and is mapped to `TransportBoxStateChangeError` exactly as today. The classifier (`IsDuplicateActiveBoxCodeViolation`) inspects `Npgsql.PostgresException.SqlState == "23505"` AND `ConstraintName == "IX_TransportBoxes_Code_Active"`; matching by constraint name (not message text) is robust against locale/version drift.

#### Decision 2: Encode the filter on the **string** column, not the enum integer

**Options considered:** integer-valued IN clause vs. string-valued IN clause.

**Chosen approach:** String literals: `WHERE "State" IN ('New','Opened','InTransit','Received','Reserve','Quarantine')`.

**Rationale:** `TransportBoxConfiguration` stores `State` via `.HasConversion<string>()`. The column is `text`/`varchar`, so an integer-based filter would never match any row. Any developer copy-pasting from another module's filtered-index migration must be aware of this. Capture this in the migration's comment so the next maintainer doesn't "fix" it back to integers.

#### Decision 3: Detect violations by `ConstraintName`, not message text

**Options considered:** parse `ex.InnerException.Message` for the index name; rely on `PostgresException.ConstraintName`; rely on `SqlState`.

**Chosen approach:** Combined check: `SqlState == "23505"` (unique-violation class) **AND** `ConstraintName == "IX_TransportBoxes_Code_Active"`.

**Rationale:** Using only `SqlState` would mis-classify any future unique constraint added to the table. Using only constraint name is fine but `SqlState` is a cheap pre-filter. Message-text matching is fragile across PostgreSQL versions and locales — avoid.

#### Decision 4: Race-condition test must use a **real PostgreSQL** instance, not InMemory

**Options considered:**
1. Add the new race test to existing `TransportBoxUniquenessTests` (uses `UseInMemoryDatabase`).
2. Add a Testcontainers-based integration test in a new file.
3. Mock `DbUpdateException` to flow through the handler.

**Chosen approach:** Option 2 — a new integration test class (e.g., `TransportBoxRaceConditionTests`) that boots a PostgreSQL Testcontainer, applies migrations, and triggers two concurrent `New → Opened` transactions.

**Rationale:** EF Core's InMemory provider **does not enforce filtered unique indexes**. A test in `TransportBoxUniquenessTests` would pass on an empty constraint and prove nothing. Mocking `DbUpdateException` (option 3) covers only the catch logic, not the actual race. The existing InMemory tests stay as-is — they validate the fast-path. The Quarantine fix in FR-3 is exercised at the InMemory level (no constraint needed).

#### Decision 5: Make the migration **fail-fast** rather than self-heal

**Options considered:** silently delete duplicates; deactivate older duplicates with a state change; abort the migration if duplicates exist.

**Chosen approach:** Abort. The migration runs a guard query (`SELECT COUNT(*) FROM (SELECT "Code", "State" ... GROUP BY ... HAVING COUNT(*) > 1)`) and `RAISE EXCEPTION` if any duplicates remain. The runbook (NFR-3) tells the operator how to inspect and resolve them first.

**Rationale:** Migrations are applied **manually** in this project (see `CLAUDE.md` project facts). An operator running the migration is in the loop and can resolve duplicates with full domain context. A self-healing migration could destroy real boxes that an operator would have reconciled differently. Fail-fast preserves the operator's authority.

## Implementation Guidance

### Directory / Module Structure

All changes are amendments to existing files — **no new modules, no new directories**.

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxConfiguration.cs` | Add `builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("IX_TransportBoxes_Code_Active").HasFilter("\"State\" IN ('New','Opened','InTransit','Received','Reserve','Quarantine')")` |
| `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` | Add `TransportBoxState.Quarantine` to `activeStates` array in `IsBoxCodeActiveAsync` |
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` | (a) New private static helper `IsDuplicateActiveBoxCodeViolation(DbUpdateException)`. (b) New `catch (DbUpdateException ex) when (IsDuplicateActiveBoxCodeViolation(ex))` arm in `Handle`. (c) Structured-log enrichment in `HandleNewToOpened` (lookup conflicting box via existing `GetByCodeAsync` only on the unhappy path) and in the generic `catch (Exception)` arm. |
| `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddUniqueIndexOnTransportBoxCodeActive.cs` (new) | Pre-flight `RAISE EXCEPTION` block + `CreateIndex` with filter. `Down` drops the index. |
| `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` | Auto-regenerated by `dotnet ef migrations add`. |
| `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` | Add Quarantine-specific InMemory test (FR-3). Existing tests stay. |
| `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxRaceConditionTests.cs` (new) | Testcontainers-based test forcing two concurrent `New → Opened` transactions on the same code; asserts exactly one succeeds and the other returns `TransportBoxDuplicateActiveBoxFound`. |
| `docs/integrations/` or PR description | Runbook (App Insights KQL, duplicate-detection SQL, remediation steps). The CLAUDE map does not have a `docs/runbooks/` folder; either add one or attach to the PR description as the spec allows. |

### Interfaces and Contracts

**No public interface changes.** The only new internal helper:

```csharp
// In ChangeTransportBoxStateHandler.cs — private, static, unit-testable.
private static bool IsDuplicateActiveBoxCodeViolation(DbUpdateException ex)
{
    // Anchor on Npgsql exception type + SQLSTATE + constraint name.
    // Match the index name verbatim so renames are caught at compile time
    // (define the name as a const both here and in TransportBoxConfiguration).
}
```

**Index name as a constant:** declare `internal const string ActiveCodeIndexName = "IX_TransportBoxes_Code_Active"` in `TransportBoxConfiguration` and reference it from both the EF config and the handler classifier. This prevents silent drift if the index is ever renamed.

**Logging contract** — fields used in App Insights queries become a stable contract:

```
ConflictReason = "DuplicateActiveBoxCode"   // both code-paths emit this
Source         = "FastPathCheck" | "DbConstraint"
BoxId, RequestedBoxCode, CurrentState, RequestedNewState
ConflictingBoxId, ConflictingBoxState       // only when known
```

### Data Flow

**Happy path (no duplicate):**

`Controller → MediatR → Handler.Handle → HandleNewToOpened → IsBoxCodeActiveAsync (false) → transition.ChangeStateAsync → SaveChangesAsync → return 200`

**Fast-path duplicate (sequential):**

`... → IsBoxCodeActiveAsync (true) → GetByCodeAsync (for conflicting box ID/state) → LogWarning → return 409 (TransportBoxDuplicateActiveBoxFound, Source="FastPathCheck")`

**Race-tail duplicate (concurrent — the bug being fixed):**

`Tx1 + Tx2 both pass IsBoxCodeActiveAsync (false) → both call SaveChangesAsync → DB serializes; first commits, second throws DbUpdateException(SqlState=23505, ConstraintName=IX_TransportBoxes_Code_Active) → catch arm classifies → LogWarning (Source="DbConstraint") → return 409`

The wire contract for the client is identical in both duplicate paths; only the log signature differentiates them, which is exactly what the diagnosis (FR-1) needs.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Production has pre-existing duplicate `(Code, State ∈ active)` rows; manual migration aborts | High | Pre-flight SQL in runbook; operator resolves before applying; migration has `RAISE EXCEPTION` guard so it fails closed |
| Including `Quarantine` in the active set breaks an existing test that depended on the missing-Quarantine bug | Medium | Audit existing `TransportBoxUniquenessTests` — none of the five tests transit through Quarantine, so risk is narrow; called out explicitly in PR (FR-3 acceptance criterion) |
| Filter clause uses integer values but column is string — index never matches | Medium | Decision 2 above; reviewer must verify migration SQL contains string literals; Quarantine PR description mentions this gotcha |
| Constraint-name drift between EF config and handler classifier | Low | Single `const` declaration referenced from both sites |
| EF Core InMemory in tests gives false confidence about constraint enforcement | Medium | New `TransportBoxRaceConditionTests` uses Testcontainers + real PostgreSQL; document in test class XML doc that InMemory cannot exercise this |
| `Npgsql.PostgresException` may not be the direct `InnerException` (could be wrapped) | Low | Classifier walks the inner-exception chain via `ex.GetBaseException()` or a small loop |
| Pgsql `DbUpdateException` from a transaction containing **multiple** entity updates (e.g., the `stocked` boxes loop) commits nothing — partial success appears not to happen but operator might assume the conflicting box is in a hybrid state | Low | Already covered by EF's transaction semantics; document in PR description |
| App Insights query syntax provided in PR is not actually exercised by the team | Low | Add the query verbatim and a screenshot of one matching trace within 24 h of deploy |

## Specification Amendments

1. **FR-1, conflicting-box lookup placement.** The spec says "looked up via `GetByCodeAsync`". `GetByCodeAsync` returns the *first* match ordered by non-Closed, then `Id` desc — which is fine for human diagnosis but **not** strictly equal to "the box currently holding the code in an active state". For diagnosis this is acceptable. Document the choice; do not introduce a new repository method.

2. **FR-2, catch placement.** The spec says "around the eventual `SaveChangesAsync` path (or the catch is added at the `Handle` level if cleaner)". **Pin this to `Handle` level** with a `when` filter — the alternative (try/catch around just `SaveChangesAsync`) hides the same control-flow inside generic `catch (Exception)` if the `when` clause misclassifies, which is harder to reason about. This is a binding architectural decision (Decision 1).

3. **FR-2, idempotency.** The spec says the migration is "idempotent". EF migrations are not idempotent by default (they fail if applied twice). Reword to: *the migration's pre-flight guard is idempotent — re-running on a database that already has the index is detected and skipped via `IF NOT EXISTS` on the `CREATE INDEX` statement, while the duplicate-row guard always runs and is safe to re-evaluate*.

4. **FR-3, test coverage.** The spec calls for "a unit test" covering the Quarantine race scenario. The Quarantine *application-level* check is fine to test in InMemory. The actual *race* (FR-2) is **not** unit-testable — it requires Testcontainers. Split into two tests (Decision 4): one InMemory unit test for FR-3, one Testcontainers integration test for FR-2.

5. **FR-4, optimistic concurrency.** The spec says "show via App Insights that no such exceptions are being thrown on this endpoint, or — if they are — add an explicit catch...". Tighten this: **the App Insights screenshot/query proving absence is an acceptance criterion, not optional**. Without it the FR-4 "documented and closed" check is unverifiable.

6. **NFR-3, runbook destination.** The spec offers "a runbook entry (or a section in the PR description)". The repo has no `docs/runbooks/` folder. Pick one: either create `docs/runbooks/transport-box-duplicate-codes.md` and link it from `CLAUDE.md`'s docs map (preferred for durability), or accept the PR description as the home (and link the PR from a one-line entry in `docs/integrations/` so future operators can find it).

## Prerequisites

Before implementation can start, confirm:

1. **Production duplicate inventory.** Run on production replica:
   ```sql
   SELECT "Code", "State", COUNT(*) AS dup_count
   FROM public."TransportBoxes"
   WHERE "Code" IS NOT NULL
     AND "State" IN ('New','Opened','InTransit','Received','Reserve','Quarantine')
   GROUP BY "Code", "State"
   HAVING COUNT(*) > 1;
   ```
   Plus the cross-state form:
   ```sql
   SELECT "Code", COUNT(*) AS active_count, ARRAY_AGG("Id" || ':' || "State") AS boxes
   FROM public."TransportBoxes"
   WHERE "Code" IS NOT NULL
     AND "State" IN ('New','Opened','InTransit','Received','Reserve','Quarantine')
   GROUP BY "Code"
   HAVING COUNT(*) > 1;
   ```
   Resolve any rows before applying the migration.

2. **Npgsql package availability** in `Anela.Heblo.Application`. Currently `PostgresException` lives in `Npgsql`, which is an Application-layer dependency? **Verify**: if `Npgsql` is only referenced from `Persistence`, the classifier in the handler must either (a) move to a small Persistence-layer helper exposed via interface, or (b) rely on `DbUpdateException.InnerException is { Source: "Npgsql" }`-style duck-typing with the SQLSTATE pulled out via reflection — option (a) is cleaner. Resolve this before coding the catch arm.

3. **Testcontainers + PostgreSQL** test scaffolding. The existing test project uses InMemory exclusively; check whether `Testcontainers.PostgreSql` is already wired up by any other test class. If not, the integration test setup is the one piece of net-new infrastructure this work introduces, and its first-time cost should be folded into the estimate.

4. **Manual migration deployment slot.** Per project facts ("Database migrations are manual"), confirm the operator can apply the migration in a maintenance window — no automated rollout will pick it up.