# Architecture Review: Photobank nightly index job — close the DateTime Kind=Unspecified regression after PR #3743

## Skip Design: true

Backend-only: a health check, a one-line defensive mapping fix, test coverage, and documentation.
No new or changed UI components, screens, or visual design decisions.

## Architectural Fit Assessment

This fits an established pattern in this repo rather than introducing a new one:
`DataQualitySchemaHealthCheck` (`backend/src/Anela.Heblo.API/HealthChecks/DataQuality/DataQualitySchemaHealthCheck.cs`)
already exists to convert a near-identical EF-model/physical-schema drift incident (missing table,
`42P01`) into a `/health/ready` signal instead of a repeating background-job exception. This spec's
FR-1 is the same remediation shape applied to a different drift symptom (wrong column type instead
of missing table) on a different table family. `memory/gotchas/ef-migration-codebase-drift.md`
explicitly flags its own coverage gap ("does NOT cover the other tables ... broader coverage is
tracked as a follow-up") — this feature is that follow-up, scoped to Photobank.

The one new piece — FR-2's `DateTime.SpecifyKind` fix on `Photo.ModifiedAt` — mirrors
`memory/gotchas/smartsupp-staged-contact-datetime-kind.md`'s already-applied fix
(`MapContactDataToEntity` stamping `Kind=Utc` on externally-sourced timestamps). Same defensive
posture, same codebase, same author intent ("relabel, not shift").

Both integration points (health checks DI registration, `PhotobankIndexJob`) are small, well-bounded,
and already exercised by existing unit tests I can extend rather than new test infrastructure.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────┐        ┌──────────────────────────────────┐
│ ServiceCollectionExtensions  │──adds──▶│ PhotobankSchemaHealthCheck       │
│  .AddHealthCheckServices()   │        │  (IHealthCheck)                  │
└─────────────────────────────┘        │  - reads information_schema.     │
                                        │    columns via ApplicationDbContext│
                                        │    .Database.SqlQuery<T>()        │
                                        │  - read-only, no writes          │
                                        └──────────────────────────────────┘
                                                       │
                                                       ▼ probed by
                                        ┌──────────────────────────────────┐
                                        │ GET /health/ready (existing)      │
                                        │  tags: ready, db, schema          │
                                        └──────────────────────────────────┘

┌────────────────────────────────────────────────────────────────┐
│ PhotobankIndexJob.UpsertPhotoBatchAsync (existing, one line)     │
│   photo.ModifiedAt = item.LastModifiedAt.HasValue                │
│       ? DateTime.SpecifyKind(item.LastModifiedAt.Value, Utc)     │
│       : DateTime.UtcNow;                                         │
└────────────────────────────────────────────────────────────────┘
```

No new modules, no new DI lifetimes beyond one more `IHealthCheck` registration (transient, same as
`DataQualitySchemaHealthCheck`).

### Key Design Decisions

#### Decision 1: Drift detection reads `information_schema.columns`, not a live write probe

**Options considered:**
- (a) Attempt a real write (e.g. touch-and-revert a row) to reproduce the failure condition directly.
- (b) Query `information_schema.columns` for the physical `data_type`/`udt_name` of each tracked
  column and compare against the EF model's declared column type.

**Chosen approach:** (b).

**Rationale:** (a) is exactly the write that already reliably reproduces the production incident —
running it from a health check would turn a diagnostic probe into a second source of the very
failure it's meant to detect, on every readiness poll. `information_schema.columns` is read-only,
cheap, and gives an unambiguous per-column verdict without touching data. This also matches
`DataQualitySchemaHealthCheck`'s own read-only posture (`AnyAsync()` on the DbSet, not a write).

#### Decision 2: Query via `ApplicationDbContext.Database.SqlQuery<T>`, not a raw `NpgsqlCommand`

**Options considered:**
- (a) Inject `NpgsqlDataSource` directly and issue a raw ADO.NET query (as the existing `AddNpgSql`
  health check does for its own connectivity probe).
- (b) Inject `ApplicationDbContext` (as `DataQualitySchemaHealthCheck` already does) and use EF
  Core 8's `Database.SqlQuery<T>(FormattableString)` for a typed, parameterized, non-tracking query
  against `information_schema.columns`.

**Chosen approach:** (b).

**Rationale:** Every other schema-aware health check and every existing raw-SQL read path in this
codebase (`KnowledgeBaseRepository`, `GridLayoutRepository`, etc.) goes through `ApplicationDbContext`,
not a bare `NpgsqlDataSource`; `DataQualitySchemaHealthCheck` specifically takes `ApplicationDbContext`
in its constructor. Matching that keeps the new check trivially testable against
`UseInMemoryDatabase` for the healthy path (same as `DataQualitySchemaHealthCheckTests`) while still
running real SQL against Npgsql in production — `Database.SqlQuery<T>` degrades gracefully to "no
rows" against the in-memory provider used in tests rather than throwing, so the happy-path test does
not need a real Postgres connection.

#### Decision 3: One health check covering all seven tracked columns, not one check per table

**Options considered:**
- (a) Three separate `IHealthCheck` registrations (`Photos`, `PhotobankIndexRoots`, `PhotoTags`).
- (b) One `PhotobankSchemaHealthCheck` covering all seven columns from the spec's Data Model table,
  reporting every drifted column in its `Unhealthy` result's `data` dictionary (not just the first).

**Chosen approach:** (b).

**Rationale:** These columns are only ever written together, in the same job, in the same
`SaveChangesAsync` calls — they fail or succeed as one operational unit from an on-call perspective.
`DataQualitySchemaHealthCheck` is one check per *table*, not per *column*, for the same reason; this
generalizes that to Photobank's three tables in a single check to avoid three near-identical
registrations for symptoms that all point at the same underlying "was the migration actually applied"
question.

## Implementation Guidance

### Directory / Module Structure

- New: `backend/src/Anela.Heblo.API/HealthChecks/Photobank/PhotobankSchemaHealthCheck.cs`
  (new `Photobank` subfolder under `HealthChecks/`, mirroring the existing `DataQuality/` subfolder
  convention — one folder per health-check family).
- New: `backend/test/Anela.Heblo.Tests/API/HealthChecks/Photobank/PhotobankSchemaHealthCheckTests.cs`
  (mirrors `DataQualitySchemaHealthCheckTests.cs`'s location convention).
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` —
  add `.AddCheck<PhotobankSchemaHealthCheck>(name: "photobank-schema", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready", "db", "schema" })`
  to the existing `healthChecksBuilder` chain in `AddHealthCheckServices`.
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`
  — the one-line `ModifiedAt` fix at line 181 (FR-2).
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs` — add the
  `PhotoTag.CreatedAt` theory case (FR-3).
- New or modify: a `PhotobankIndexJob`-adjacent test file under
  `backend/test/Anela.Heblo.Tests/Features/Photobank/` (check first whether one already exists to
  extend, e.g. `PhotobankIndexJobTests.cs`; create it if not) — the `Kind=Utc` mapping test (FR-3).
- Modify: `docs/development/setup.md`'s "Diagnostic SQL for suspected schema drift" section (FR-4).
- Modify: `memory/gotchas/ef-migration-codebase-drift.md`'s "Known limitation" section (FR-4).

### Interfaces and Contracts

```csharp
public sealed class PhotobankSchemaHealthCheck : IHealthCheck
{
    // Constructor takes ApplicationDbContext, matching DataQualitySchemaHealthCheck.

    // Checked columns (table, column, expected physical type "timestamp without time zone"):
    //   Photos.TakenAt, Photos.IndexedAt, Photos.ModifiedAt, Photos.LastAutoTaggedAt,
    //   PhotobankIndexRoots.CreatedAt, PhotobankIndexRoots.LastIndexedAt,
    //   PhotoTags.CreatedAt

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default);
    // Healthy: "Photobank schema is aligned" when every checked column reports
    //   data_type = 'timestamp without time zone'.
    // Unhealthy: description "Photobank schema drift detected"; `data` contains a
    //   "driftedColumns" entry — a list of { table, column, expectedType, actualType } —
    //   for every column NOT reporting "timestamp without time zone". Never throws on a
    //   drifted column found; only throws (caught, Unhealthy) on connectivity failure.
}
```

`information_schema.columns` query shape (single round trip, not one query per column):

```sql
SELECT table_name, column_name, data_type
FROM information_schema.columns
WHERE table_schema = 'public'
  AND (
    (table_name = 'Photos' AND column_name IN ('TakenAt','IndexedAt','ModifiedAt','LastAutoTaggedAt'))
    OR (table_name = 'PhotobankIndexRoots' AND column_name IN ('CreatedAt','LastIndexedAt'))
    OR (table_name = 'PhotoTags' AND column_name = 'CreatedAt')
  );
```

Postgres reports `timestamp without time zone` / `timestamp with time zone` (not the SQL literal
`timestamp`/`timestamptz` shorthand) in `information_schema.columns.data_type` — the health check's
comparison must use the full `information_schema` string, not the `"timestamp"` string EF's
`GetColumnType()` returns (that's a DDL-type string, a different vocabulary; `PhotoSchemaTests.cs`
compares against `GetColumnType()`, this health check compares against `information_schema`'s
`data_type` — do not conflate the two literal strings when implementing).

### Data Flow

1. Azure App Service / orchestrator polls `GET /health/ready` on its existing cadence.
2. `PhotobankSchemaHealthCheck.CheckHealthAsync` issues one `information_schema.columns` query via
   `ApplicationDbContext.Database.SqlQuery<PhotobankColumnRow>(...)` (or `FromSqlRaw` into an
   anonymous/keyless entity — confirm which `SqlQuery<T>` overload compiles cleanly against this
   repo's EF 8.0.8 pin before finalizing; both are viable, prefer `SqlQuery<T>` for a keyless record
   if it compiles without extra `HasNoKey()` ceremony).
3. Result rows are compared against the expected-column table above; any mismatch → `Unhealthy` with
   the drifted set in `data`.
4. Independently, every future `PhotobankIndexJob` run gets FR-2's defensive `ModifiedAt` fix,
   regardless of what the health check reports — the two changes are not sequenced against each
   other; FR-2 is unconditionally safe to ship even if the health check later shows the drift was
   never about `ModifiedAt` at all.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `information_schema.columns` query syntax doesn't compile cleanly with EF Core 8.0.8's `SqlQuery<T>` against a type with no corresponding entity (needs a keyless/unmapped result type) | Medium | Verify with a local build before considering FR-1 done; fall back to `FromSqlRaw` against a minimal unmapped record with `[Keyless]`-equivalent configuration, or a raw `NpgsqlCommand` via `NpgsqlDataSource` (Decision 2's option (a)) if `SqlQuery<T>` proves awkward — either satisfies FR-1's acceptance criteria. |
| Health check turns `Unhealthy` in a legitimately-fine environment because `information_schema` behaves differently under a non-Postgres provider (e.g. the in-memory provider used by tests, or a future non-relational provider) | Low | Guard with the same `db.Database.IsRelational()` pattern `MigrateDatabaseAsync` already uses; treat a non-relational provider as `Healthy`/skip, not `Unhealthy` — mirror `DataQualitySchemaHealthCheck`'s existing generic-`Exception` → `Unhealthy` catch-all only for genuine connectivity failures, not for "provider doesn't support this query." |
| FR-2's fix doesn't actually change today's exception rate (because the real drift is elsewhere, per Background) | Low | Explicitly acceptable and already scoped for in the spec — FR-2 is a defensive closure of a legitimate gap regardless of whether it's the current cause; FR-1 is the mechanism that will actually confirm or rule out the drift hypothesis once deployed. Do not treat FR-2 alone as "the fix" in the PR description. |
| New health check tagged `ready` could take an unhealthy Photobank-drift instance out of rotation for a condition that doesn't affect any other feature (Photobank is a narrow, non-critical-path feature) | Medium | Match `DataQualitySchemaHealthCheck`'s own precedent exactly (same `ready` tag, same `HealthStatus.Unhealthy` failure status) rather than inventing a different severity policy here — this is a repo-wide convention question already answered by the existing check, not something this feature should relitigate. |

## Specification Amendments

None — the spec's FR/NFR set is directly implementable as scoped. One clarification worth carrying
into the task plan: FR-1's acceptance criteria describes the `data` shape as flat
(`table`/`column`/`expectedType`/`actualType`), but Decision 3 covers multiple columns in one check;
the planner should design the `Unhealthy` result's `data` dictionary as a single `driftedColumns`
list entry (see Interfaces and Contracts above) rather than trying to force multiple drifted columns
into the flat single-column shape the spec's API/Interface Design example JSON shows for a
single-column illustration — that example was illustrative of one drifted column, not a contract for
exactly one.

## Prerequisites

None beyond what already exists in this branch (EF Core 8.0.8, `ApplicationDbContext`,
`AddHealthCheckServices`, `PhotoSchemaTests.cs`'s existing pattern). No new infrastructure, no new
migration needed — this feature deliberately does not touch the schema itself (see spec's NFR-2 /
Out of Scope).
