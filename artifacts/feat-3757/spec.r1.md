# Specification: Photobank nightly index job — close the DateTime Kind=Unspecified regression after PR #3743

## Summary

The nightly `PhotobankIndexJob` still throws `System.ArgumentException: Cannot write DateTime with
Kind=Unspecified to PostgreSQL type 'timestamp with time zone', only UTC is supported` from
`PhotobankPhotoRepository`/`PhotobankRootRepository`'s shared `SaveChangesAsync`, at an unchanged
rate of 3/day, even after PR #3743 (closed #3444) shipped a migration that was believed to fix the
last offending column. Code inspection (this spec) confirms every Photobank `DateTime` column in
the current codebase is already mapped to `timestamp` (without time zone) via `AsUtcTimestamp()`
and that all matching EF migrations exist in the repository — so the residual failure is either (a)
schema drift between the code's migrations and the physical production database (this repo's
migrations are applied automatically at app startup via `MigrateDatabaseAsync()`, but the sibling
incident documented in `memory/gotchas/ef-migration-codebase-drift.md` shows this can still fail
silently), or (b) a not-yet-covered write path. This spec closes both possibilities: it adds an
operational signal for (a), a defensive fix + regression test for the one remaining
externally-sourced `DateTime` write path for (b), and extends this repo's existing schema-drift
diagnostic playbook to cover Photobank.

## Background

- `PhotobankIndexJob.IndexRootAsync` (`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`)
  runs nightly (cron `0 3 * * *`) and calls `SaveChangesAsync` on three Photobank repositories, all
  of which share one scoped `ApplicationDbContext`.
- The global `ApplicationDbContext` convention forces every `DateTime` value to `Kind=Unspecified`
  before it reaches Npgsql (documented in `PhotoSchemaTests.cs`'s class comment). PostgreSQL's
  `timestamp without time zone` columns accept `Kind=Unspecified` values; `timestamp with time
  zone` columns reject them. So this exception fires if, and only if, the physical column type for
  whichever `DateTime` property was touched is still `timestamptz` — regardless of what Kind the
  in-memory value originally had before the DbContext convention normalized it away.
- History of fixes at this same call site:
  - **#3330** (migration `20260624115315_AlignPhotoTimestampsWithoutTimeZone`): converted
    `Photos.TakenAt` and `Photos.LastAutoTaggedAt` from `timestamptz` → `timestamp`. Dropped the
    exception rate from 14/day to 3/day.
  - **#3444 / PR #3743** (migration `20260724120000_AlignPhotobankIndexRootTimestampWithoutTimeZone`):
    converted `PhotobankIndexRoots.LastIndexedAt` from `timestamptz` → `timestamp`, diagnosed as
    "the only Photobank DateTime column missing `.AsUtcTimestamp()`". Merged 2026-07-25. The very
    next nightly run (2026-07-26) failed identically, and every run since (through 2026-07-27) has
    too — 3/day, unchanged.
  - Current code state (verified by reading every Photobank entity/configuration in this branch):
    **all** `DateTime`/`DateTime?` properties across `Photo`, `PhotobankIndexRoot`, and `PhotoTag`
    already call `.AsUtcTimestamp()` (i.e. `HasColumnType("timestamp")`), and a matching migration
    exists in the repo for each one that wasn't `timestamp` from its original `CREATE TABLE`. There
    is no remaining Photobank entity property in the codebase lacking this mapping.
- This means PR #3743's fix is either not yet reflected in the production schema (migration/deploy
  drift — the same failure class already documented for a different table in
  `memory/gotchas/ef-migration-codebase-drift.md`, whose "Known limitation" section explicitly
  flags that its safeguard "does NOT cover the other tables ... broader coverage is tracked as a
  follow-up"), or the exception's actual failing column was never `LastIndexedAt` to begin with and
  PR #3743's diagnosis was incomplete. Both are now addressed by this spec rather than guessed at.
- Prior related regression in this codebase for the identical exception message/pattern:
  `memory/gotchas/smartsupp-staged-contact-datetime-kind.md` — root-caused to an externally-sourced
  DateTime never being stamped `Kind=Utc` before being written through a code path with inconsistent
  column-type assumptions. `Photo.ModifiedAt = item.LastModifiedAt ?? DateTime.UtcNow;`
  (`PhotobankIndexJob.cs:181`) is the *only* Photobank DateTime write sourced from something other
  than `DateTime.UtcNow` — `item.LastModifiedAt` is `GraphDeltaItem.LastModifiedDateTime`, deserialized
  from the Microsoft Graph delta API's `lastModifiedDateTime` field via a plain `System.Text.Json`
  `DateTime?` converter with no explicit UTC handling. Even though the DbContext convention strips
  Kind before every write (so this alone cannot explain today's exception once the column is
  `timestamp`), it is the one place in this job where an externally-controlled value flows into a
  tracked entity without going through the app's own UTC-generation pattern, and following the
  Smartsupp precedent, it should be defensively normalized rather than left to depend on an external
  API's serialization format never changing.

## Functional Requirements

### FR-1: Photobank schema-drift health check
Add a health check that detects, at runtime, whether any Photobank `DateTime` column's *physical*
PostgreSQL type has drifted from what the EF model declares (`timestamp` without time zone) —
converting a silent, repeating background-job exception into an observable readiness signal, the
same remediation pattern already established by `DataQualitySchemaHealthCheck` for the `DqtRuns`
drift incident.

**Acceptance criteria:**
- A new `PhotobankSchemaHealthCheck` (or equivalent name) queries `information_schema.columns` (or
  equivalent EF/ADO.NET read) for `public."Photos".("TakenAt","IndexedAt","ModifiedAt","LastAutoTaggedAt")`,
  `public."PhotobankIndexRoots".("CreatedAt","LastIndexedAt")`, and `public."PhotoTags"."CreatedAt"`.
- If every checked column's PostgreSQL type is `timestamp without time zone`, the check returns
  `Healthy`.
- If any checked column is `timestamp with time zone`, the check returns `Unhealthy` with structured
  `data` naming the offending table/column(s) (mirroring `DataQualitySchemaHealthCheck`'s
  `entity`/`expectedTable`/`schema` shape), so it's immediately diagnosable from `/health/ready`
  output without a manual SQL session.
- The check is read-only (`information_schema` query only) — it must never attempt a write, since a
  write is what already reliably reproduces the production exception.
- Registered in `AddHealthCheckServices` (`ServiceCollectionExtensions.cs`) under `/health/ready`
  with tags `ready`, `db`, `schema` — consistent with `data-quality-schema`'s registration.

### FR-2: Defensively normalize `Photo.ModifiedAt`'s DateTime Kind at the Graph API boundary
`PhotobankIndexJob.UpsertPhotoBatchAsync` must stamp `photo.ModifiedAt` as `DateTimeKind.Utc`
(relabel, not shift — Microsoft Graph's `lastModifiedDateTime` is always UTC) instead of assigning
the deserialized `item.LastModifiedAt` value as-is, whatever `Kind` `System.Text.Json` gave it.

**Acceptance criteria:**
- `photo.ModifiedAt = item.LastModifiedAt.HasValue ? DateTime.SpecifyKind(item.LastModifiedAt.Value, DateTimeKind.Utc) : DateTime.UtcNow;`
  (or equivalent) replaces the current `photo.ModifiedAt = item.LastModifiedAt ?? DateTime.UtcNow;`
  at `PhotobankIndexJob.cs:181`.
- `DateTime.SpecifyKind` is used, not any form of `.ToUniversalTime()` — the Graph value is already
  the correct instant; only its `Kind` label needs correcting, exactly as the Smartsupp precedent
  documents ("relabel, not shift").

### FR-3: Close the schema regression-test gap
Extend the existing `PhotoSchemaTests`-style regression guard to cover `PhotoTag.CreatedAt` (the one
Photobank `DateTime` column with no existing schema-mapping test), and add a focused unit test for
FR-2's mapping fix.

**Acceptance criteria:**
- A new theory case (or new test) asserts `PhotoTag.CreatedAt`'s `GetColumnType()` is `"timestamp"`,
  matching the pattern already used for `Photo` and `PhotobankIndexRoot` in
  `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs`.
- A new test (e.g. in a `PhotobankIndexJobTests` or `PhotobankGraphServiceTests` fixture) asserts
  that after `UpsertPhotoBatchAsync` processes a delta item whose `LastModifiedAt` has
  `Kind=Unspecified` (and, separately, `Kind=Local`), the resulting `Photo.ModifiedAt.Kind` is
  `DateTimeKind.Utc`.

### FR-4: Extend the schema-drift diagnostic runbook to Photobank
Close the explicit follow-up gap noted in `memory/gotchas/ef-migration-codebase-drift.md`'s "Known
limitation of the safeguard" section ("does NOT cover the other tables ... broader coverage is
tracked as a follow-up") for the Photobank tables covered by this spec.

**Acceptance criteria:**
- `docs/development/setup.md`'s "Diagnostic SQL for suspected schema drift" section gains a
  Photobank-specific worked example (migration-fragment values `AlignPhotoTimestampsWithoutTimeZone`
  / `AlignPhotobankIndexRootTimestampWithoutTimeZone`, and a column-type variant of the physical
  check — `information_schema.columns` filtered to the tables/columns in FR-1 — since this incident
  is a column-type drift, not a table-rename drift, unlike the documented example).
- `memory/gotchas/ef-migration-codebase-drift.md`'s "Known limitation" section is updated to note
  Photobank is now covered by `PhotobankSchemaHealthCheck` (FR-1), so a future reader doesn't have to
  re-derive that this table class is already handled.

## Non-Functional Requirements

### NFR-1: Performance
`PhotobankSchemaHealthCheck` must complete in well under the existing `/health/ready` scrape
interval and must not open a dedicated connection outside the shared pool — reuse `ApplicationDbContext`
/ the existing `NpgsqlDataSource`, consistent with how `DataQualitySchemaHealthCheck` and the
`AddNpgSql` check already do.

### NFR-2: Safety
No production write is performed by any new diagnostic code path (health check or documentation).
Nothing in this spec attempts to apply a migration or alter a column from application code — this
repo's own migrations already contain the correct `ALTER COLUMN ... TYPE timestamp` statements;
applying a pending migration to production is an existing, separate operational action (automatic
via `MigrateDatabaseAsync()` on next successful deploy, or manual per this repo's standard
migration procedure), not something this change should perform out-of-band.

## Data Model

No new entities or columns. Existing columns referenced by this spec:

| Table | Column | Expected type (per current model) | Migration that set it |
|---|---|---|---|
| `Photos` | `TakenAt` | `timestamp` | `20260624115315_AlignPhotoTimestampsWithoutTimeZone` |
| `Photos` | `IndexedAt` | `timestamp` | `20260424122851_AddPhotobankTables` (original) |
| `Photos` | `ModifiedAt` | `timestamp` | `20260424122851_AddPhotobankTables` (original) |
| `Photos` | `LastAutoTaggedAt` | `timestamp` | `20260624115315_AlignPhotoTimestampsWithoutTimeZone` |
| `PhotobankIndexRoots` | `CreatedAt` | `timestamp` | `20260424122851_AddPhotobankTables` (original) |
| `PhotobankIndexRoots` | `LastIndexedAt` | `timestamp` | `20260724120000_AlignPhotobankIndexRootTimestampWithoutTimeZone` |
| `PhotoTags` | `CreatedAt` | `timestamp` | `20260424122851_AddPhotobankTables` (original) |

## API / Interface Design

New health check exposed at the existing `/health/ready` endpoint (no new endpoint). On failure, its
JSON entry includes (mirroring `DataQualitySchemaHealthCheck`'s shape):

```json
{
  "status": "Unhealthy",
  "description": "Photobank schema drift detected",
  "data": {
    "table": "PhotobankIndexRoots",
    "column": "LastIndexedAt",
    "expectedType": "timestamp",
    "actualType": "timestamp with time zone"
  }
}
```

## Dependencies

- Existing `ApplicationDbContext` / `NpgsqlDataSource` DI registrations.
- Existing `AddHealthCheckServices` extension point (`ServiceCollectionExtensions.cs`).
- No new NuGet packages or external services.

## Out of Scope

- Actually applying any pending migration to the production database — that is an operational
  action outside this repository's code, per NFR-2. This spec's FR-1 makes the drift observable;
  it does not remediate it.
- `Photo.TakenAt` behavior changes: no code in the current codebase ever sets `TakenAt` (verified by
  a full-repo search) — it is always `null` on write, so it cannot be a source of today's exception
  and is included in FR-1/FR-3 only for completeness of the schema-drift/regression coverage.
- Re-diagnosing whether PR #3743's own root-cause analysis was correct beyond what's stated in
  Background — this spec treats "the column-type mapping is now correct in code for every Photobank
  DateTime property" as verified fact (confirmed by direct code inspection) and focuses on making
  the remaining unknown (physical DB state) observable, per FR-1.

## Open Questions

None. Where the actual production database's current column types are unknown from this sandbox
(no production DB access here), FR-1's health check is designed specifically to make that state
observable after deploy rather than leaving it as a blocking question — this is a deliberate design
choice given the constraint, not an oversight.

## Status: COMPLETE
