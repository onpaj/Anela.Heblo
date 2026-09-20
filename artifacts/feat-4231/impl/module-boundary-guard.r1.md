# Implementation: module-boundary-guard

## What was implemented
Added the module boundary guard for `ExpeditionListArchive -> FileStorage`, as
specified in the task context. This locks in the migration completed by the
four prior tasks (`migrate-download-handler`, `migrate-reprint-handler`,
`migrate-get-lists-by-date-handler`, `migrate-get-dates-handler`), which moved
`ExpeditionListArchive` off direct `FileStorage` references and onto the
module-owned `IExpeditionListArchiveBlobStore` contract, implemented by
`ExpeditionListArchiveBlobStoreAdapter` in `FileStorage.Infrastructure`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` —
  added the empty `ExpeditionListArchiveFileStorageAllowlist` field (grouped
  with the other per-pair allowlist fields, right after
  `ExpeditionListShoptetOrdersAllowlist`) and a new `ModuleBoundaryRule` entry
  named `"ExpeditionListArchive -> FileStorage"` in the `Rules()` `TheoryData`
  initializer (inserted right after the existing
  `"ExpeditionListArchive -> ExpeditionList"` rule), forbidding
  `Anela.Heblo.Domain.Features.FileStorage`,
  `Anela.Heblo.Application.Features.FileStorage`, and
  `Anela.Heblo.Persistence.FileStorage` namespace prefixes from being
  referenced by `Anela.Heblo.Application.Features.ExpeditionListArchive`.

## Tests
`ModuleBoundariesTests` (existing theory-driven architecture test suite) now
includes the new `"ExpeditionListArchive -> FileStorage"` row. No new test
file was needed — the task is itself the addition of one more row of
existing coverage.

## How to verify
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
```
Result: `Passed! - Failed: 0, Passed: 38, Skipped: 0, Total: 38` — all rules,
including the new one, pass with zero violations.

## Notes
No deviations from the task context — the two code blocks were inserted
verbatim at the specified insertion points.

## PR Summary
Added an architecture-test guard (`ModuleBoundariesTests`) that locks in the
`ExpeditionListArchive -> FileStorage` module boundary, so a future change
cannot silently reintroduce a direct dependency from `ExpeditionListArchive`
onto `FileStorage`'s Domain/Application/Persistence namespaces now that the
four handler migrations route through the module-owned
`IExpeditionListArchiveBlobStore` contract instead.

### Changes
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — new `ExpeditionListArchiveFileStorageAllowlist` field and new `"ExpeditionListArchive -> FileStorage"` `ModuleBoundaryRule` entry

## Status
DONE
