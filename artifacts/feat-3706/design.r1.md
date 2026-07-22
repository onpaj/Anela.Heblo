# Design: Close coverage gap in `LeafletDocumentRepository`

## Component Design

This is a test-only change. No production code, DTOs, or interfaces are added or modified. Two test files are touched, both under `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/`, both tagged `[Trait("Category", "Integration")]` and backed by a real `Testcontainers.PostgreSql` instance (`pgvector/pgvector:pg16`), consistent with the file(s) being extended.

### File 1 (extend): `LeafletRepositoryIntegrationTests.cs`

Responsibility: cover the un-exercised code paths of `AddChunksAsync` and `SearchSimilarAsync`. Reuses everything already established in the file — no new fixtures.

- Reused as-is: the class's existing `PostgreSqlContainer` field, `IAsyncLifetime.InitializeAsync`/`DisposeAsync`, `SetupSchemaAsync` (hand-rolled DDL for `LeafletDocuments`/`LeafletChunks` + HNSW index), the `_repository`/`_context` fields, the `MakeDocument` helper, and the `TestcontainersSettings.ResourceReaperEnabled = false` static-constructor setting for Podman.
- New test methods, added after the existing `AddChunksAsync_*` block:
  - `AddChunksAsync_PersistsAll_AtExactBatchBoundary` (FR-1.1) — 1000 chunks, `ChunkIndex` 0..999, asserts first/last row round-trip.
  - `AddChunksAsync_PersistsAll_AcrossTwoBatches` (FR-1.2) — 1001 chunks, `ChunkIndex` 0..1000, asserts total count = 1001 and that the last row of batch 1 (`ChunkIndex = 999`) and the single row of batch 2 (`ChunkIndex = 1000`) each round-trip with distinct `Content`/`Summary`/`WordCount`, proving the second `NpgsqlCommand`'s parameter set doesn't collide with the first's.
  - Both generated via `Enumerable.Range(...).Select(...)` scaled up from the existing 5-chunk idiom, with cheap per-index embeddings (e.g. `new[] { (float)i, 0f, 0f }`) since only scalar-field round-trip is asserted, not similarity ordering.
- New test methods, added after the existing `SearchSimilarAsync_*` block:
  - `SearchSimilarAsync_MapsAllReaderColumns_AcrossTwoDocuments` (FR-2.1) — two documents (distinct `Filename`/`SourcePath`), one chunk each, embeddings `[1,0,0]` / `[0,1,0]`, query `[1,0,0]` with `topK: 2`. Asserts every reader-mapped field of the top result (`Chunk.Id`, `DocumentId`, `ChunkIndex`, `Content`, `Summary`, `WordCount`), that `Chunk.Document.Id`/`Filename`/`SourcePath` belong to that chunk's *own* document (not the other one — catches a fixed/first-document JOIN bug), that `Chunk.Embedding` is asserted as `Array.Empty<float>()` (current intentional behavior), and that `Score` is a `double` with the closer chunk's score strictly greater than the farther one's.
  - `SearchSimilarAsync_TopKLimitsResultCount` (FR-2.2) — 3 chunks, `topK: 1`, asserts exactly 1 result.
  - `SearchSimilarAsync_ReturnsEmptyList_WhenNoChunks` (FR-2.3) — zero-chunk document, asserts an empty, non-null list with no exception.
- Data flow for both blocks: build chunks/documents in memory → call the write path under test (`AddChunksAsync`, or `AddDocumentAsync`/`AddChunksAsync` for FR-2 setup, dogfooding the already-tested write path) → re-read via `_context.LeafletChunks.AsNoTracking()...ToListAsync()` (EF) for FR-1, or via `SearchSimilarAsync` (the raw-SQL read path under test) for FR-2 → assert against originally-inserted values.
- No dedicated test is added for the connection-state guard (`if (connection.State != Open)`) — already exercised by existing tests per the spec's FR-1 acceptance criteria; left uncommented, no new coverage needed.
- No dedicated test is added for the `CommandTimeout = 120` literal beyond its structural execution by any `SearchSimilarAsync` call — per FR-2 acceptance criteria, a timeout-triggering test is out of scope.

### File 2 (new): `LeafletDocumentRepositoryPagedTests.cs`

Responsibility: the only method with zero existing repository-level coverage — `GetDocumentsPagedAsync` (filters, four-way sort, paging/total-count). Placed in the same folder as File 1.

- Same class shape as `LeafletRepositoryIntegrationTests`: `[Trait("Category", "Integration")]`, `IAsyncLifetime`, a private `PostgreSqlContainer` field (`pgvector/pgvector:pg16`, Ryuk disabled via the same static-constructor setting), its own `SetupSchemaAsync` copied verbatim from File 1 (byte-for-byte identical DDL — not shared via inheritance or a static helper; YAGNI per the architect's Decision 1/3rd-copy trigger).
- A `MakeDocument`-style helper, copied from File 1 and extended with optional named parameters for `status`, `contentType`, `indexedAt`, and `ingestedAt` (defaulting to File 1's existing defaults), since FR-3 needs to vary those fields — the original helper only varies `filename`/`hash`/`driveId`/`graphItemId`.
- Test methods (naming follows the file's `MethodUnderTest_Condition_ExpectedResult` convention):
  - `GetDocumentsPagedAsync_FilenameFilter_MatchesPartialCaseSensitive` (FR-3.1) — seeds `"invoice-report.pdf"`, `"Invoice-Summary.pdf"`, `"other.pdf"`; filter `"invoice"`; asserts only the lowercase match returns and the differently-cased file is absent.
  - `GetDocumentsPagedAsync_FilenameFilter_EscapesLiteralWildcards` (FR-3.2) — seeds `"50%_off.pdf"` / `"50Xoff.pdf"`; filter `"50%_off"`; asserts only the literal match returns.
  - `GetDocumentsPagedAsync_FilenameFilter_NoMatch_ReturnsEmptyPageAndZeroTotal` (FR-3.3).
  - `GetDocumentsPagedAsync_StatusFilter_MatchesEachEnumValue` (FR-3.4) — `[Theory]` over `Processing`/`Indexed`/`Failed`.
  - `GetDocumentsPagedAsync_ContentTypeFilter_MatchesExactOnly` (FR-3.5) — `"application/pdf"` vs `"application/pdf-x"`.
  - `GetDocumentsPagedAsync_AllFiltersCombined_AndSemantics` (FR-3.6).
  - `GetDocumentsPagedAsync_SortByFilename_BothDirections` (FR-3.7).
  - `GetDocumentsPagedAsync_SortByStatus_BothDirections` (FR-3.8) — enum ordinal order `Processing(0) < Indexed(1) < Failed(2)`.
  - `GetDocumentsPagedAsync_SortByIndexedAt_BothDirections_WithNulls` (FR-3.9) — at least one document with `IndexedAt = null`; asserts Postgres default `NULLS LAST` (ascending) / `NULLS FIRST` (descending).
  - `GetDocumentsPagedAsync_UnrecognizedSortBy_FallsBackToIngestedAt` (FR-3.10) — `[Theory]` with `sortBy = ""` and `sortBy = "NotARealColumn"`, both directions.
  - `GetDocumentsPagedAsync_PageSlicing_StableTotal` (FR-3.11) — 5 documents, `pageSize = 2`, all three pages checked for item identity and `Total == 5`.
  - `GetDocumentsPagedAsync_Total_ReflectsFilteredCount_NotPagedCount` (FR-3.12).
- Every filter/sort test asserts on document identity (`Id`/`Filename`), not just counts, and each filter test asserts both inclusion and exclusion (e.g. FR-3.1 must confirm `"Invoice-Summary.pdf"` is absent, not merely that `"invoice-report.pdf"` is present) — per the spec's and architect's acceptance criteria.
- Assertions throughout both files use plain xUnit `Assert.*` (`Assert.Equal`, `Assert.NotNull`, `Assert.Single`, etc.), matching the existing file's established convention, not `FluentAssertions` (per architect Decision 2 — matching existing style takes precedence over the testing-strategy doc's general preference).
- Each test uses a distinct `ContentHash`/`Filename` per the existing file's collision-avoidance convention, even though each test gets its own container instance.

No shared base class, `[CollectionDefinition]`, or shared fixture is introduced between the two files — each keeps its own container-per-test-class lifecycle, matching the already-accepted cost documented in the spec's NFR-1 and the architect's explicit rejection of a shared-fixture option (YAGNI; revisit only if a third Leaflet integration-test file needs the same schema).

## Data Schemas

N/A — no schema, DTO, or entity changes. Tests exercise the existing `LeafletDocument`, `LeafletChunk`, and `LeafletDocumentStatus` domain types and the existing `LeafletDocuments`/`LeafletChunks` tables exactly as defined by the existing `SetupSchemaAsync` DDL (mirroring `LeafletDocumentConfiguration`/`LeafletChunkConfiguration`). The shapes being asserted against are the existing `(LeafletChunk Chunk, double Score)` tuple returned by `SearchSimilarAsync` and the existing paged-result shape (`Items`, `Total`) returned by `GetDocumentsPagedAsync` — neither is modified by this change.
