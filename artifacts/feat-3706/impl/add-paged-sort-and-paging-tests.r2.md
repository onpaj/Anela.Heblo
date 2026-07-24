# Implementation: add-paged-sort-and-paging-tests (revision 2)

## What was implemented

Fixed the single real bug identified in code review r1: the test
`GetDocumentsPagedAsync_SortByStatus_BothDirections` asserted enum-ordinal
sort order for the `Status` column (`Processing, Indexed, Failed` ascending),
but `LeafletDocumentConfiguration` converts `Status` to a lowercase string
column via `.HasConversion(v => v.ToString().ToLowerInvariant(), ...)`, so
Postgres (and EF Core's SQL translation generally) sorts on the stored
string alphabetically, not the CLR enum ordinal. The expected orderings in
both the ascending and descending assertions were corrected, and the
preceding comment was rewritten to describe the actual (string-based)
collation behavior instead of the incorrect enum-ordinal assumption.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs` — corrected `GetDocumentsPagedAsync_SortByStatus_BothDirections`: ascending expectation changed from `Processing, Indexed, Failed` to `Failed, Indexed, Processing`; descending expectation changed from `Failed, Indexed, Processing` to `Processing, Indexed, Failed`; comment above the assertions rewritten to explain the string-conversion-driven alphabetical collation instead of the (incorrect) enum-ordinal claim.

## Tests

- `GetDocumentsPagedAsync_SortByStatus_BothDirections` — **fixed**. Now asserts alphabetical ordering of the stored string values: ascending `Failed, Indexed, Processing` (`"failed" < "indexed" < "processing"`), descending `Processing, Indexed, Failed`. No other test methods were touched — the other five (`SortByFilename`, `SortByIndexedAt_WithNulls`, `UnrecognizedSortBy` theory, `PageSlicing_StableTotal`, `Total_ReflectsFilteredCount_NotPagedCount`) were reviewed and confirmed correct in r1; unchanged here.

## How to verify

1. Read `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentConfiguration.cs` lines ~22-27 — confirms `Status` is mapped with `.HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<LeafletDocumentStatus>(v, true))`, i.e. stored as `"processing"` / `"indexed"` / `"failed"`.
2. Read the corrected test method in `LeafletDocumentRepositoryPagedTests.cs` — assertions now match alphabetical order of the stored strings, and the comment documents why.
3. Build: `timeout 240 dotnet build Anela.Heblo.sln` — 0 errors (251 pre-existing warnings unrelated to this file).
4. Format: `timeout 180 dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs` — clean, no changes needed.
5. Full run (outside this sandbox, requires Docker): `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletDocumentRepositoryPagedTests"` should show 15 passing.

## Notes

- **What was wrong before**: the r1 test encoded the assumption (inherited from task context) that sorting by `Status` follows the C# enum's ordinal values (`Processing=0`, `Indexed=1`, `Failed=2`). That assumption doesn't hold because the EF Core `HasConversion` maps the enum to its lowercase string representation before it reaches the database column, so `ORDER BY "Status"` operates on the string, not the original enum ordinal.
- **What was independently verified**: I read `LeafletDocumentConfiguration.cs` directly (not just trusting the reviewer's report) and confirmed the `HasConversion` call maps `Status` to a `varchar(16)` lowercase string column with no custom collation override, meaning standard alphabetical/byte-order comparison applies: `"failed" < "indexed" < "processing"`. This matches the reviewer's independently-reproduced EF Core/Sqlite result exactly, so no further experimentation was needed — the fix directly mirrors the reviewer's specified correct values.
- **Remaining Docker-execution limitation**: this sandbox has no running Docker daemon, so the Testcontainers-based Postgres integration tests in this file (tagged `Category=Integration`) cannot be executed here; `dotnet test` was deliberately not run, per task instruction. Correctness of the fix was verified by reading the persistence configuration and the test code directly, not by executing the test. This is the same known, accepted limitation noted in the r1 submission.

## PR Summary

Fixes a test bug found in code review r1: `GetDocumentsPagedAsync_SortByStatus_BothDirections` in
`LeafletDocumentRepositoryPagedTests.cs` asserted that sorting by `Status` follows the C# enum's
ordinal values, but `LeafletDocumentConfiguration` stores `Status` as a lowercase string column via
`HasConversion`, so the database actually sorts alphabetically on the stored string
(`"failed" < "indexed" < "processing"`). The test's expected orderings for both ascending and
descending sort, plus the explanatory comment above the assertions, have been corrected to match
this actual string-based collation behavior. No production code changes were needed — this is a
test-only correctness fix. Build passes with 0 errors and `dotnet format --verify-no-changes`
passes clean on the changed file. As before, the Testcontainers-based Postgres integration tests
in this file cannot be executed in this sandbox (no Docker daemon); the fix was verified by
directly reading the EF Core configuration and reasoning about the resulting SQL collation, not by
running the test.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs` — corrected expected sort order in `GetDocumentsPagedAsync_SortByStatus_BothDirections` (ascending: `Failed, Indexed, Processing`; descending: `Processing, Indexed, Failed`) and updated the accompanying comment to describe the actual string-based collation instead of the incorrect enum-ordinal assumption.

## Status
DONE_WITH_CONCERNS
