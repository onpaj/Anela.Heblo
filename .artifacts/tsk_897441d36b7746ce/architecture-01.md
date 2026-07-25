# Architecture Assessment: Fix residual `PhotobankRepository.SaveChangesAsync` DateTime Kind=Unspecified exception

## Verdict

The plan and design from the previous steps are correct and verified against the current code —
I re-read every file they cite (`PhotobankIndexRootConfiguration.cs`, `DateTimeConfigurationExtensions.cs`,
`ApplicationDbContext.cs:186-210`, the PR #3330 migration, `PhotoSchemaTests.cs`, the model snapshot, and
`PhotobankIndexJob.cs`). Nothing in them is stale or speculative. This assessment confirms the approach,
adds one consequence of the bug that raises its priority slightly, and gives the implementer a precise
sequencing and acceptance checklist. **Proceed with the one-line config fix + mirrored migration + test
extension exactly as designed.** There is no architectural decision left open — this is a convention
compliance gap, not a design question.

## Alignment with existing patterns

The codebase has a single, established convention for `DateTime` persistence, and it is a global
DbContext-level ValueConverter (`ApplicationDbContext.cs:186-210`) that unconditionally rewrites every
`DateTime`/`DateTime?` property to `Kind=Unspecified` on write and reinterprets as `Kind=Utc` on read —
applied to **every entity in the model**, not just Photobank. This convention has one hard precondition:
the physical column must be `timestamp` (without time zone). Npgsql enforces this at the driver level and
throws exactly the reported `ArgumentException` when the precondition is violated.

The opt-in mechanism is `AsUtcTimestamp()` (`Extensions/DateTimeConfigurationExtensions.cs`), a two-line
extension (`DateTime` and `DateTime?` overloads) that does nothing but `HasColumnType("timestamp")`. Grep
confirms this same extension is already used across Bank, Packaging, Grid Layouts, and Smartsupp
repositories — it is the repo-wide idiom for this convention, not something introduced or special-cased
for Photobank. **Every** `IEntityTypeConfiguration<T>` in the codebase is expected to call it on every
`DateTime` property; omitting it is not a valid alternative mapping, it's a bug, full stop.

`PhotobankIndexRootConfiguration.cs:21` confirmed as read:
```csharp
builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();   // correct
...
builder.Property(x => x.LastIndexedAt);                             // missing AsUtcTimestamp()
```
This is the only `DateTime` property in the entire Photobank module (`Photo`, `PhotoTag`, `Tag`, `TagRule`,
`PhotobankIndexRoot`) not opted in — verified by inspecting each configuration file, not inferred. The
model snapshot (`ApplicationDbContextModelSnapshot.cs:3174`) independently confirms `LastIndexedAt` has no
`HasColumnType` override, i.e. it resolved to Npgsql's implicit default (`timestamp with time zone`).

PR #3330's migration (`20260624115315_AlignPhotoTimestampsWithoutTimeZone.cs`) is the direct precedent to
mirror: raw `migrationBuilder.Sql(...)` with `ALTER COLUMN ... TYPE timestamp USING col AT TIME ZONE 'UTC'`
rather than EF's generated `AlterColumn<>` — because only the raw SQL form lets you control the cast
semantics (reinterpret existing `timestamptz` values as UTC-naive, not convert them through the session
timezone). The proposed migration in design-01.md is a mechanical copy of this pattern onto one table/
column. No new migration pattern is being introduced.

`PhotoSchemaTests.cs` establishes the regression-guard pattern: a schema-only test (Npgsql provider,
no open connection, inspects `db.Model...GetColumnType()`) that fails at build time if a `DateTime`
column drifts from the convention. Extending it to `PhotobankIndexRoot` is the correct and only
consistent way to close this gap — a fresh test class would duplicate `NewNpgsqlContext()` for no
benefit.

## Proposed architecture

No new components. Three existing files change, each in place:

```
PhotobankIndexRootConfiguration.Configure()   [1-line fix: .AsUtcTimestamp()]
        │
        ▼ (model building, applied automatically via ApplyConfigurationsFromAssembly)
ApplicationDbContext.OnModelCreating()         [unchanged — global converter already correct]
        │
        ▼ (physical column must match convention)
Migration: ALTER COLUMN LastIndexedAt TYPE timestamp USING ... AT TIME ZONE 'UTC'
        │
        ▼ (guarded against future regression by)
PhotoSchemaTests.cs  →  extended with PhotobankIndexRoot theory
```

**Options considered:** none seriously — this is a one-fact bug (one property missing one method call)
with a single established remediation pattern already proven in production by PR #3330. The only real
decision point is test placement (extend `PhotoSchemaTests.cs` vs. a new `PhotobankIndexRootSchemaTests.cs`
file), which the plan already resolved in favor of extending the existing file. I agree with that call:
the class's purpose ("guard the DateTime-Kind convention across Photobank schema") is not `Photo`-specific
by nature, only by history, and a second near-identical `NewNpgsqlContext()` helper would be pure
duplication for zero isolation benefit — these are unit tests with no shared mutable state.

**Rejected alternative (worth naming so it isn't reconsidered later):** widening
`DateTimeConfigurationExtensions` or `OnModelCreating` to auto-apply `HasColumnType("timestamp")` to every
`DateTime` property globally, removing the need for per-entity opt-in entirely. This would structurally
prevent this entire class of bug. I am **not** recommending it as part of this fix — it's a bigger,
cross-cutting change touching every entity's migration history (every existing `timestamptz` column not
yet migrated would suddenly need a matching migration, and any entity relying on the current default
would break loudly at migration-generation time). It's a legitimate future hardening idea, but it's out of
scope for a targeted production-bug fix and shouldn't block or scope-creep this PR. If this class of bug
recurs a third time, that's the trigger to revisit it — flagging it here so it's not lost, not to route it
into this task.

## Implementation guidance

Follow the plan's rough-plan sequencing as-is; the additions below are precision, not change:

1. **Config fix** — `PhotobankIndexRootConfiguration.cs:21`, append `.AsUtcTimestamp()` to the existing
   `builder.Property(x => x.LastIndexedAt);` statement. Single line, no other edits to this file.

2. **Migration** — generate via `dotnet ef migrations add AlignPhotobankIndexRootTimestampWithoutTimeZone`
   from `backend/src/Anela.Heblo.Persistence` (so `.Designer.cs` and the model snapshot regenerate
   correctly), then replace the generated `Up()`/`Down()` bodies with the raw SQL shown in design-01.md,
   copied verbatim in structure from PR #3330's migration (only the table/column names differ:
   `"PhotobankIndexRoots"."LastIndexedAt"` instead of `"Photos"."TakenAt"`/`"LastAutoTaggedAt"`). Diff the
   resulting `ApplicationDbContextModelSnapshot.cs` — it must touch only the `LastIndexedAt` property's
   `HasColumnType` entry (line ~3174) and the migration's own `ProductVersion`/timestamp bookkeeping;
   anything else in the snapshot diff signals a stale/dirty model and should stop the PR.

3. **Test** — add the `[Theory]`/`[InlineData]` block from design-01.md to `PhotoSchemaTests.cs`, covering
   both `PhotobankIndexRoot.CreatedAt` (already correct — proves the test methodology itself is sound) and
   `PhotobankIndexRoot.LastIndexedAt` (currently broken — proves the test catches the real bug). Confirm
   by running the test **before** step 1's edit lands (or via a throwaway local revert) that the
   `LastIndexedAt` case fails, then confirm it passes after.

4. **No other files change.** `IPhotobankRepository`, `PhotobankRepository`, `PhotobankIndexJob`, and any
   DTO/controller surface are untouched — this is purely persistence-configuration + migration + test.

## Risks and mitigations

- **Migration data safety.** `ALTER COLUMN ... TYPE timestamp USING col AT TIME ZONE 'UTC'` is the exact,
  already-production-validated pattern from PR #3330 — low risk. Mitigation: review `dotnet ef migrations
  script` output before merge (called out in the plan); confirm `NULL` rows pass through unchanged (the
  column is nullable and this is the same nullability shape PR #3330 already handled for `TakenAt`).

- **Manual migration application.** Per project fact, migrations are not run automatically at deploy.
  Mitigation: state explicitly in the PR description that staging/production require a manual
  `dotnet ef database update` (or equivalent) after merge — this must not be silently assumed to happen.

- **Underestimated blast radius of the current bug (raises priority, doesn't change the fix).** Verified
  by reading `PhotobankIndexJob.cs:96-98`: `root.DeltaLink` and `root.LastIndexedAt` are set together and
  persisted in the *same* `SaveChangesAsync` call that currently throws. Because the exception is caught
  and logged at the job level (`catch (Exception ex) { _logger.LogError(...) }`) rather than propagated,
  the job doesn't crash — but `root.DeltaLink` **also never gets persisted**, every single night, for
  every affected root, since before PR #3330. This means each affected root has been silently re-fetching
  and re-processing its delta from a stale `DeltaLink` on every nightly run since the bug's introduction
  (individual photo upserts succeed and commit per-item at line 138, so this doesn't lose photo data or
  duplicate visible photos — it wastes Graph API calls and job time re-processing an ever-growing backlog
  window every night, and `LastIndexedAt`/staleness reporting for these roots has been wrong since before
  #3330). This is a functional correctness issue beyond exception noise and is worth a one-line callout in
  the PR description; no code changes beyond the plan are needed since fixing the write also fixes the
  delta-link persistence as a side effect.

- **Test placement drift.** Extending `PhotoSchemaTests.cs` to cover a non-`Photo` entity slightly
  overloads the file's name. Mitigation: none needed for this PR (low-cost, matches project's "surgical
  changes" rule) — but if a third Photobank entity ever needs this guard, that's the trigger to rename the
  file/class to something like `PhotobankSchemaTests`, not before.

## Prerequisites before implementation begins

None outstanding. Root cause is confirmed by direct code and model-snapshot inspection (not inferred from
telemetry alone), the remediation pattern is already proven in production by PR #3330, and the test
methodology already exists and needs only extension. Implementation can start immediately following the
plan's rough-plan steps 1–6, using the precision in this document's "Implementation guidance" section.
