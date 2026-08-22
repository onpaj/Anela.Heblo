# Code Review: add-per-model-embedding-client-cache

## Summary
The implementation replaces the single `Lazy<EmbeddingClient>` field with a
`ConcurrentDictionary<string, Lazy<EmbeddingClient>>` keyed by resolved model id, resolving the
model per-call as `options?.ModelId ?? _options.EmbeddingModel`, exactly as FR-1 and NFR-1 require.
The injected-client test seam is preserved unchanged (seeded under `_options.EmbeddingModel`), and
a new `clientFactory` seam supports the added override-path tests without hitting the real OpenAI
endpoint. All 13 tests (7 pre-existing + 4 new) pass; solution builds clean; `dotnet format
--verify-no-changes` is clean.

## Review Result: PASS

### task: add-per-model-embedding-client-cache
**Status:** PASS

Verified against spec.r1.md FR-1/NFR-1 acceptance criteria:
- Override `ModelId` routes the HTTP call to that model (`GenerateAsync_ModelIdOverride_UsesOverriddenModel`) — met.
- No-override calls continue to use `_options.EmbeddingModel` (`GenerateAsync_NoModelIdOverride_UsesConfiguredModel`) — met.
- Same overridden `ModelId` twice reuses one cached client, i.e. constructed once (`GenerateAsync_SameModelIdTwice_ConstructsClientOnce` — 2 HTTP calls, 1 factory invocation) — met.
- Different `ModelId`s resolve independently (`GenerateAsync_DifferentModelIds_ResolveIndependently`) — met.
- All 7 pre-existing tests pass unmodified — met (13/13 passing).
- Construction is `Lazy<>`-guarded per key, so `ConcurrentDictionary.GetOrAdd`'s possible
  multiple-invocation-under-race behavior cannot construct more than one `EmbeddingClient` per
  model — correctly matches the single-construction guarantee the spec calls for.
- No eviction / unbounded growth concern: keys are sourced only from operator config
  (`RagFeatureOptions.EmbeddingModel`), not user input — consistent with NFR-1's intent and
  reasonably documented in an inline comment.
- Deferred construction (no client built at DI time) is preserved — `_clientFactory` is only
  invoked lazily inside `GenerateAsync`.

No correctness bugs found. Diff is minimal and scoped to exactly the described change (2 files,
+132/-4).

## Docs to Update
(none — this is an internal adapter implementation detail with no public API, CLI, or config surface change; `docs/integrations/` covers Shoptet only, not applicable here)

## Overall Notes
Implementation matches the task-context's prescribed code verbatim, with no deviations. Clean, well-commented, and fully test-covered.
