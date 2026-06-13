`LeafletDocumentStatus.Indexed` exists — plan is type-consistent. Saved to `artifacts/feat-arch-review-leaflet-addchunksasync-raw-s/plan.r1.md`.

**Plan summary:**
- **5 tasks**, ~10 steps total — one-method bug fix, one regression test, one gotcha note, one commit.
- **Task 1** writes a Testcontainers + pgvector integration test (`LeafletRepositoryIntegrationTests`) modeled on the sibling `KnowledgeBaseRepositoryIntegrationTests` and confirms it fails RED against current code.
- **Task 2** applies the three-edit fix to `LeafletDocumentRepository.AddChunksAsync`: add `"Summary"` to the INSERT column list, `@summary` to VALUES, and the parameter binding — mirroring the working `KnowledgeBaseRepository.AddChunksAsync`. Includes the non-optional comment per arch-review NFR-2 amendment.
- **Task 3** creates `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md` documenting the rule and naming both raw-SQL repositories as future-touch hotspots.
- **Task 4** validates `dotnet format` + `dotnet build` + the affected test classes + a sibling-suite sanity check.
- **Task 5** commits the three touched paths with a descriptive conventional-commit message.

The plan explicitly fences out backfill, reflection guardrails, and migration of the existing skipped tests per arch-review's Out-of-scope clarifications.