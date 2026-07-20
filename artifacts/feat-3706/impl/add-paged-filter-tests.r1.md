# Implementation: add-paged-filter-tests

## What was implemented
Added a new integration test file covering the three optional filters
(`filenameFilter`, `statusFilter`, `contentTypeFilter`) of
`LeafletDocumentRepository.GetDocumentsPagedAsync`, which previously had zero
tests against the real repository (it was only ever exercised via mocks in
`GetLeafletDocumentsHandlerTests.cs`). The file follows the exact
Testcontainers/pgvector pattern established by the sibling
`LeafletRepositoryIntegrationTests.cs`.

Before writing, the actual production file
(`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`)
and the existing sibling test file were read in full and matched the task
context's description exactly (method signature, filter/sort logic,
`LeafletDocument`/`LeafletDocumentStatus` shapes), so the file was created
verbatim as specified in the task context with no deviations.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs` — new integration test file: `IAsyncLifetime` fixture with a `PostgreSqlContainer` (`pgvector/pgvector:pg16`), hand-rolled `SetupSchemaAsync` DDL matching the sibling file's schema, a `MakeDocument` factory helper (varies filename/hash/status/contentType/indexedAt/ingestedAt), and 6 test methods (8 test cases with Theory data) covering the three filters.

## Tests
- `GetDocumentsPagedAsync_FilenameFilter_MatchesPartialCaseSensitive` — partial match on `filenameFilter`, verifies case-sensitive `LIKE` (differently-cased "Invoice-Summary.pdf" excluded).
- `GetDocumentsPagedAsync_FilenameFilter_EscapesLiteralWildcards` — filter text containing `%` and `_` is escaped and matched literally, not as SQL wildcards.
- `GetDocumentsPagedAsync_FilenameFilter_NoMatch_ReturnsEmptyPageAndZeroTotal` — no match returns empty list and `Total == 0`.
- `GetDocumentsPagedAsync_StatusFilter_MatchesEachEnumValue` (Theory, 3 cases: Processing/Indexed/Failed) — each enum value filters to exactly the matching document.
- `GetDocumentsPagedAsync_ContentTypeFilter_MatchesExactOnly` — exact equality; `"application/pdf-x"` does not match `"application/pdf"`.
- `GetDocumentsPagedAsync_AllFiltersCombined_AndSemantics` — all three filters combined with AND semantics; only the document satisfying all three is returned.

## How to verify
```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletDocumentRepositoryPagedTests"
```
Expected: `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8` (5 Facts + 3 Theory cases), requires a running Docker/Podman daemon for Testcontainers.

Build/format (already run in this session):
```
dotnet build Anela.Heblo.sln          # 0 errors (251 pre-existing warnings, unrelated)
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs
```

## Notes
- **Sandbox limitation (accepted, pre-existing):** this sandbox has no running Docker daemon, so `dotnet test` against these `Category=Integration` tests was intentionally NOT run — Testcontainers would hang indefinitely trying to reach a daemon rather than failing fast. Verification here relies on: (1) a careful line-by-line comparison of the test file's assumptions against the actual production code and domain types, which matched exactly, and (2) a successful `dotnet build` with 0 errors.
- The production code, domain model (`LeafletDocument`, `LeafletDocumentStatus`), and sibling test file all matched the task context's description exactly — no adaptation was needed; the file was created verbatim as specified.
- `dotnet format --verify-no-changes` produced no output/diffs for the new file, so no reformatting pass was needed.
- Per the task context's own caveat: if this test is later run against real Postgres/pgvector and the case-sensitivity assertion in `GetDocumentsPagedAsync_FilenameFilter_MatchesPartialCaseSensitive` fails, that indicates the `pgvector/pgvector:pg16` image's default collation is not case-sensitive in that environment — a genuine finding to report, not a test bug to silently loosen.

## PR Summary
This adds a new integration test file,
`LeafletDocumentRepositoryPagedTests.cs`, covering `GetDocumentsPagedAsync`'s
three optional filters (`filenameFilter`, `statusFilter`,
`contentTypeFilter`), which previously had zero coverage against the real
repository — it was only ever exercised indirectly through handler tests
that mock `ILeafletDocumentRepository` entirely. The new file mirrors the
existing `LeafletRepositoryIntegrationTests.cs` pattern: a Testcontainers
`PostgreSqlContainer` running `pgvector/pgvector:pg16`, hand-rolled schema
setup, and plain xUnit `Assert.*` calls, tagged `[Trait("Category",
"Integration")]`. Tests cover partial case-sensitive filename matching,
literal escaping of SQL wildcard characters (`%`/`_`) in the filename
filter, no-match behavior, all three `LeafletDocumentStatus` enum values,
exact-match content-type filtering, and AND-combination of all three
filters together. This is a test-only, additive change with no production
code touched. A follow-up task (`add-paged-sort-and-paging-tests`) will
append sort/paging test coverage to this same file.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs` — new file, 6 test methods (8 test cases) covering `GetDocumentsPagedAsync`'s three filters.

## Status
DONE_WITH_CONCERNS
