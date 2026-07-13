# Implementation: batch-leaflet-addchunks-insert

## What was implemented
Replaced the per-chunk `NpgsqlCommand` loop in `LeafletDocumentRepository.AddChunksAsync` with a single multi-row parameterised `INSERT`, batched at `MaxRowsPerBatch = 1000` rows to stay comfortably under Npgsql's 65,535-parameter ceiling. An N-chunk document now issues `ceil(N / 1000)` round trips instead of N. Empty input now returns immediately instead of opening a connection for no work.

## Files created/modified
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs` — `AddChunksAsync` rewritten to build one `VALUES (...), (...), ...` INSERT per batch; added `using System.Text;` and the `MaxRowsPerBatch` constant. The exact 7-column list (`Id, DocumentId, ChunkIndex, Content, Summary, WordCount, Embedding`), the `ON CONFLICT ("Id") DO NOTHING` clause, the `Pgvector.Vector` binding, and the inline comment referencing `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md` are all preserved verbatim.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs` — added `AddChunksAsync_PersistsAllRows_WhenMultipleChunks` (5 chunks, asserts every row's Id/ChunkIndex/Content/Summary/WordCount persisted correctly and in order) and `AddChunksAsync_IsNoOp_WhenInputEmpty` (empty enumerable throws nothing and inserts nothing).

## Tests
- `LeafletRepositoryIntegrationTests` (13 tests total, including the 2 new ones) are Testcontainers-based (`pgvector/pgvector:pg16`) and require a Docker daemon.
- **Environment-gated:** this sandbox has no Docker daemon (`docker ps` → "failed to connect to the docker API ... no such file or directory"). All 13 tests in the class — including the 11 pre-existing ones — fail identically at the constructor with `Docker is either not running or misconfigured`, confirming this is an environment limitation, not a regression introduced by this change.
- `dotnet build` succeeded with 0 errors for both `Anela.Heblo.Persistence.csproj` and `Anela.Heblo.Tests.csproj` (only pre-existing, unrelated nullable-reference warnings in other files).
- `dotnet format` on `Anela.Heblo.Persistence.csproj` made no additional changes beyond the hand-written diff.

## How to verify
In an environment with Docker available:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~LeafletRepositoryIntegrationTests"
```
Expect all 13 tests green, including `AddChunksAsync_PersistsAllRows_WhenMultipleChunks` and `AddChunksAsync_IsNoOp_WhenInputEmpty`.

## Notes
- The original per-row implementation was already functionally correct, so this is a pure performance/round-trip-count refactor; the new tests are correctness guards for the rewrite (multi-row batching, empty-input no-op) rather than regression tests for a prior bug.
- `KnowledgeBaseRepository.cs` (which has the identical anti-pattern per the architect's finding) was left untouched — explicitly out of scope for this issue.
- Integration test execution could not be verified in this sandbox due to the missing Docker daemon; CI (which does have Docker) should be treated as the real gate for this class.

## PR Summary
Batches the per-chunk raw-SQL `INSERT` loop in `LeafletDocumentRepository.AddChunksAsync` into a single multi-row parameterised `INSERT` per 1000-row batch, cutting an N-chunk document's DB round trips from N to `ceil(N/1000)` (effectively 1 for any realistic document). The exact 7-column list, `ON CONFLICT DO NOTHING` idempotency, and `Pgvector` embedding binding are preserved unchanged, per the documented gotcha that raw-SQL inserts must stay in sync with the EF mapping. Adds two integration tests covering multi-chunk persistence correctness and empty-input handling.

### Changes
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs` — batched multi-row INSERT rewrite of `AddChunksAsync`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs` — two new integration tests

## Status
DONE_WITH_CONCERNS
