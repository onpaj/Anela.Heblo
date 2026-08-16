# Code Review: rebind-adapter-embedding-defaults-to-openai-config-keys

## Summary
The implementation matches the task context exactly: the DI-time embedding option fallback now
binds from `OpenAI:EmbeddingModel`/`OpenAI:EmbeddingDimensions` instead of `KnowledgeBase:*`, with
a comment explaining the fallback's new scope. The test project was extended with the two required
concrete packages and a new binding test class covering all three specified cases. Full test run
of the adapter project passes 16/16, matching the acceptance criteria.

## Review Result: PASS

### task: rebind-adapter-embedding-defaults-to-openai-config-keys
**Status:** PASS

## Docs to Update
(None — this is an internal DI-binding rename with no change to observable behavior, no new
public API, and no docs reference the `KnowledgeBase:EmbeddingModel`/`KnowledgeBase:EmbeddingDimensions`
keys as something operators need to configure.)

## Overall Notes
- Verified the source diff matches the task context's Step 4 snippet verbatim (both the code and
  the added comment).
- Verified the csproj diff matches Step 1 verbatim (added `Microsoft.Extensions.Configuration`
  8.0.0 and `Microsoft.Extensions.DependencyInjection` 8.0.1).
- Verified the new test file matches Step 2 verbatim.
- Verified via `grep -r "KnowledgeBase:Embedding"` that no other source code (only docs/plan
  artifacts, which are historical records, not live config references) still depends on the
  retired keys.
- `dotnet test backend/test/Anela.Heblo.Adapters.OpenAI.Tests/...` reports 16/16 passing, matching
  the task's Step 5 expectation.
