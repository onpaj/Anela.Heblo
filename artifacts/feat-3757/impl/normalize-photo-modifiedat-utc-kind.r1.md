# Implementation: normalize-photo-modifiedat-utc-kind

## What was implemented
`PhotobankIndexJob.UpsertPhotoBatchAsync` was assigning `photo.ModifiedAt = item.LastModifiedAt ?? DateTime.UtcNow;`, which passes through whatever `DateTimeKind` `item.LastModifiedAt` happens to carry (from Graph delta JSON deserialization this is `Kind=Unspecified`). Npgsql/EF Core rejects `Kind=Unspecified` when writing to a `timestamp with time zone` column, causing the nightly `PhotobankIndexJob` run to throw `ArgumentException` from `PhotobankRepository.SaveChangesAsync` for every photo whose Graph item carries a non-null `lastModifiedDateTime`. The assignment now normalizes the value to `DateTimeKind.Utc` via `DateTime.SpecifyKind` before it's ever assigned to `photo.ModifiedAt`, matching the pattern used elsewhere in this job (`DateTime.UtcNow` fallback is already `Kind=Utc`).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — line 181: `photo.ModifiedAt` assignment now stamps `DateTimeKind.Utc` on the Graph-sourced value instead of passing its Kind through unchanged.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — added `UpsertPhotoBatch_GraphItemLastModifiedAtHasUnspecifiedKind_PhotoModifiedAtIsStampedUtc`, which feeds a `DateTimeKind.Unspecified` `LastModifiedAt` through a simulated Graph delta item and asserts the captured `Photo.ModifiedAt` has `Kind == DateTimeKind.Utc` and the correct instant.

## Tests
- `PhotobankIndexJobTests.UpsertPhotoBatch_GraphItemLastModifiedAtHasUnspecifiedKind_PhotoModifiedAtIsStampedUtc` (new) — confirmed FAIL before the fix (`Kind` was `Unspecified`), PASS after.
- Full `PhotobankIndexJobTests` fixture: 12/12 passed, no regressions.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests"
```
Expect `Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12`.

## Notes
Followed the task-context file exactly — only line 181 of `PhotobankIndexJob.cs` was changed, no other lines in `UpsertPhotoBatchAsync` or elsewhere in the file were touched. This addresses the `Photo.ModifiedAt` field specifically; the issue notes this is the third fix attempt at this signal, so other still-unnormalized `DateTime` fields on the same batch may remain — those are covered by the other pending tasks in this feature (`photobank-phototag-schema-regression-test`, `photobank-schema-drift-health-check`, `photobank-drift-runbook-docs`), not this one.

## PR Summary
Normalizes `Photo.ModifiedAt` to `DateTimeKind.Utc` when it's sourced from a Microsoft Graph delta item's `lastModifiedDateTime` (which arrives with `Kind=Unspecified` after JSON deserialization), addressing part of the telemetry signal in issue #3757. Note per this feature's own spec/arch-review: `ApplicationDbContext`'s global `DateTime` value converter re-stamps every value to `Kind=Unspecified` immediately before write regardless of in-memory `Kind`, so this task alone does not change today's exception rate — the real remediation is the separately-scoped pending migration/converter fix covered by this feature's other tasks. This task's job is the application-layer contract (`Photo.ModifiedAt` is always assigned as Utc) and its regression test, not the end-to-end DB write behavior.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — stamp `DateTimeKind.Utc` on `photo.ModifiedAt` via `DateTime.SpecifyKind`
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — regression test proving the Kind is always Utc regardless of source Kind

## Status
DONE
