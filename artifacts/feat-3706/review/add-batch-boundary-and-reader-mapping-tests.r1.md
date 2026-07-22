# Code Review: add-batch-boundary-and-reader-mapping-tests

## Summary
The implementation adds exactly the five specified `[Fact]` tests to the existing `LeafletRepositoryIntegrationTests.cs`, verbatim to the task-context's prescribed code, with zero production-code changes (189 insertions, 1 file). I traced each new assertion against the actual `AddChunksAsync` batching loop and `SearchSimilarAsync` reader-ordinal mapping in `LeafletDocumentRepository.cs` and found every assumption correct.

## Review Result: PASS

### task: add-batch-boundary-and-reader-mapping-tests
**Status:** PASS

## Docs to Update
(none)

## Overall Notes
- **Batch-boundary correctness (FR-1):** `AddChunksAsync`'s loop is `for (var offset = 0; offset < chunkList.Count; offset += MaxRowsPerBatch)` with `MaxRowsPerBatch = 1000` and `batch = chunkList.Skip(offset).Take(MaxRowsPerBatch)`. For 1000 chunks this produces exactly one batch (loop exits since `1000 < 1000` is false after the first iteration); for 1001 chunks it produces two batches (indices 0-999, then the single row at index 1000). The two new tests (`AddChunksAsync_PersistsAll_AtExactBatchBoundary`, `AddChunksAsync_PersistsAll_AcrossTwoBatches`) match this behavior precisely, and their first/last-row field assertions (`ChunkIndex`, `Content`, `Summary`, `WordCount`) are consistent with the column list in the raw INSERT (`"Id", "DocumentId", "ChunkIndex", "Content", "Summary", "WordCount", "Embedding"`).
- **Reader-mapping correctness (FR-2):** `SearchSimilarAsync`'s `SELECT` list is `c."Id", c."DocumentId", c."ChunkIndex", c."Content", c."Summary", c."WordCount", d."Filename", d."SourcePath", ... AS "Score"`, and the reader code pulls ordinals 0-8 accordingly (`GetGuid(0)`=Id, `GetGuid(1)`=DocumentId, `GetInt32(2)`=ChunkIndex, `GetString(3)`=Content, `GetString(4)`=Summary, `GetInt32(5)`=WordCount, `GetString(6)`=Filename, `GetString(7)`=SourcePath, `GetDouble(8)`=Score), with `Embedding` hard-coded to `[]`. The new `SearchSimilarAsync_MapsAllReaderColumns_AcrossTwoDocuments` test's assertions on `Chunk.Document.Filename`/`SourcePath` and `Assert.Empty(top.Chunk.Embedding)` correctly reflect this. The `TopKLimitsResultCount` and `ReturnsEmptyList_WhenNoChunks` tests are simple and correctly reasoned against the `LIMIT @topK` clause and the zero-iteration reader loop.
- **Score-ordering assumption:** with `Score = 1 - cosineDistance` and orthogonal vs. identical query/embedding vectors used in the mapping test, `results[0].Score > results[1].Score` holds given the `ORDER BY … <=> …` ascending-distance sort — verified against the SQL text.
- **Architecture adherence:** tests were added in-place to the existing file (not a new sibling file), matching arch-review Decision 1, since FR-1/FR-2 extend already-covered methods (the sibling-file guidance was specifically for the out-of-scope `GetDocumentsPagedAsync`/FR-3 work). Plain `Assert.*` is used throughout, matching Decision 2 and the file's existing convention. The existing `MakeDocument` helper and container/schema fixture are reused as-is, with no changes to shared fixture code.
- **Sandbox limitation:** as instructed, the inability to execute the tests against a live Postgres/Testcontainers instance in this sandbox (no Docker daemon) is not treated as a defect; the developer's `DONE_WITH_CONCERNS` status and documented manual-verification approach are appropriate given the constraint.
