# Architecture Review: Close coverage gap in `LeafletDocumentRepository`

## Skip Design: true

## Architectural Fit Assessment

This is a pure test-addition task with no production code, contract, or UI surface change, so it fits cleanly with existing patterns rather than introducing anything new.

I verified the claims in the spec directly:

- `LeafletDocumentRepository.cs` (`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`) does contain exactly the three methods called out: `AddChunksAsync` (batch loop at line 37, `MaxRowsPerBatch = 1000` at line 14, connection-state guard at line 34), `SearchSimilarAsync` (raw `NpgsqlCommand` with `CommandTimeout = 120` at line 121, reader ordinals 0–8 at lines 132–151), and `GetDocumentsPagedAsync` (three optional filters at lines 189–199, four-way `sortBy` switch at lines 201–215, paging/count at lines 217–223).
- `LeafletRepositoryIntegrationTests.cs` (`backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs`) is real, already tagged `[Trait("Category", "Integration")]`, already uses `Testcontainers.PostgreSql` with the `pgvector/pgvector:pg16` image, `IAsyncLifetime` for per-test container lifecycle, a `MakeDocument` helper, and a `SetupSchemaAsync` that hand-rolls the `LeafletDocuments`/`LeafletChunks` DDL (including the HNSW index) rather than running EF migrations. It currently has 14 tests and does **not** touch `GetDocumentsPagedAsync` at all — confirmed by grep, zero references to that method in the file.
- I confirmed the CI claim in the spec: both `.github/workflows/ci-feature-branch.yml` (lines 87, 93) and `ci-main-branch.yml` (line 150) run `dotnet test` with `--filter "Category!=Playwright&Category!=Integration"`. No workflow filters on `Category=Integration`. So the spec's central premise — these tests will verify real behavior but will not move the CI-gated coverage number — is accurate, not speculative.
- `GetLeafletDocumentsHandlerTests.cs` does mock `ILeafletDocumentRepository.GetDocumentsPagedAsync` entirely (confirmed via grep, 5 `.Setup(r => r.GetDocumentsPagedAsync(...))` call sites), so the real LINQ-to-SQL translation for filters/sort is indeed never exercised today.

There's no module-boundary, DI, or contract concern here: `LeafletDocumentRepository` is Persistence-layer infrastructure already bound in `LeafletModule.cs` per ADR-004, and none of that changes. The only real architectural question is "does the new test code follow the one true pattern already established," and the answer is yes, it should, because a second pattern would be pure inconsistency with no benefit.

## Proposed Architecture

### Component Overview

No new components. All new test methods are added as additional `[Fact]`/`[Theory]` members inside the existing `LeafletRepositoryIntegrationTests` class (or a sibling partial-purpose file in the same folder for `GetDocumentsPagedAsync`, see below), reusing the existing `_repository`/`_context` fields, the existing `MakeDocument` helper, and the existing `SetupSchemaAsync` schema. No fixture changes, no new base classes, no new NuGet packages (Testcontainers.PostgreSql, xunit are already referenced).

### Key Design Decisions

#### Decision 1: One file vs. a sibling file for `GetDocumentsPagedAsync`

**Options considered:**
1. Add all ~25 new test methods directly into `LeafletRepositoryIntegrationTests.cs`, growing it from 445 lines to roughly 900+.
2. Split `GetDocumentsPagedAsync` coverage into a new sibling file, e.g. `LeafletDocumentRepositoryPagedTests.cs`, in the same `Features/Leaflet/Integration/` folder, duplicating the container/schema/`MakeDocument` bootstrap.
3. Extract a shared base class or fixture collection (e.g. `[CollectionDefinition]` with a shared container) that both files use.

**Chosen approach:** Option 2 — a new sibling file `LeafletDocumentRepositoryPagedTests.cs` in `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/`, dedicated to `GetDocumentsPagedAsync` (FR-3, ~20 cases). Keep `AddChunksAsync` (FR-1) and `SearchSimilarAsync` (FR-2) additions in the existing `LeafletRepositoryIntegrationTests.cs`, since they extend methods that file already covers.

**Rationale:** The spec explicitly offers this as acceptable ("a clearly-named sibling file ... if the developer prefers to keep the file size manageable"). `GetDocumentsPagedAsync` has zero existing tests in this file, so there's no established block to extend — it's a clean addition, not a fragmentation of already-cohesive tests. A single file north of 700–900 lines mixing three unrelated method's tests, several of them `[Theory]`-heavy, becomes harder to navigate for the next reader. Splitting by method under test is a natural seam, not an arbitrary one. Option 3 (shared fixture/collection) is explicitly rejected: it would introduce a *different* test-infrastructure pattern than every other Leaflet integration test uses, contradicting FR-3's acceptance criterion ("do not introduce a second, different test-infrastructure approach"), and per-class container-per-test is already an accepted, working pattern here (NFR-1 in the spec explicitly accepts this cost). Do not build shared collection fixtures speculatively.

#### Decision 2: Assertion library — `Assert.*` vs. `FluentAssertions`

**Options considered:**
1. Follow `docs/architecture/testing-strategy.md`'s stated preference for FluentAssertions.
2. Match the existing file's actual convention, which uses plain xUnit `Assert.*` throughout (`Assert.Equal`, `Assert.NotNull`, `Assert.Single`, etc. — confirmed, no FluentAssertions usage anywhere in the 445-line file).

**Chosen approach:** Option 2 — plain `Assert.*`, matching the file being extended.

**Rationale:** CLAUDE.md is explicit: "Match existing style even if you'd do it differently." Mixing `Assert.Equal` and `.Should().Be()` in the same file/PR is a bigger readability cost than the strategy doc's stated (but here unfollowed) preference. This is a documentation/reality drift worth a one-line note back to the team, not a reason to introduce inconsistency into this file.

#### Decision 3: Batch-boundary test data generation (FR-1)

**Options considered:**
1. Hand-write 1000/1001 chunk literals (impractical).
2. Generate via `Enumerable.Range(...).Select(...)`, following the existing `AddChunksAsync_PersistsAllRows_WhenMultipleChunks` pattern (already uses `Enumerable.Range(0, 5).Select(i => new LeafletChunk {...})`) but scaled to 1000/1001, with distinct-but-cheap embeddings (e.g. `[i, 0, 0]` cast to float — exact vector values don't matter for insert-count assertions, only uniqueness/round-trip of scalar fields matters).

**Chosen approach:** Option 2, scaled up.

**Rationale:** Directly reuses an established idiom in the same file instead of inventing a new one. Embeddings can be trivial per-index vectors (`new[] { (float)i, 0f, 0f }`) since FR-1 only asserts `ChunkIndex`/`Content`/`Summary`/`WordCount` round-trip, not embedding search correctness (that's FR-2's job).

## Implementation Guidance

### Directory / Module Structure

- Extend: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs`
  - Add FR-1 tests (`AddChunksAsync_PersistsAll_AtExactBatchBoundary`, `AddChunksAsync_PersistsAll_AcrossTwoBatches` or similarly descriptive names matching the file's `MethodUnderTest_Condition_ExpectedResult` naming) after the existing `AddChunksAsync_*` tests.
  - Add FR-2 tests (`SearchSimilarAsync_MapsAllReaderColumns_AcrossTwoDocuments`, `SearchSimilarAsync_TopKLimitsResultCount`, `SearchSimilarAsync_ReturnsEmptyList_WhenNoChunks`) after the existing `SearchSimilarAsync_*` tests.
- Create new: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs`
  - Same class shape as `LeafletRepositoryIntegrationTests`: `[Trait("Category", "Integration")]`, `IAsyncLifetime`, a private `PostgreSqlContainer` field, `InitializeAsync`/`DisposeAsync`, a private `SetupSchemaAsync` (copy verbatim — do not try to share it via inheritance or a static helper unless a second future consumer appears; YAGNI), and its own `MakeDocument`-style helper (can be copied and, if useful, extended with optional `status`/`contentType`/`indexedAt`/`ingestedAt` parameters, since FR-3 needs to vary those — the original `MakeDocument` only varies `filename`/`hash`/`driveId`/`graphItemId`).
  - No changes anywhere under `backend/src/`.

### Interfaces and Contracts

No interface or contract changes (`ILeafletDocumentRepository` is untouched). The only "contract" developers must follow is the existing test-infrastructure shape:
- `[Trait("Category", "Integration")]` on the class.
- `PostgreSqlContainer` built with `.WithImage("pgvector/pgvector:pg16")`, no Ryuk (`TestcontainersSettings.ResourceReaperEnabled = false` in a static constructor).
- Hand-rolled DDL via `SetupSchemaAsync`, not EF migrations — keep the new file's schema script byte-for-byte identical to the existing one (both tables + HNSW index) unless a test specifically needs a schema variant (none do here).
- A `MakeDocument`-style factory helper with sensible defaults and named-optional overrides, so each test only specifies what it varies.
- Distinct `ContentHash`/`Filename` per test to avoid collisions — moot for the new paged-tests file since each test gets its own container, but keep it anyway for consistency and in case the pattern changes later.

### Data Flow

- **FR-1 (batch boundary):** Test builds N `LeafletChunk` DTOs in memory → single `AddChunksAsync(chunks)` call → repository's `for` loop issues 1 SQL `INSERT` (N=1000) or 2 SQL `INSERT`s (N=1001, batches of 1000 + 1) against the real Postgres container → test re-reads via `_context.LeafletChunks.AsNoTracking()...ToListAsync()` (EF, not raw SQL) to verify count and spot-check first/last rows of each batch. This proves the two `INSERT`s don't collide on parameter names (each batch gets a fresh `NpgsqlCommand` with its own `@id0..` set) and that no row is dropped/duplicated across the boundary.
- **FR-2 (reader mapping):** Test inserts 2+ documents/chunks via the repository's own `AddDocumentAsync`/`AddChunksAsync` (dogfooding the write path already tested) → calls `SearchSimilarAsync` (the raw-SQL read path under test) → asserts every field of the returned `(LeafletChunk Chunk, double Score)` tuple against the originally-inserted values, including cross-checking `Chunk.Document.Id`/`Filename`/`SourcePath` belongs to the *chunk's own* document (catches a hypothetical bug where the JOIN always resolves to the first document) and that `Chunk.Embedding` is asserted as empty (locks down current, intentional behavior).
- **FR-3 (paged/filter/sort):** Test seeds several `LeafletDocument`s directly (varying `Filename`/`Status`/`ContentType`/`IndexedAt`/`IngestedAt`) via `AddDocumentAsync` → calls `GetDocumentsPagedAsync` with a given filter/sort/paging combination → asserts on `Items` identity (`Id`/`Filename`, not just `Count`) and `Total`. This is the only genuinely new *scenario* being tested (not just a boundary/edge extension), so give it the most attention: each filter test should assert both what's included and what's excluded (e.g. FR-3.1's `"Invoice-Summary.pdf"` must be *absent*, not just `"invoice-report.pdf"` present).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| ~25 new tests × one Postgres container start each meaningfully slows local/manual full-suite runs (each container start is several seconds) | Low | Accepted cost, matches existing per-test-class container lifecycle (NFR-1 in spec); not run in CI per-PR since `Category=Integration` is excluded everywhere. No action needed beyond what the spec already documents. |
| FR-3.1's assumption that Postgres `LIKE` is case-sensitive under this DB's default collation may not hold on every environment (e.g. a `citext`-based or non-`C` collation deployment) | Low-Medium | Spec already flags this as an explicit assumption (Background, FR-3.1). If the assertion fails when first run against the real Testcontainers image, that's a genuine finding (the `pgvector/pgvector:pg16` image default collation) — confirm empirically on first run rather than assuming; do not silently "fix" by loosening the assertion without understanding why. |
| Reviewer/CI illusion of increased coverage: none of this moves the CI-gated 60% threshold, so a future contributor or the coverage-gap routine itself could re-flag this same file next week | Medium | Explicitly call out in the PR description that these are `Category=Integration` tests, deliberately excluded from the coverage run, per the spec's own analysis. Recommend (but do not implement, per Out of Scope) a follow-up ticket to either (a) run `Category=Integration` in a nightly/scheduled workflow with coverage collection, or (b) special-case this file in the coverage-gap routine's threshold config so it isn't re-flagged. This review flags it as a **process gap**, not something FR-1/2/3 should silently work around. |
| New `LeafletDocumentRepositoryPagedTests.cs` schema script drifts from the original `SetupSchemaAsync` over time (two copies to keep in sync) | Low | Acceptable now (only 2 files); if a third Leaflet integration-test file ever needs the same schema, that's the trigger to extract a shared internal helper — not before (YAGNI, per CLAUDE.md's surgical-changes guidance). |
| `AddWithValue`-based parameter binding for 1001-chunk batch could hit an unexpected Npgsql parameter-count ceiling in the *last* small batch (1 row × 7 params) — unlikely, but worth confirming empirically | Low | The code comment at line 13 already reasons about the 65,535-param ceiling for a full 1000-row batch; the 1-row second batch is trivially safe. No mitigation needed beyond running the test once, which FR-1.2 does anyway. |

## Specification Amendments

None required — the spec (`spec.r1.md`) is unusually thorough and already resolved its own open questions (case-sensitivity assumption, `NULLS` ordering default, CI/coverage interaction) by reading the actual code and CI config, which I independently re-verified and found accurate. Two small implementation notes for the developer, not spec changes:

1. Use plain xUnit `Assert.*` (not FluentAssertions) to match the file being extended — see Decision 2. The spec doesn't mandate an assertion library either way, so this is guidance, not a contradiction.
2. For FR-3's `MakeDocument`-equivalent helper in the new file, add optional named parameters for `status`, `contentType`, `indexedAt`, and `ingestedAt` (all defaulting to the same values the original `MakeDocument` uses) rather than constructing `LeafletDocument` inline in every test — keeps the new file consistent with the existing helper-based style.

## Prerequisites

- A container runtime (Docker or Podman) must be available in whatever environment runs these tests — already a prerequisite for the existing integration suite, confirmed by the `TestcontainersSettings.ResourceReaperEnabled = false` Podman accommodation already in the file. No new infrastructure setup needed; this task adds tests to an already-working harness.
- `Testcontainers.PostgreSql` v3.6.0 and `xunit`/`xunit.runner.visualstudio` are already referenced in `Anela.Heblo.Tests.csproj` per the spec — confirmed no new package references are needed.
- Before writing tests, the developer should run the existing `LeafletRepositoryIntegrationTests` locally once (`dotnet test --filter "Category=Integration&FullyQualifiedName~LeafletRepositoryIntegrationTests"`) to confirm the container runtime is actually working in their environment — this is a fast sanity check, not new infrastructure work.
