# Specification: Close coverage gap in `LeafletDocumentRepository` (AddChunksAsync, SearchSimilarAsync, GetDocumentsPagedAsync)

## Summary
`LeafletDocumentRepository` (`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`) has three methods with real, un-exercised behavior: the multi-batch loop in `AddChunksAsync`, the raw-SQL reader mapping in `SearchSimilarAsync`, and the three-filter/four-way-sort logic in `GetDocumentsPagedAsync` (currently untested at the repository level). This spec enumerates the exact test cases to add — extending the existing Testcontainers-backed integration suite at `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs` — so each of these code paths is verified against a real Postgres+pgvector instance. This is a test-only change: no production code, DTOs, or interfaces are modified.

## Background
A weekly coverage-gap routine flagged `LeafletDocumentRepository.cs` at 7.8% line coverage (threshold 60%, CI run #29525794843). Investigation of the current test suite shows the picture is more nuanced than the raw number suggests:

- `LeafletRepositoryIntegrationTests.cs` already exists and already covers: single-chunk and 5-chunk `AddChunksAsync` inserts, idempotency (`ON CONFLICT DO NOTHING`), empty-input no-op, basic `SearchSimilarAsync` ordering/summary mapping, `GetChunkByIdAsync`, `DeleteDocumentAsync` cascade, and the `GetBy*` lookup methods.
- **However**, none of these tests exercise the `AddChunksAsync` multi-batch boundary (`MaxRowsPerBatch = 1000`), none assert the *full* set of reader-mapped columns in `SearchSimilarAsync` (ordinals 6–8, cross-document joins, the hard-coded empty `Embedding`), and **`GetDocumentsPagedAsync` has zero tests against the real repository** — it is only ever exercised through handler tests that mock `ILeafletDocumentRepository` entirely (e.g. `GetLeafletDocumentsHandlerTests.cs`), so the real filter/sort/paging SQL translation is never run.
- **Important CI fact**: this repository's tests are tagged `[Trait("Category", "Integration")]` and use `Testcontainers.PostgreSql` (pgvector image) because the code under test uses Npgsql-specific features (the `Vector` type, the `<=>` cosine-distance operator, and the 3-argument `EF.Functions.Like` overload with an escape character) that have no InMemory/SQLite equivalent — a true unit test of this class is not feasible. Both `.github/workflows/ci-feature-branch.yml` and `ci-main-branch.yml` run `dotnet test` with `--filter "Category!=Playwright&Category!=Integration"`, and no workflow in this repo runs `Category=Integration` tests at all. **This means the new tests specified here will exercise and lock down the exact behaviors the brief is worried about, but will not move the automated line-coverage percentage that CI gates on**, since Integration-tagged tests are excluded from every coverage run. This is a pre-existing gap in the CI setup, not something this task should fix (see Out of Scope). The correct action is still to add the tests, following the existing file's established pattern, because it is the only technically viable way to verify this code against real Postgres/pgvector semantics.

## Functional Requirements

### FR-1: `AddChunksAsync` batch-boundary coverage
Add tests to `LeafletRepositoryIntegrationTests.cs` that exercise the `for (var offset = 0; offset < chunkList.Count; offset += MaxRowsPerBatch)` loop across a batch boundary, using `MaxRowsPerBatch = 1000`.

- **FR-1.1 — Exactly at the batch limit (single batch, boundary edge).** Call `AddChunksAsync` with exactly 1000 chunks (unique sequential `ChunkIndex` 0..999, unique embeddings). Assert all 1000 rows persist with correct `ChunkIndex`/`Content`/`Summary`/`WordCount`, in particular the first (`ChunkIndex = 0`) and last (`ChunkIndex = 999`) rows of the batch.
- **FR-1.2 — One chunk over the batch limit (two-batch path).** Call `AddChunksAsync` with 1001 chunks (unique sequential `ChunkIndex` 0..1000) in a single call. Assert:
  - Total row count for the document equals 1001 (no chunks silently dropped, none duplicated).
  - The last row of the first batch (`ChunkIndex = 999`) round-trips with its own distinct `Content`/`Summary`/`WordCount`.
  - The single row of the second batch (`ChunkIndex = 1000`) round-trips with its own distinct `Content`/`Summary`/`WordCount`, proving the second `INSERT` (built with a fresh `NpgsqlCommand` and its own `@id0`-style parameter set) executed correctly and did not collide with or overwrite the first batch's parameter names.

**Acceptance criteria:**
- Both tests use the `MakeDocument` helper and a distinct `ContentHash` per test (matching existing file conventions) so they don't collide with other tests sharing the container-per-test-class lifecycle.
- No exception is thrown in either case.
- The connection-state guard (`if (connection.State != Open)`) is not given a dedicated test: it is already exercised on every existing call (`AddChunksAsync_PersistsSummary` hits the "closed → open" branch; `AddChunksAsync_IsIdempotent_WhenCalledTwiceWithSameId`'s second call hits the "already open" branch). No new test is required for this guard; note this explicitly as a comment or leave uncommented — it is already covered by existing tests, so FR-1 does not need to add anything for it.

### FR-2: `SearchSimilarAsync` reader-mapping coverage
Add tests that assert every column read from the raw-SQL result (ordinals 0–8: `Id`, `DocumentId`, `ChunkIndex`, `Content`, `Summary`, `WordCount`, `Filename`, `SourcePath`, `Score`) and the `LeafletChunk`/`LeafletDocument` reconstruction.

- **FR-2.1 — Full column mapping across two distinct documents.** Insert two documents (distinct `Filename`/`SourcePath`), each with one chunk with distinct `Content`, `Summary`, `WordCount`, `ChunkIndex`, and embeddings `[1,0,0]` and `[0,1,0]` respectively. Query with embedding `[1,0,0]`, `topK: 2`. For the top (closest) result, assert:
  - `Chunk.Id`, `Chunk.DocumentId`, `Chunk.ChunkIndex`, `Chunk.Content`, `Chunk.Summary`, `Chunk.WordCount` all equal the values inserted for that chunk.
  - `Chunk.Document.Id` equals that chunk's own `DocumentId`, and `Chunk.Document.Filename` / `Chunk.Document.SourcePath` equal that chunk's **own** document's values (not the other document's) — this proves the `JOIN` on ordinals 6/7 resolves to the correct document per row, not a fixed/first document.
  - `Chunk.Embedding` equals an empty array (`Array.Empty<float>()` / `[]`) — this is intentional current behavior (the reader never populates `Embedding`; it always builds `[]`), and must be asserted explicitly so a future change to this behavior is caught.
  - `Score` (ordinal 8) is a `double`, and the closer chunk's score is strictly greater than the farther chunk's score.
- **FR-2.2 — `topK` limits result count.** With 3 chunks of distinct embeddings, call `SearchSimilarAsync(topK: 1)` and assert exactly 1 result is returned (verifies the `LIMIT @topK` parameter is wired, not just present in the SQL text).
- **FR-2.3 — Empty result set.** Call `SearchSimilarAsync` against a document with zero chunks (or an empty database) and assert an empty (non-null) list is returned with no exception — this exercises the `while (await reader.ReadAsync(ct))` loop's zero-iteration path.

**Acceptance criteria:**
- All three tests pass in a single run of the extended `LeafletRepositoryIntegrationTests.cs`.
- The `CommandTimeout = 120` assignment is a fixed literal; it is exercised structurally as a statement by any test that calls `SearchSimilarAsync` (satisfying line coverage), and is not a candidate for a dedicated behavioral test (would require waiting out or mocking a 120-second timeout, which is not a reasonable use of test time — call this out as a design decision, not an open question).

### FR-3: `GetDocumentsPagedAsync` filter/sort/paging coverage
This method currently has **no** tests against the real repository. Add tests covering each optional filter independently, all three combined, all four sort branches in both directions, and paging/total-count behavior.

**Filters:**
- **FR-3.1 — `filenameFilter`, partial match.** Seed documents `"invoice-report.pdf"`, `"Invoice-Summary.pdf"`, `"other.pdf"`. Filter `filenameFilter = "invoice"`. Assert only `"invoice-report.pdf"` is returned — Postgres `LIKE` under the default `C`/case-sensitive collation does not match `"Invoice-Summary.pdf"` against a lowercase pattern. (Assumption, see Background: default collation is case-sensitive; if the target database uses a case-insensitive collation this assertion should be adjusted — flagged here, not left silent.)
- **FR-3.2 — `filenameFilter` escapes literal wildcard characters.** Seed `"50%_off.pdf"` and `"50Xoff.pdf"`. Filter `filenameFilter = "50%_off"`. Assert only `"50%_off.pdf"` matches (proves `%` and `_` in user input are escaped to `\%`/`\_` and treated literally, not as SQL wildcards, and that escaping the backslash first doesn't corrupt the later escapes).
- **FR-3.3 — `filenameFilter` with no match returns an empty page and `Total == 0`.**
- **FR-3.4 — `statusFilter`, each enum value.** `[Theory]` over `LeafletDocumentStatus.Processing`, `.Indexed`, `.Failed`: seed one document per status, filter by each in turn, assert only the matching document is returned.
- **FR-3.5 — `contentTypeFilter`, exact match (not partial).** Seed `ContentType = "application/pdf"` and `"application/pdf-x"`; filter `contentTypeFilter = "application/pdf"`; assert only the exact match is returned.
- **FR-3.6 — All three filters combined (AND semantics).** Seed several documents where only one satisfies all three of filename substring, status, and content type simultaneously; assert exactly that one is returned.

**Sorting (four branches × two directions = 8 cases):**
- **FR-3.7 — `sortBy = "Filename"`, ascending and descending.**
- **FR-3.8 — `sortBy = "Status"`, ascending and descending** (enum ordinal order: `Processing (0) < Indexed (1) < Failed (2)`).
- **FR-3.9 — `sortBy = "IndexedAt"`, ascending and descending**, including at least one document with `IndexedAt = null`. Assert Postgres's default `NULLS` placement: `NULLS LAST` for ascending, `NULLS FIRST` for descending (this is Postgres's documented default and does not depend on explicit `NULLS FIRST/LAST` syntax being present in the LINQ-generated SQL).
- **FR-3.10 — Default/unrecognized `sortBy` falls back to `IngestedAt` ordering, ascending and descending.** Use both `sortBy = ""` and `sortBy = "NotARealColumn"` as inputs (at least one `[Theory]` case for each) to confirm the `_ =>` switch arm is reached however the caller misspells or omits the value.

**Paging and total count:**
- **FR-3.11 — Correct page slicing and stable `Total`.** Seed 5 documents with distinct `IngestedAt` timestamps. With `pageSize = 2`: request `pageNumber = 1` → 2 items (the 2 most-recently-ingested, since default sort is `IngestedAt`) and `Total == 5`; `pageNumber = 2` → the next 2 items, `Total == 5`; `pageNumber = 3` → the remaining 1 item, `Total == 5`.
- **FR-3.12 — `Total` reflects the *filtered* count, not the paged count.** Combine a filter that matches 3 of 5 seeded documents with `pageSize = 2`; assert the returned page has 2 items but `Total == 3`.

**Acceptance criteria:**
- Every test seeds only the documents it needs, with unique `ContentHash`/`Filename` values, and asserts against document identity (`Id`) or `Filename`, not just counts, so ordering bugs are caught (not just filtering bugs).
- Tests are added to the same `LeafletRepositoryIntegrationTests.cs` file (or a clearly-named sibling file in the same `Features/Leaflet/Integration` folder, e.g. `LeafletDocumentRepositoryPagedTests.cs`, if the developer prefers to keep the file size manageable) using the same `[Trait("Category", "Integration")]` / `PostgreSqlContainer` / `SetupSchemaAsync` pattern already established — do not introduce a second, different test-infrastructure approach for this one method.

## Non-Functional Requirements

### NFR-1: Performance
No performance targets apply to production code (unchanged). For the test suite itself: each `[Fact]`/`[Theory]` case in this file starts its own `PostgreSqlContainer` instance (xunit creates a new test-class instance per test method by default, and `IAsyncLifetime.InitializeAsync` starts the container each time) — this matches the existing file's behavior and is an accepted cost. Adding roughly 20–25 new test cases (FR-1: 2, FR-2: 3, FR-3: ~20 across theories) will add proportionally to local/manual run time, but since `Category=Integration` tests are excluded from all CI workflows (see Background), this has no impact on PR turnaround time.

### NFR-2: Security
N/A — no new attack surface, no auth/authorization changes, no new secrets or externally-reachable endpoints. Tests use an ephemeral, locally-orchestrated Testcontainers Postgres instance with no real data.

## Data Model
N/A — no schema or entity changes. Tests use the existing `LeafletDocument` / `LeafletChunk` / `LeafletDocumentStatus` domain types and the existing `LeafletDocuments` / `LeafletChunks` tables as already defined by `SetupSchemaAsync` in the integration test file (which mirrors `LeafletDocumentConfiguration` / `LeafletChunkConfiguration`).

## API / Interface Design
N/A — no changes to `ILeafletDocumentRepository` or `LeafletDocumentRepository`. This is a test-only addition.

## Dependencies
- `Testcontainers.PostgreSql` (v3.6.0, already referenced in `Anela.Heblo.Tests.csproj`) with the `pgvector/pgvector:pg16` image — already used by the file being extended.
- `xunit` / `xunit.runner.visualstudio` (already referenced).
- A container runtime (Docker or Podman) available wherever these tests are run; `TestcontainersSettings.ResourceReaperEnabled = false` is already set in the static constructor to support Podman (no Ryuk). No new dependency needs to be added.
- No dependency on any other module or feature.

## Out of Scope
- Modifying `LeafletDocumentRepository.cs` production code (this is a coverage-gap/test-only task; if a real bug were found during test-writing, that would be a separate follow-up, not part of this change).
- Changing the CI coverage-gate configuration (e.g., adding a workflow step that runs `Category=Integration` tests, or otherwise making Integration-tagged tests count toward the 60% line-coverage threshold). This is a real, pre-existing gap between "what the coverage tool measures" and "what is actually tested," but fixing the CI pipeline is a separate, larger decision outside this task's scope.
- Introducing a non-Postgres (InMemory/SQLite) unit-test path for these three methods — not feasible given the Npgsql-specific `Vector` type, `<=>` operator, and 3-arg `EF.Functions.Like` escape overload in use.
- Load/perf testing of `AddChunksAsync` at scales beyond the batch boundary (e.g. tens of thousands of chunks) or of `SearchSimilarAsync` against a large, warmed HNSW index — the brief's concern is correctness at the boundary, not scale.
- Testing the other already-covered repository methods (`GetByHashAsync`, `GetBySourcePathAsync`, `UpdateSourcePathAsync`, `UpdateGraphItemIdAsync`, `UpdateStatusAsync`, `GetDistinctContentTypesAsync`, `GetFirstChunkIdsByDocumentIdsAsync`) — not flagged by the brief and not touched here.

## Open Questions
None — ambiguities (case-sensitivity of `LIKE` filtering, `NULLS` ordering default, and the Integration-tag/CI-coverage interaction) were resolved by reading the actual code, the existing test file's conventions, and the CI workflow filters, and are documented as explicit assumptions/acceptance criteria above.

## Status: COMPLETE
