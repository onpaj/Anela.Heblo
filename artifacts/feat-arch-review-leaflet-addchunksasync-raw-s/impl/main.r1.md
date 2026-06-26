All changes are committed. Implementation is complete. Here's a summary of what was done:

---

## Implementation: Persist Summary Column in LeafletChunk Raw SQL Insert

### What was implemented

Fixed `LeafletDocumentRepository.AddChunksAsync` to include the `"Summary"` column in its raw SQL INSERT — which was silently discarding LLM-generated summaries on every ingestion. Also fixed `SearchSimilarAsync` to SELECT and project `Summary` so the read path returns correct data.

### Files created/modified

- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs` — added `"Summary"` to INSERT column list and VALUES, bound `@summary` parameter, fixed `SearchSimilarAsync` SELECT to include `c."Summary"` with corrected ordinals, added warning comment referencing the gotcha doc
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs` — new integration test class using Testcontainers + `pgvector/pgvector:pg16`, mirroring `KnowledgeBaseRepositoryIntegrationTests`; 10 tests including `AddChunksAsync_PersistsSummary` and `SearchSimilarAsync_ReturnsChunkWithSummary`
- `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md` — new gotcha doc listing both raw-SQL repositories as future hotspots, recovery path, and prevention checklist

### Tests

- `AddChunksAsync_PersistsSummary` — proves Summary round-trips through INSERT → EF read
- `SearchSimilarAsync_ReturnsChunkWithSummary` — proves Summary survives the vector search SELECT path
- 8 additional integration tests covering idempotency, cascade delete, retrieval by hash/graphItemId

### How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Category!=Integration"  # passes: 4846
# Integration tests (require Docker with pgvector):
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Category=Integration"
```

---

**Ready to ship. What would you like to do?**

1. **Push and create a Pull Request** — push `feat-arch-review-leaflet-addchunksasync-raw-s` to origin and open a PR against `main`
2. **Merge back to `main` locally** — checkout main, merge, run tests on merged result
3. **Keep the branch as-is** — I'll handle it later
4. **Discard this work**