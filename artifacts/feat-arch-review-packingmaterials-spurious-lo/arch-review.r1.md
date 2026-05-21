# Architecture Review: Decouple PackingMaterials Daily Run Idempotency From The Audit Log

## Skip Design: true

## Architectural Fit Assessment

The proposed change is a clean fit for the existing PackingMaterials vertical slice. The module already separates `PackingMaterial` (aggregate), `PackingMaterialLog` (audit fact), and `PackingMaterialConsumption` (per-invoice fact). Adding `PackingMaterialDailyRun` as a fourth, deliberately standalone "operations metadata" entity follows the established pattern of one entity per concern. It also closes a real semantic leak — the audit log was being used as a control-plane signal, which the existing entity layout never intended.

Integration points are narrow and already-shaped:
- **Persistence**: extend `IPackingMaterialRepository` + `PackingMaterialRepository` (no new repository), add one `IEntityTypeConfiguration<>`, one `DbSet<>` registration in `ApplicationDbContext`, one EF Core migration with a backfill `Sql(...)` block (the pattern used in `20251208184900_AddTransferIdColumnWithDataHandling`).
- **Domain**: one new aggregate-less entity in `Anela.Heblo.Domain.Features.PackingMaterials`, mirroring the shape of `PackingMaterialLog` (`IEntity<int>`, protected EF ctor, private setters).
- **Application**: one localized rewrite in `ConsumptionCalculationService.ProcessDailyConsumptionAsync` (remove marker block, enqueue daily-run row before `SaveChangesAsync`, add catch for the Postgres unique-constraint violation).
- **Boundaries**: no MediatR contract change, no DTO change, no controller change, no module-boundary impact. `DailyConsumptionJob` and `ProcessDailyConsumptionHandler` stay untouched.

The change also removes a hidden invariant (the `AsNoTracking` constraint encoded as a comment) — this is a net architectural improvement, not just a bug fix.

## Proposed Architecture

### Component Overview

```
                  ┌──────────────────────────────────────┐
Hangfire ───────► │  DailyConsumptionJob (unchanged)     │
                  └──────────────┬───────────────────────┘
                                 │ MediatR
                                 ▼
                  ┌──────────────────────────────────────┐
                  │  ProcessDailyConsumptionHandler      │
                  │  (unchanged)                         │
                  └──────────────┬───────────────────────┘
                                 │
                                 ▼
                  ┌──────────────────────────────────────┐
                  │  ConsumptionCalculationService       │
                  │  (only this changes)                 │
                  │                                      │
                  │  1. HasDailyProcessingBeenRunAsync   │
                  │     (now backed by DailyRun table)   │
                  │  2. Compute consumption              │
                  │  3. Decrement materials + add logs   │
                  │  4. AddConsumptionRowsAsync          │
                  │  5. AddDailyRunAsync (NEW)           │
                  │  6. SaveChangesAsync                 │
                  │     └─ catches PG 23505 → WasRun=F   │
                  └──────────────┬───────────────────────┘
                                 │
                                 ▼
                  ┌──────────────────────────────────────┐
                  │  IPackingMaterialRepository          │
                  │  + HasDailyProcessingBeenRunAsync()  │ ◄── reads DailyRun
                  │  + AddDailyRunAsync()        (NEW)   │
                  └──────────────┬───────────────────────┘
                                 │ EF Core (single SaveChangesAsync = one tx)
                                 ▼
   PackingMaterials   PackingMaterialLogs   PackingMaterialConsumptions
                                    +
                  ┌──────────────────────────────────────┐
                  │  PackingMaterialDailyRuns (NEW)      │
                  │  ───────────────────────────────────  │
                  │  Id, Date (UNIQUE), ProcessedAt,     │
                  │  MaterialsProcessed                  │
                  └──────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: New table, not a singleton row
**Options considered:** (A) dedicated table, (B) singleton settings row with `LastProcessedDate`.
**Chosen approach:** Dedicated `PackingMaterialDailyRuns` table with one row per processed date.
**Rationale:** The spec already selected this; architecturally it is the correct call because the daily run is a chronological *event*, not configuration. A singleton row cannot answer "was March 12 processed?" without auxiliary state, would need its own concurrency protection (row lock or version stamp), and forecloses future per-run metadata (duration, retry count, status) that a row-per-day table absorbs by simply adding columns.

#### Decision 2: Concurrency via unique index + caught `DbUpdateException`
**Options considered:**
- (A) Try-insert and catch the unique-constraint violation (Postgres SqlState `23505`).
- (B) `SELECT … FOR UPDATE` advisory lock or a Postgres advisory lock on a hash of the date.
- (C) Raw `INSERT … ON CONFLICT DO NOTHING` SQL.

**Chosen approach:** (A) — let the unique index on `Date` be the source of truth, catch the resulting `DbUpdateException` whose `InnerException` is a `PostgresException` with `SqlState == "23505"`, and translate to `WasRun = false`.

**Rationale:**
- The unique index already needs to exist for FR-5; (B) and (C) add infrastructure on top of it.
- A single `SaveChangesAsync` is wrapped in an implicit transaction by EF Core + Npgsql, so a unique-violation rollback automatically also reverts the consumption decrements and the consumption-fact inserts that were enqueued in the same call. No partial application is possible — directly satisfies FR-5's "losing call rolls back consumption changes".
- The codebase uses EF idiomatically; raw SQL `ON CONFLICT` would be an outlier and require a separate round-trip to load the materials first.
- Advisory locks add a third coordination mechanism for a path that runs at most twice per day in adversarial conditions.

**Implication:** the service must specifically detect Postgres error code `23505` on the `IX_PackingMaterialDailyRuns_Date` index and not swallow other `DbUpdateException`s. Use a private helper like `IsDuplicateDailyRunViolation(DbUpdateException ex)` so the check is localized and testable.

#### Decision 3: Entity lives in Domain, not Persistence
**Options considered:** keep `PackingMaterialDailyRun` as a persistence-only type vs. a domain entity.
**Chosen approach:** Domain entity in `Anela.Heblo.Domain.Features.PackingMaterials`, mirroring `PackingMaterialLog`.
**Rationale:** Consistent with sibling types (`PackingMaterialLog`, `PackingMaterialConsumption`), and the repository contract already lives in Domain (`IPackingMaterialRepository`). The entity has identity (`IEntity<int>`) and a small invariant (Date must be set, ProcessedAt is UTC), so a Domain class — not a record — matches `PackingMaterial`/`PackingMaterialLog` style. No business behavior other than a constructor; no `Update*` methods.

#### Decision 4: Same `SaveChangesAsync` for daily-run row and consumption side effects
**Options considered:** Separate `SaveChangesAsync` for the daily-run row vs. all-in-one.
**Chosen approach:** Single `SaveChangesAsync` that flushes the consumption decrements (tracked entity mutations), the consumption fact rows, and the daily-run row together.
**Rationale:** Atomicity. If the daily-run row insert fails (concurrency, transient DB error), the consumption decrements must also roll back; otherwise we re-process on the next run and double-decrement. EF Core + Npgsql wrap a single `SaveChangesAsync` in an implicit transaction, so this is free with the existing repository surface.

#### Decision 5: Repository surface — one new method, reuse existing interface
**Options considered:** Add `AddDailyRunAsync` to `IPackingMaterialRepository` vs. introduce `IPackingMaterialDailyRunRepository`.
**Chosen approach:** Add `AddDailyRunAsync(PackingMaterialDailyRun, CancellationToken)` to `IPackingMaterialRepository`.
**Rationale:** The spec mandates "no new repository interface" (NFR-3). This also matches the existing precedent — `PackingMaterialConsumption` lives behind `AddConsumptionRowsAsync` on the same repository, not behind its own interface. The mock test infrastructure (`MockPackingMaterialRepository`) extends naturally without a new mock class.

#### Decision 6: Backfill — accept `MIN(CreatedAt) GROUP BY Date` (resolves OQ-1)
**Options considered:** `MIN(CreatedAt)` per date vs. `now()` for all backfilled rows.
**Chosen approach:** `MIN(CreatedAt) GROUP BY Date` in plain Postgres SQL.
**Rationale:** Postgres handles the `GROUP BY` natively in tens-of-thousands-of-rows volume in well under a second. The slightly more accurate timestamp costs essentially nothing and avoids hand-waving the historical record. Single provider (Postgres) — no cross-provider SQL portability concern.

#### Decision 7: Result shape unchanged (resolves OQ-2)
**Options considered:** Extend `ProcessDailyConsumptionResult` with a `Reason` enum vs. keep `(WasRun, MaterialsProcessed)`.
**Chosen approach:** Keep the two-field shape. Distinguish idempotent-skip from concurrency-loss only in logging.
**Rationale:** No current caller branches on the distinction. `ProcessDailyConsumptionHandler` already collapses `!WasRun` into a single "already processed" response. NFR-4 already mandates a distinct log event name (`PackingMaterialsDailyRunDuplicateDetected`) for the concurrency-loser path — that is the right place for the distinction. Adding a `Reason` field now is YAGNI. If a future caller needs the distinction, the field is a non-breaking addition.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Domain/Features/PackingMaterials/
├── PackingMaterialDailyRun.cs              ← NEW
└── IPackingMaterialRepository.cs           ← modify (add AddDailyRunAsync)

backend/src/Anela.Heblo.Persistence/PackingMaterials/
├── PackingMaterialDailyRunConfiguration.cs ← NEW
└── PackingMaterialRepository.cs            ← modify (impl + change HasDailyProcessingBeenRunAsync)

backend/src/Anela.Heblo.Persistence/
├── ApplicationDbContext.cs                  ← modify (add DbSet<PackingMaterialDailyRun>)
└── Migrations/
    └── YYYYMMDDhhmmss_AddPackingMaterialDailyRunsTable.cs  ← NEW (table + index + backfill)

backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/
└── ConsumptionCalculationService.cs         ← modify (remove marker, enqueue daily-run, catch PG 23505)

backend/test/Anela.Heblo.Tests/Features/PackingMaterials/
├── MockPackingMaterialRepository.cs         ← modify (add AddedDailyRuns + AddDailyRunAsync)
└── ConsumptionCalculationServiceTests.cs    ← modify (rename + rewrite the two marker tests, add new tests)
```

No changes to `PackingMaterialsModule.cs` (DI registration is by interface, which is unchanged). No changes to `DailyConsumptionJob`, `ProcessDailyConsumptionHandler`, or any contracts in `Application/Features/PackingMaterials/Contracts/`.

### Interfaces and Contracts

**Domain entity (`PackingMaterialDailyRun.cs`):**

```csharp
public class PackingMaterialDailyRun : IEntity<int>
{
    public int Id { get; private set; }
    public DateOnly Date { get; private set; }
    public DateTime ProcessedAt { get; private set; }
    public int MaterialsProcessed { get; private set; }

    protected PackingMaterialDailyRun() { }

    public PackingMaterialDailyRun(DateOnly date, int materialsProcessed)
    {
        Date = date;
        ProcessedAt = DateTime.UtcNow;
        MaterialsProcessed = materialsProcessed;
    }
}
```

`ProcessedAt` is set inside the constructor (not by the caller) to keep the invariant local to the entity, matching `PackingMaterialLog.CreatedAt`. The single constructor signature also limits the surface for misuse.

**Repository contract delta:**

```csharp
// IPackingMaterialRepository.cs — add:
Task AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default);
// HasDailyProcessingBeenRunAsync signature unchanged; implementation re-targeted.
```

**EF configuration (`PackingMaterialDailyRunConfiguration.cs`):**

- Table: `PackingMaterialDailyRuns`, schema `public` (matches the current state of `PackingMaterialConfiguration` and `PackingMaterialLogConfiguration` — *not* the `dbo` schema used in the original 2025-11-18 migration).
- `Id` identity PK.
- `Date` `date NOT NULL` with **unique** index `IX_PackingMaterialDailyRuns_Date`.
- `ProcessedAt` `timestamp without time zone NOT NULL` (matches `PackingMaterialLog.CreatedAt`).
- `MaterialsProcessed` `int NOT NULL`, default `0`.

**Repository implementation deltas:**

```csharp
public async Task<bool> HasDailyProcessingBeenRunAsync(
    DateOnly date, CancellationToken cancellationToken = default)
{
    return await Context.Set<PackingMaterialDailyRun>()
        .AnyAsync(r => r.Date == date, cancellationToken);
}

public async Task AddDailyRunAsync(
    PackingMaterialDailyRun run, CancellationToken cancellationToken = default)
{
    await Context.Set<PackingMaterialDailyRun>().AddAsync(run, cancellationToken);
}
```

**Service delta (`ConsumptionCalculationService.ProcessDailyConsumptionAsync`):**

1. Delete lines 69–74 (marker block + load-bearing comment) entirely.
2. After the existing `AddConsumptionRowsAsync` call, add:
   ```csharp
   var dailyRun = new PackingMaterialDailyRun(processingDate, processedCount);
   await _repository.AddDailyRunAsync(dailyRun, cancellationToken);
   ```
3. Wrap the existing `SaveChangesAsync` call in `try`/`catch (DbUpdateException ex) when IsDuplicateDailyRunViolation(ex)`. The catch returns `new ProcessDailyConsumptionResult(false, 0)` and logs a `Warning` with event name `PackingMaterialsDailyRunDuplicateDetected` per NFR-4. Any other `DbUpdateException` propagates unchanged (so the MediatR handler's generic catch still handles it).
4. After successful `SaveChangesAsync`, add the structured `Information` log `PackingMaterialsDailyRunRecorded` with `ProcessingDate` and `MaterialsProcessed`.

**`IsDuplicateDailyRunViolation` helper:**

```csharp
private static bool IsDuplicateDailyRunViolation(DbUpdateException ex) =>
    ex.InnerException is PostgresException pg
    && pg.SqlState == PostgresErrorCodes.UniqueViolation
    && string.Equals(pg.ConstraintName, "IX_PackingMaterialDailyRuns_Date", StringComparison.Ordinal);
```

Pinning the constraint name keeps the catch surgical — it won't accidentally swallow a unique-violation on some other future index.

### Data Flow

**Happy-path daily run (non-empty stock, with invoices):**

```
DailyConsumptionJob (Hangfire 03:00)
  → MediatR ProcessDailyConsumptionRequest
    → ConsumptionCalculationService.ProcessDailyConsumptionAsync(date)
      1. HasDailyProcessingBeenRunAsync(date) → false  (queries PackingMaterialDailyRuns)
      2. Load materials (tracking) + invoices for date
      3. For each material with total > 0: material.UpdateQuantity(...) [adds PackingMaterialLog row]
      4. AddConsumptionRowsAsync(allFactRows)
      5. AddDailyRunAsync(new PackingMaterialDailyRun(date, processedCount))
      6. SaveChangesAsync  ← single transaction commits: material updates, log rows, consumption rows, daily-run row
    → returns (WasRun=true, MaterialsProcessed=N)
```

**Empty-stock or zero-invoices path:**

```
  1. HasDailyProcessingBeenRunAsync(date) → false
  2. Load materials (possibly empty) + invoices (possibly empty)
  3. No material decrements, no consumption-fact rows, no PackingMaterialLog rows
  4. AddDailyRunAsync(new PackingMaterialDailyRun(date, 0))
  5. SaveChangesAsync commits only the daily-run row
  → returns (WasRun=true, MaterialsProcessed=0)
  Next invocation for the same date: step 1 returns true → returns (false, 0).
```

**Concurrency-loser path:**

```
  Worker A and Worker B both pass step 1 (both see false).
  Both load materials, compute, enqueue decrements + daily-run row.
  Worker A SaveChangesAsync first → commits.
  Worker B SaveChangesAsync → unique-violation on Date.
    - EF Core rolls back the transaction (decrements, fact rows, daily-run row all uncommitted).
    - DbUpdateException caught; structured Warning logged.
    - Returns (WasRun=false, MaterialsProcessed=0).
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Catching `DbUpdateException` too broadly masks unrelated DB errors | High | Pin the catch to PG SqlState `23505` *and* `ConstraintName == "IX_PackingMaterialDailyRuns_Date"`. Re-throw all other `DbUpdateException`s. Cover with a test that injects a different `DbUpdateException` and asserts it propagates. |
| Backfill SQL syntax differs across deployments / breaks if `PackingMaterialLogs` table is empty | Medium | The backfill is `INSERT INTO ... SELECT MIN("CreatedAt"), 0, "Date" FROM "PackingMaterialLogs" WHERE "LogType" = 1 GROUP BY "Date"`. With no matching rows, this is a no-op `INSERT 0 0` — safe. Use Npgsql-quoted identifiers (the codebase's other `migrationBuilder.Sql(...)` blocks already quote identifiers this way). |
| Migration not auto-applied (project convention: manual migrations) | Medium | Spec already calls this out (Dependencies). Add an explicit deployment-checklist entry: "Run new migration on each environment before deploying the application build that depends on the new table." Without it, `HasDailyProcessingBeenRunAsync` will throw on the missing table. |
| Schema drift between original `dbo`-schema migration and current `public`-schema configs | Medium | The current EF configurations all point to `public`. The new migration MUST also create the table in `public` (not `dbo`) to match. If for any environment the move from `dbo` to `public` has not been applied, the production deploy will fail on the new query — but that environment is already broken for the other PackingMaterials tables and is out of scope here. |
| Tracking-mode change to `GetAllWithAllocationsAsync` could regress in some other way | Low | This change *removes* the dependency on tracking for idempotency; the read path itself stays unchanged (still `Include(...).ToListAsync()` — tracking by default). The `material.UpdateQuantity(...)` calls remain functionally tracking-dependent for the *real* decrement path, but that is the same invariant any EF-based service has. Document in `memory/gotchas/` per Out of Scope. |
| `PackingMaterialLog` rows from the old marker behavior remain in production | Low | Spec explicitly leaves these in place. Add a one-liner to `memory/gotchas/` flagging that pre-cutover `AutomaticConsumption` log rows with `OldQuantity == NewQuantity` on the first material returned by `GetAllWithAllocationsAsync` are historical artifacts of the prior implementation. |
| Test mock divergence — `MockPackingMaterialRepository` does not enforce uniqueness | Low | Extend the mock with an `AddedDailyRuns` dictionary keyed by `Date`. Have `AddDailyRunAsync` throw a constructed `DbUpdateException` (with a `PostgresException` inner whose `SqlState == "23505"` and `ConstraintName == "IX_PackingMaterialDailyRuns_Date"`) when the key already exists. This lets the new concurrency-loser unit test exercise the catch path without an integration test. |

## Specification Amendments

1. **Resolve OQ-1 (backfill `ProcessedAt`)** — set `ProcessedAt = MIN(CreatedAt) GROUP BY Date`. Postgres handles this in well under the 10-second NFR-1 budget for the expected data volume.

2. **Resolve OQ-2 (result shape)** — keep `ProcessDailyConsumptionResult` as `(bool WasRun, int MaterialsProcessed)`. Distinguish the concurrency-loser path only through the `PackingMaterialsDailyRunDuplicateDetected` log event (NFR-4). No public API change.

3. **Schema clarification** — the new table MUST be created in the `public` schema to match the current EF configurations (`PackingMaterialConfiguration` and `PackingMaterialLogConfiguration` both target `public`), NOT the `dbo` schema used in the 2025-11-18 `AddPackingMaterialsTables` migration. The spec already says `public`; calling this out explicitly because it contradicts the original migration and is the source of the most likely "works locally, fails on deploy" failure mode.

4. **Catch specificity** — FR-5 currently says "catches this [DbUpdateException]". Tighten to: the catch MUST verify both `SqlState == "23505"` AND `ConstraintName == "IX_PackingMaterialDailyRuns_Date"`. Any `DbUpdateException` not matching this profile MUST propagate unchanged.

5. **Test extension** — add to FR-7 a fourth test: `ProcessDailyConsumptionAsync_PropagatesOtherDbUpdateExceptions` — feeds the mock a non-23505 `DbUpdateException` and asserts the service does not swallow it. This locks in Decision 2's catch specificity.

6. **`PackingMaterialDailyRun` constructor** — the entity takes `(DateOnly date, int materialsProcessed)` only; `ProcessedAt` is set to `DateTime.UtcNow` inside the constructor (do not accept it as a parameter). This keeps the "UTC timestamp" invariant local. Migration-backfilled rows go around the constructor by writing directly via SQL — that is fine and expected.

## Prerequisites

- **Database migration** must be added (`AddPackingMaterialDailyRunsTable` or similar) **and manually applied** to every target environment before deploying the application build that depends on it. Per CLAUDE.md, migrations are not part of the deploy pipeline. Without this, the application will throw on every `HasDailyProcessingBeenRunAsync` call.
- **Verify schema state** of `PackingMaterials` and `PackingMaterialLogs` tables on each environment — they must already be in the `public` schema for the new table (also in `public`) to coexist cleanly. If any environment still has them in `dbo`, that drift must be reconciled first (and is out of scope for this feature).
- **No new NuGet packages, no new configuration, no new external infrastructure, no DI registration changes.** The PackingMaterials module registration in `Program.cs` and `PackingMaterialsModule.cs` remains as-is.
- **Deployment ordering**: migration first, then code. Rolling back code without rolling back the migration is safe (the new table is simply unused); rolling back the migration after the new code is live will break the job.