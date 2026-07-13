# Code Review: Batch embedding calls in KnowledgeBaseDocIndexingStrategy (whole-branch)

## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes
Reviewed the full branch diff against merge-base with `main` (2 files, 48 insertions / 6 deletions, scoped entirely to `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs` and its test file). The change replaces N per-chunk `_embeddingGenerator.GenerateAsync` calls with a single batched call collected after sequential summarization, matching the pre-existing, already-accepted pattern in the sibling `ConversationIndexingStrategy` (same "batched result preserves input order" assumption, not a new risk introduced here). A zero-chunk guard prevents an empty-batch call. Index alignment between `chunkTexts[i]`/`summaries[i]`/`embeddings[i]`/`chunks[i]` is preserved. The public method signature, `Supports`, and constructor are untouched. Test changes are proportionate: one assertion tightened from `Times.AtLeastOnce` to `Times.Exactly(1)`, one test's mock fixed to return one embedding per input (the pre-existing mock always returned exactly one embedding regardless of batch size, which would have broken under batching), and one new test added for the zero-chunk guard. No unrelated files touched, no scope creep, no dead code or duplicated logic introduced.
