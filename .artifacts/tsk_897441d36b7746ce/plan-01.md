# Plan: Fix residual `PhotobankRepository.SaveChangesAsync` DateTime Kind=Unspecified exception

## Summary

PR #3330 fixed two `Photo` columns (`TakenAt`, `LastAutoTaggedAt`) that were missing the `AsUtcTimestamp()`
mapping, which dropped the exception rate from 14/day to 3/day. The residual ~3/day is a **different
entity in the same job**: `PhotobankIndexRoot.LastIndexedAt` is still mapped to PostgreSQL's default
`timestamp with time zone` column type instead of `timestamp` (without time zone). Root cause confirmed
by direct code inspection — this is not a hypothesis needing further drill-down.

## Context

`ApplicationDbContext.OnModelCreating` installs a global `ValueConverter` on every `DateTime`/`DateTime?`
property that forces the CLR value to `DateTimeKind.Unspecified` before writing and reinterprets it as
`DateTimeKind.Utc` on read (`backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs:186-208`). This
convention only works if the underlying column is `timestamp` (without time zone) — Npgsql rejects
`Kind=Unspecified` writes to `timestamp with time zone` columns with exactly the reported
`ArgumentException`. Each entity's `IEntityTypeConfiguration` must therefore explicitly opt every
`DateTime` column into `timestamp` via the `AsUtcTimestamp()` extension
(`backend/src/Anela.Heblo.Persistence/Extensions/DateTimeConfigurationExtensions.cs`); any column left
unconfigured silently defaults to `timestamptz` and breaks at write time.

PR #3330 audited and fixed every `DateTime` column on `Photo` but did not audit sibling Photobank entities
touched by the same nightly job. `PhotobankIndexJob.IndexRootAsync`
(`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs:96-97`)
sets `root.LastIndexedAt = DateTime.UtcNow;` and calls `_repo.SaveChangesAsync(ct)` once per active root,
every night. `PhotobankIndexRootConfiguration.Configure`
(`backend/src/Anela.Heblo.Persistence/Photobank/PhotobankIndexRootConfiguration.cs:21`) has:

```csharp
builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();   // correct
...
builder.Property(x => x.LastIndexedAt);                             // MISSING AsUtcTimestamp()
```

`LastIndexedAt` is the only remaining Photobank `DateTime` column not mapped to `timestamp`. This exactly
matches the signal: one exception per active root per nightly run (~3 active roots, matching the steady
3/day rate observed every day since PR #3330 merged), fired at the very end of `IndexRootAsync` after all
photo/tag work for that root already succeeded.

I audited every other Photobank `DateTime` property and confirmed all are already correctly configured:
`Photo.IndexedAt`, `Photo.ModifiedAt`, `Photo.TakenAt`, `Photo.LastAutoTaggedAt`, `PhotoTag.CreatedAt`,
`PhotobankIndexRoot.CreatedAt`. `TagRule` and `Tag` have no `DateTime` properties. No other entity is a
candidate.

## Functional requirements

**FR-1 — Map `PhotobankIndexRoot.LastIndexedAt` to `timestamp` (without time zone).**
In `PhotobankIndexRootConfiguration.cs`, change:
```csharp
builder.Property(x => x.LastIndexedAt);
```
to:
```csharp
builder.Property(x => x.LastIndexedAt).AsUtcTimestamp();
```
Acceptance: EF model metadata reports `GetColumnType() == "timestamp"` for `PhotobankIndexRoot.LastIndexedAt`.

**FR-2 — Add an EF Core migration altering the existing `LastIndexedAt` column, UTC-preserving.**
Follow the exact pattern used in `20260624115315_AlignPhotoTimestampsWithoutTimeZone.cs` (PR #3330):
```sql
ALTER TABLE public."PhotobankIndexRoots"
ALTER COLUMN "LastIndexedAt" TYPE timestamp USING "LastIndexedAt" AT TIME ZONE 'UTC';
```
with a `Down()` that reverts to `timestamp with time zone` using the same `AT TIME ZONE 'UTC'` conversion.
Acceptance: migration applies cleanly against a Postgres instance with existing `PhotobankIndexRoots` rows
(including `NULL` `LastIndexedAt` values, since the column is nullable) without data loss or timezone
shift; `dotnet ef migrations script` reviewed for correctness before merge. Per project rules, DB
migrations are **not** run automatically at deploy — the migration is checked in and must be applied
manually against staging/production after merge (call this out explicitly in the PR description).

**FR-3 — Extend the schema regression test to cover `PhotobankIndexRoot`.**
Existing `PhotoSchemaTests.cs` (`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs`)
only guards `Photo` columns. Add an analogous theory covering `PhotobankIndexRoot.CreatedAt` and
`PhotobankIndexRoot.LastIndexedAt` (both should assert `GetColumnType() == "timestamp"`), so any future
Photobank entity/column added without `AsUtcTimestamp()` fails a unit test instead of surfacing as a
nightly-job exception. Prefer widening the existing test class/theory data rather than duplicating the
`NewNpgsqlContext()` helper — keep it in the same file or a small sibling file, whichever reads more
naturally against the existing structure.
Acceptance: new test(s) fail against the current code (missing `AsUtcTimestamp()` on `LastIndexedAt`) and
pass after FR-1.

**FR-4 — Verify no regression in `PhotobankIndexJob` / repository tests.**
Run the existing Photobank test suite (`backend/test/Anela.Heblo.Tests/Features/Photobank/**`) plus any
`PhotobankIndexJob` batching tests (added in #3692/#3697) to confirm the fix doesn't disturb the
save-batching behavior.
Acceptance: full existing Photobank test suite green.

## Non-functional requirements

- **No behavior change for callers.** `PhotobankIndexRoot.LastIndexedAt` continues to hold UTC `DateTime`
  values in the CLR; only the storage column type and physical bytes change. `PhotoLocator`/API-facing
  DTOs are unaffected (`LastIndexedAt` is not currently exposed via `PhotoLocator`).
- **Migration safety.** The `ALTER COLUMN ... USING ... AT TIME ZONE 'UTC'` conversion must not shift
  existing timestamps — same approach already validated in production by PR #3330's migration.
- **Telemetry.** After deployment + manual migration apply, confirm via the same App Insights query in the
  signal (`appinsights-query.sh --timespan P7D ... problemId has "DateTimeConverterResolver"`) that the
  exception count drops to zero and stays there for at least 3 consecutive nightly runs.

## Data model

Single column type change, no new entities:

| Entity | Column | Before | After |
|---|---|---|---|
| `PhotobankIndexRoot` | `LastIndexedAt` | `timestamp with time zone` (implicit default) | `timestamp` (explicit, UTC-naive) |

No changes to `Photo`, `PhotoTag`, `Tag`, `TagRule`.

## Interfaces

None — this is a pure persistence-layer/EF configuration + migration fix. No API contract, DTO, or
frontend changes. `IPhotobankRepository`/`PhotobankRepository` public surface is unchanged.

## Dependencies and scope

**In scope:**
- `PhotobankIndexRootConfiguration.cs` property mapping fix
- One new EF Core migration (Up/Down + Designer + snapshot update)
- Schema regression test extension in `PhotoSchemaTests.cs` (or sibling)

**Out of scope:**
- Any other Photobank entity/column — audited and confirmed clean (see Context).
- Re-litigating the value-converter/global-convention design itself; it's an established, working pattern
  used across the codebase (grep shows the same `Kind`-handling approach reused in Bank, Packaging, Grid
  Layouts, Smartsupp repositories) — not something to change here.
- Manual production migration execution — flag it in the PR/handoff since deployment doesn't run
  migrations automatically (project fact), but actually running it against staging/prod is a follow-up
  operational step, not part of this code change.

## Rough plan

1. Fix `PhotobankIndexRootConfiguration.cs`: add `.AsUtcTimestamp()` to the `LastIndexedAt` property
   mapping.
2. Generate EF Core migration (`dotnet ef migrations add AlignPhotobankIndexRootTimestampWithoutTimeZone`
   from `backend/src/Anela.Heblo.Persistence`), then replace the generated `ALTER COLUMN` with the explicit
   `USING ... AT TIME ZONE 'UTC'` SQL (mirroring #3330) in both `Up()` and `Down()` to guarantee no data
   shift; verify the Designer/snapshot diff only touches `LastIndexedAt`'s column type.
3. Extend `PhotoSchemaTests.cs` with a `PhotobankIndexRoot` theory (or new adjacent test) asserting
   `CreatedAt` and `LastIndexedAt` both map to `"timestamp"`.
4. Run `dotnet build` + `dotnet format` (per repo validation rules) and the full Photobank test suite;
   confirm the new test fails pre-fix and passes post-fix (quick revert/re-apply check).
5. In the PR description, note that the migration must be applied manually against staging/production
   after merge (migrations are not automated), and link back to this telemetry signal / PR #3330 for
   context.
6. Post-deploy: re-run the App Insights query from the signal for a few days to confirm the exception
   count reaches and stays at zero.

## Open questions

- **None blocking.** The root cause is fully confirmed by static inspection (missing `AsUtcTimestamp()`
  call is directly visible, and it's the only remaining unconfigured `DateTime` column in the Photobank
  module reachable from `PhotobankRepository.SaveChangesAsync`). No further telemetry drill-down is needed
  before implementing.
- One judgment call for the next step (design/dev): whether to place the new regression test as an
  additional `[Theory]`/`[InlineData]` block in the existing `PhotoSchemaTests` class (simplest, matches
  the "surgical change" project rule) or split into a new `PhotobankIndexRootSchemaTests.cs` for clearer
  entity-per-file naming. Default: extend the existing file — lower churn, same guard pattern, and the
  class docstring can be broadened to "Photobank" from "Photo" in one line.
