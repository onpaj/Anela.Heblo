# Implementation: photobank-drift-runbook-docs

## What was implemented

Extended the schema-drift diagnostic runbook to cover the Photobank column-type drift variant of
this failure class (#3757), distinct from the existing table-existence (`DqtRuns` rename) variant
already documented.

## Files created/modified

- `docs/development/setup.md` — appended a new "Photobank column-type drift (distinct from the
  table-rename case above)" subsection immediately after the existing "Diagnostic SQL for suspected
  schema drift" section. Includes a migration-history SQL check (for
  `AlignPhotoTimestampsWithoutTimeZone` / `AlignPhotobankIndexRootTimestampWithoutTimeZone`), a
  physical column-type check against `Photos`, `PhotobankIndexRoots`, and `PhotoTags`, and
  interpretation guidance pointing at `PhotobankSchemaHealthCheck` (`GET /health/ready`, tag
  `photobank-schema`) as the preferred first check.
- `memory/gotchas/ef-migration-codebase-drift.md` — appended a sentence to the existing "Known
  limitation of the safeguard" section noting that Photobank's `DateTime` columns are now covered by
  the sibling `PhotobankSchemaHealthCheck` safeguard, while other tables remain uncovered. Existing
  text was not removed or rewritten.

## Tests

None — documentation-only change.

## How to verify

- Read `docs/development/setup.md`, "Photobank column-type drift" subsection, confirm it sits
  directly after "These diagnostic queries are read-only and safe to run against any environment."
- Read `memory/gotchas/ef-migration-codebase-drift.md`, "Known limitation of the safeguard" section,
  confirm the original sentence is intact and the new paragraph is appended after it.
- Verified `PhotobankSchemaHealthCheck` is registered as `photobank-schema` under `/health/ready` in
  `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:108-109`, matching the claim
  made in both doc edits.

## Notes

Followed the task context verbatim — content was fully specified there, no deviations.

## PR Summary
Added a Photobank-specific column-type-drift diagnostic subsection to the schema-drift runbook in
`docs/development/setup.md`, alongside the existing table-existence (`DqtRuns` rename) case, and
cross-referenced the new `PhotobankSchemaHealthCheck` safeguard from the "Known limitation" note in
`memory/gotchas/ef-migration-codebase-drift.md`.

### Changes
- `docs/development/setup.md` — new "Photobank column-type drift" diagnostic subsection with SQL
  checks and interpretation guidance
- `memory/gotchas/ef-migration-codebase-drift.md` — appended cross-reference to
  `PhotobankSchemaHealthCheck` in the "Known limitation" section

## Status
DONE
