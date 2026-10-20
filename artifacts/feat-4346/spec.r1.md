# Specification: Fix CS1503 build break in RecurringJobSeeder

## Summary
`RecurringJobSeeder.SeedDefaultConfigurationsAsync` passes the whole `existing` collection to `HasSeededFieldsChanged`, which takes a single `RecurringJobConfiguration`. This is a compile error (CS1503) that breaks the solution build on `main`. The fix is to pass `existingConfig`, the per-job entry already resolved from `existingByName`.

## Background
PR #4324 (issue #4318) replaced per-job lookups with one batch `GetAllAsync` read and a name-keyed dictionary. The `out var existingConfig` variable was introduced, but the `HasSeededFieldsChanged` call site at line 51 still references `existing` (the list). All branches cut from `main` fail `dotnet build`/`dotnet test`.

## Functional Requirements

### FR-1: Correct argument at HasSeededFieldsChanged call site
In `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` (~line 51) change `HasSeededFieldsChanged(existing, config)` to `HasSeededFieldsChanged(existingConfig, config)`.
**Acceptance criteria:**
- `dotnet build` of the solution succeeds with no CS1503.
- Seeder semantics are unchanged: update only when DisplayName, Description or TimeZoneId differ; CronExpression and IsEnabled preserved.
- All tests in `RecurringJobSeederTests` pass (including the "nothing changed preserves LastModifiedAt/By" and "resyncs TimeZoneId" cases).

## Non-Functional Requirements

### NFR-1: Performance
No change; the single batch read introduced by #4318 stays.

### NFR-2: Security
Not applicable.

## Data Model
No change.

## API / Interface Design
No change. `HasSeededFieldsChanged` signature is untouched.

## Dependencies
None.

## Out of Scope
- Any refactor of the seeder or its tests.
- Adding new tests (existing suite already covers the branch; it failed only because the project would not compile).
- CI changes to catch build breaks before merge.

## Open Questions
None.

## Status: COMPLETE
