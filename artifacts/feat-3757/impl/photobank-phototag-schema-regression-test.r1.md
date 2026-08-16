# Implementation: photobank-phototag-schema-regression-test

## What was implemented
Added a regression-guard test asserting that `PhotoTag.CreatedAt` maps to a `timestamp` (without time
zone) column, matching the pattern already used by `PhotoSchemaTests` for `Photo` and
`PhotobankIndexRoot`. `PhotoTagConfiguration.cs` already calls `.AsUtcTimestamp()` on `CreatedAt`, so
no production code changed — this task only adds test coverage so a future change that drops that
mapping fails CI immediately instead of surfacing as a nightly `PhotobankIndexJob` production
exception (the same class of regression this whole issue, #3757, is about).

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs` — added
  `PhotoTag_DateTimeColumns_AreTimestampWithoutTimeZone([Theory][InlineData(nameof(PhotoTag.CreatedAt))])`,
  reusing the existing `NewNpgsqlContext()` private helper already defined in this test class.

## Tests
- `PhotoSchemaTests.PhotoTag_DateTimeColumns_AreTimestampWithoutTimeZone` (new) — verified PASS
  immediately, as expected per the task context (this is a regression guard for an already-correct
  mapping, not a fix for a currently-broken one).

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotoSchemaTests.PhotoTag_DateTimeColumns_AreTimestampWithoutTimeZone"
```
Expect `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

## Notes
Followed the task-context file exactly — only the new theory method was added to
`PhotoSchemaTests.cs`; no other file was touched. Other still-pending tasks in this feature
(`photobank-schema-drift-health-check`, `photobank-drift-runbook-docs`) cover the remaining scope of
issue #3757.

## PR Summary
Adds a regression-guard unit test confirming `PhotoTag.CreatedAt` stays mapped to
`timestamp without time zone`, so a future regression in that EF Core configuration is caught by CI
instead of surfacing as a nightly `DateTime Kind=Unspecified` production exception.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotoSchemaTests.cs` — new
  `PhotoTag_DateTimeColumns_AreTimestampWithoutTimeZone` regression test

## Status
DONE
