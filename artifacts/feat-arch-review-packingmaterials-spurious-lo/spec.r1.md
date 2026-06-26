# Specification: Decouple PackingMaterials Daily Run Idempotency From The Audit Log

## Summary
`ConsumptionCalculationService.ProcessDailyConsumptionAsync` currently records that a day has been processed by writing a no-op log entry on `materials[0]` whenever no real consumption occurred. This pollutes the audit log and silently breaks idempotency when the materials list is empty. This spec replaces the marker hack with an explicit `PackingMaterialDailyRun` table that records one row per processed date, removing the coupling between idempotency tracking and the audit log.

## Background
The packing-materials module computes daily consumption by iterating materials and decrementing stock based on issued invoices for the day. To guarantee idempotency of the Hangfire-driven `DailyConsumptionJob` (cron `0 3 * * *`), the service today calls `HasDailyProcessingBeenRunAsync`, which queries `PackingMaterialLogs` for any row with `LogType == AutomaticConsumption` on the target date. When no material was decremented (e.g. a `PerOrder` material with zero invoices for the day, or a stockroom with zero materials configured), no natural log entry is produced — so the service writes a synthetic `OldQuantity == NewQuantity` entry on `materials[0]` purely to leave the sentinel behind.

Concrete problems with the current design:

1. **Audit log pollution.** Operators reading `PackingMaterialLogs` cannot distinguish a real `AutomaticConsumption` no-op on a specific material from the idempotency sentinel. The first material in `GetAllWithAllocationsAsync` ordering is arbitrarily privileged and accumulates phantom entries.
2. **Empty-stock failure.** The `if (processedCount == 0 && materials.Count > 0)` guard in `ConsumptionCalculationService.cs:70` means an environment with no `PackingMaterial` rows leaves zero log entries, so `HasDailyProcessingBeenRunAsync` keeps returning `false` and the job re-executes every cycle. This is a realistic onboarding state.
3. **Hidden invariant.** The marker write relies on EF Core change tracking, encoded only as a code comment ("`GetAllWithAllocationsAsync must NOT use AsNoTracking`"). A future repository refactor toward `AsNoTracking` would silently break idempotency without test failure on the read path.
4. **Misleading existing tests.** `ProcessDailyConsumptionAsync_WritesMarkerLog_WhenZeroConsumption` and `…_MixedTypes_ZeroInvoices_PerDayDecrementsPerOrderGetsMarkerLog` (in `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs`) codify the marker behavior, so they will need to be updated alongside the implementation.

The implementation team has selected **Option A (dedicated table)** from the brief; Option B (settings-singleton row) is rejected because the dedicated table preserves a chronological record of every processed day, supports future per-day metadata (e.g. processed-materials count, error markers, retries) without further schema changes, and queries cleanly without singleton-row coordination.

## Functional Requirements

### FR-1: Persist daily-run completion in a dedicated table
Introduce a new persistent entity `PackingMaterialDailyRun` that records one row per processed date. The row is written exactly once after `ProcessDailyConsumptionAsync` completes successfully for that date, regardless of how many materials were decremented (including zero).

**Acceptance criteria:**
- A new EF Core entity `PackingMaterialDailyRun` exists under `Anela.Heblo.Domain.Features.PackingMaterials` with at minimum: `Id` (int, identity), `Date` (`DateOnly`, unique), `ProcessedAt` (`DateTime` UTC), `MaterialsProcessed` (int).
- A new migration adds the `PackingMaterialDailyRuns` table in the `public` schema with a unique index on `Date` (`IX_PackingMaterialDailyRuns_Date`).
- After completion of `ProcessDailyConsumptionAsync` for a given `processingDate`, exactly one new `PackingMaterialDailyRun` row exists for that date.
- The row is written within the same transaction / `SaveChangesAsync` call as the consumption updates so that a partial failure leaves neither side committed.

### FR-2: Switch idempotency check to the new table
`HasDailyProcessingBeenRunAsync` queries the new `PackingMaterialDailyRun` table instead of `PackingMaterialLogs`.

**Acceptance criteria:**
- `IPackingMaterialRepository.HasDailyProcessingBeenRunAsync(DateOnly, CancellationToken)` returns `true` iff a `PackingMaterialDailyRun` row exists for the given date.
- The implementation no longer references `PackingMaterialLog.LogType == AutomaticConsumption` for idempotency.
- The check is short-circuit-safe under concurrent workers: the unique index on `Date` guarantees that a duplicate run insert fails with a `DbUpdateException` rather than silently double-processing. The service catches this and returns `WasRun = false` for that invocation (or surfaces a structured "already processed" result — see FR-5).

### FR-3: Remove the marker write from ConsumptionCalculationService
Delete the marker-on-`materials[0]` block at `ConsumptionCalculationService.cs:69–74`. Replace it with an explicit insert of a `PackingMaterialDailyRun` row.

**Acceptance criteria:**
- `ConsumptionCalculationService.cs` contains no synthetic `material.UpdateQuantity(currentQuantity, …)` call.
- After a run where every material had `total == 0`, no `PackingMaterialLog` rows are produced for any material on `processingDate`.
- The comment `// Relies on EF change tracking — GetAllWithAllocationsAsync must NOT use AsNoTracking` is removed because it is no longer load-bearing for idempotency. (`GetAllWithAllocationsAsync` may continue to be tracking; this spec does not require changing its tracking mode — see Out of Scope.)

### FR-4: Handle the empty-materials case
When `materials.Count == 0`, `ProcessDailyConsumptionAsync` still records a `PackingMaterialDailyRun` row for `processingDate` and returns `WasRun = true, MaterialsProcessed = 0`.

**Acceptance criteria:**
- Calling `ProcessDailyConsumptionAsync(date)` on an empty stock writes exactly one `PackingMaterialDailyRun` row for `date`.
- A second call to `ProcessDailyConsumptionAsync(date)` on an empty stock returns `WasRun = false, MaterialsProcessed = 0` and writes no further rows.
- The `DailyConsumptionJob` running on consecutive days on an empty-stock environment produces one row per day and never re-executes for a prior date.

### FR-5: Concurrency safety
Two concurrent invocations of `ProcessDailyConsumptionAsync` for the same date MUST result in exactly one `PackingMaterialDailyRun` row and one set of consumption side effects.

**Acceptance criteria:**
- The unique index on `Date` is enforced at the database level.
- The losing concurrent call detects the unique-constraint violation, rolls back its consumption changes, and returns `WasRun = false`. No partial application of consumption decrements is committed by the losing call.
- An integration test (or unit test against the EF in-memory + concurrency mock) demonstrates that two parallel calls produce a single committed run.

### FR-6: Backfill existing processed dates
Existing production deployments have `PackingMaterialLogs` rows with `LogType == AutomaticConsumption` going back to module rollout. The migration MUST seed `PackingMaterialDailyRun` rows for every distinct `Date` value already present in `PackingMaterialLogs` with `LogType == AutomaticConsumption`, so that historical idempotency is preserved across the deployment.

**Acceptance criteria:**
- The same migration that creates the table also executes a `INSERT INTO ... SELECT DISTINCT Date ...` backfill from `PackingMaterialLogs` where `LogType == AutomaticConsumption`.
- `ProcessedAt` for backfilled rows is set to the earliest matching log's `CreatedAt` for that date (or `now()` if that proves expensive — see Open Questions).
- `MaterialsProcessed` for backfilled rows is set to `0` (we have no reliable way to reconstruct the original count from logs).
- Running the migration against an empty `PackingMaterialLogs` table is a no-op and does not fail.

### FR-7: Update existing tests
The two test methods that codify the marker behavior must be rewritten to assert the new contract.

**Acceptance criteria:**
- `ProcessDailyConsumptionAsync_WritesMarkerLog_WhenZeroConsumption` (currently `ConsumptionCalculationServiceTests.cs:166`) is renamed (e.g. `…RecordsDailyRun_WhenZeroConsumption`) and asserts that (a) `material.Logs` is empty and (b) a `PackingMaterialDailyRun` row exists for the processing date.
- `ProcessDailyConsumptionAsync_MixedTypes_ZeroInvoices_PerDayDecrementsPerOrderGetsMarkerLog` is renamed and updated so that the `PerOrder` material has zero logs and the `PerDay` material has exactly its real consumption log; both runs are gated by the new `PackingMaterialDailyRun` row, not by the `PerDay` log.
- A new test `ProcessDailyConsumptionAsync_RecordsDailyRun_WhenMaterialListIsEmpty` covers FR-4.
- A new test `ProcessDailyConsumptionAsync_SecondRun_ReturnsWasRunFalse_WithoutMutating` covers FR-2 idempotency through the new path.
- The existing mock `MockPackingMaterialRepository` is extended with `AddedDailyRuns` and an in-memory store so all existing tests continue to compile and pass.

## Non-Functional Requirements

### NFR-1: Performance
- `HasDailyProcessingBeenRunAsync` against `PackingMaterialDailyRun` MUST return in p95 ≤ 5 ms on production-sized data (the table grows by one row per day, ~365/year, so this is effectively constant time given the unique index on `Date`).
- The backfill migration on a production-sized `PackingMaterialLogs` table (estimated tens of thousands of rows over the module's lifetime) MUST complete in under 10 seconds during deployment.

### NFR-2: Security
- No new external surface area (HTTP endpoints, MediatR requests, or DTOs) is introduced by this change. The new table is purely persistence-internal.
- No PII or secrets are stored in `PackingMaterialDailyRun`.
- Existing authorization on the `ProcessDailyConsumption` MediatR request and the Hangfire job is unchanged.

### NFR-3: Maintainability
- The hidden invariant about EF change tracking on `GetAllWithAllocationsAsync` is eliminated. After this change, switching that repository call to `AsNoTracking` MUST NOT break idempotency (manual code-review item; covered by FR-3 acceptance criteria).
- The new `PackingMaterialDailyRun` entity follows existing module conventions: domain class in `Anela.Heblo.Domain.Features.PackingMaterials`, EF configuration in `Anela.Heblo.Persistence.PackingMaterials`, repository access through `IPackingMaterialRepository` (no new repository interface).

### NFR-4: Observability
- After completion, the service logs structured information at `Information` level including `ProcessingDate`, `MaterialsProcessed`, and a stable event name (e.g. `PackingMaterialsDailyRunRecorded`).
- On the concurrency-loser path (FR-5), the service logs at `Warning` level with the same `ProcessingDate` and a distinct event name (e.g. `PackingMaterialsDailyRunDuplicateDetected`).

## Data Model

New entity:

```
PackingMaterialDailyRun
├── Id                int        (identity, PK)
├── Date              date       (unique, NOT NULL)
├── ProcessedAt       timestamp  (UTC, NOT NULL)
└── MaterialsProcessed int       (NOT NULL, default 0)
```

Indexes:
- `IX_PackingMaterialDailyRuns_Date` — unique, single column on `Date`.

No relationships to other entities. The table is a standalone audit-of-runs record, deliberately decoupled from `PackingMaterial` and `PackingMaterialLog`.

Existing entities (`PackingMaterial`, `PackingMaterialLog`, `PackingMaterialConsumption`, `PackingMaterialAllocation`) are unchanged. Existing `PackingMaterialLog` rows of type `AutomaticConsumption` are retained as-is (the previously-spurious entries on `materials[0]` remain in the historical record but no new ones are written; see Out of Scope for the cleanup question).

## API / Interface Design

No HTTP endpoint changes. No DTO changes. No MediatR request/response shape changes.

Repository interface change (`IPackingMaterialRepository`):
- `HasDailyProcessingBeenRunAsync(DateOnly, CancellationToken)` — semantics unchanged from the consumer's perspective; backed by `PackingMaterialDailyRuns` instead of `PackingMaterialLogs`.
- New method: `Task AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken)` (or equivalent — exact shape determined during implementation; the service must be able to enqueue a single insert before `SaveChangesAsync`).

Service flow after change:
1. `HasDayAlreadyBeenProcessedAsync` → if true, return early with `WasRun = false`.
2. Load materials and invoices.
3. Compute consumption, apply decrements, enqueue consumption fact rows.
4. Construct a new `PackingMaterialDailyRun(date, DateTime.UtcNow, processedCount)` and enqueue via `AddDailyRunAsync`.
5. `SaveChangesAsync` — single transaction. On `DbUpdateException` whose root cause is the unique-constraint on `Date`, log per NFR-4 and return `WasRun = false`.
6. Return `ProcessDailyConsumptionResult(true, processedCount)`.

## Dependencies
- EF Core 8 (already in use).
- Existing Hangfire-based `IRecurringJobStatusChecker` for `DailyConsumptionJob`.
- Existing `IPackingMaterialRepository` infrastructure and `ApplicationDbContext`.
- No new NuGet packages.
- A manual database migration step is required per project convention (`Database migrations are manual (not automated in deployment).` in CLAUDE.md). The migration MUST be added to `backend/src/Anela.Heblo.Persistence/Migrations/` and executed manually against each environment.

## Out of Scope
- Cleanup of pre-existing spurious `AutomaticConsumption` log rows on `materials[0]` from historical data. Those entries are left in place; their meaning will be documented in `memory/gotchas/` as a known historical artifact rather than retroactively deleted, to avoid mutating audit history.
- Changing `GetAllWithAllocationsAsync` to use `AsNoTracking`. This spec removes the *reason* the invariant exists; whether to actually switch the read path is a separate optimization decision.
- Adding richer metadata to `PackingMaterialDailyRun` (errors, retry counts, processing duration). The minimal schema in Data Model is sufficient for the current finding; additional fields can be added in future migrations.
- Exposing `PackingMaterialDailyRun` over the HTTP API or in any UI.
- Reworking `DailyConsumptionJob`'s cron, retry, or scheduling behavior.

## Open Questions

### OQ-1: Backfill `ProcessedAt` value
FR-6 specifies `ProcessedAt` = earliest matching `PackingMaterialLogs.CreatedAt` for that date. On production data, this requires a `MIN(CreatedAt) GROUP BY Date` subquery in the migration. If this proves slow or the SQL becomes awkward across providers, is it acceptable to instead set `ProcessedAt = now()` for all backfilled rows, accepting that the timestamp will represent the migration-deployment moment rather than the original run? The historical value is informational only — no business logic depends on it — so the simpler approach is probably fine, but please confirm.

### OQ-2: Concurrency-loser semantics for callers
FR-5 specifies that the losing concurrent call returns `WasRun = false`. The current `ProcessDailyConsumptionResult` shape is `(bool WasRun, int MaterialsProcessed)`, which does not distinguish "already processed (idempotent skip)" from "lost a concurrent race". Is the existing two-field shape sufficient (callers treat both as no-op), or should we extend the result to surface the concurrency-loss case distinctly for observability? The job (`DailyConsumptionJob.cs`) currently only branches on `result.Success` (which maps from `WasRun` in the MediatR handler) and would treat both equally — so the simpler answer is "leave it" unless there is a specific operator-facing need.
