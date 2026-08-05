# Review: KnowledgeBase hooks — replace `(apiClient as any).http.fetch` with generated client calls

## Verdict: done

## What I checked

- **Spec conformance.** `grep -n "as any\|\.http\.fetch\|getAuthenticatedFetch\|getApiBaseUrl" frontend/src/api/hooks/useKnowledgeBase.ts` returns nothing — all 18 original occurrences are gone, and no escape hatch remains anywhere in the file, matching the issue's suggested direction (and going one step further than it: even `SubmitFeedback` is fully typed, not routed to the escape hatch).
- **Generated-client signatures.** Cross-checked every call site in the rewritten hook against the actual generated methods in `frontend/src/api/generated/api-client.ts` (`knowledgeBase_GetDocuments`, `_GetDocumentContentTypes`, `_Search`, `_GetChunkDetail`, `_Ask`, `_DeleteDocument`, `_SubmitFeedback`, `_UploadDocument`, `_GetFeedbackList`) — argument order, types, and optionality all match exactly.
- **Architecture review's required fix.** `useSubmitFeedbackMutation` now calls the typed `apiClient.knowledgeBase_SubmitFeedback(new SubmitFeedbackRequest(payload))` inside `try/catch`, checking `(e as { status?: number }).status === 409`. Diffed this against `useSubmitArticleFeedbackMutation` in `useArticles.ts:212-241` (the cited precedent) — the pattern is mirrored line-for-line, as architecture-01.md required.
- **DTO deletion / re-export.** All 11 duplicated local interfaces are gone; the file now imports generated types directly and re-exports `DocumentSummary`/`ChunkResult`/`SourceReference` for consumers. The `ragFeedbackTypes.ts` mapping (`toLocalFeedbackListResponse`/`toLocalFeedbackChunk`) correctly handles the `Date → string`/`undefined → null` deltas the architecture doc flagged, and stays file-local as directed (doesn't leak generated DTOs into the Smartsupp-shared types).
- **Consumer fallout.** Spot-checked all 4 touched components (`KnowledgeBaseDocumentsTab`, `KnowledgeBaseSearchAskTab`, `KnowledgeBaseAskTab`, `KnowledgeBaseSearchTab`) — the `?? ''`/`?? 0`/`?? []`/`.toLocaleDateString()` fallbacks are consistent, correct, and don't change behavior (backend always populates these fields in practice, as noted in development-01.md's deviation log).
- **Deviations from design/architecture docs** (payload typed as `ISubmitFeedbackRequest` interface instead of the `SubmitFeedbackRequest` class; additional optional-field fallout on `ChunkResult`/`SourceReference`/`DocumentSummary.id` not caught by the design doc's field tables) are well-justified, minimal, and consistent with the established pattern elsewhere in the file — not scope creep.

## Verification run in this review (not just trusted from the dev log)

- `CI=true npm run build` → **Compiled successfully**, no new errors/warnings.
- `npm run lint` → 188 pre-existing problems in unrelated files (`financial-overview`, `terminal`, `leaflet-generator`, `contexts` tests); nothing under `knowledge-base` or `useKnowledgeBase`.
- `CI=true npx react-scripts test --watchAll=false --testPathPattern="useKnowledgeBase|knowledge-base|useKbFeedbackAdapter"` → **4 suites, 45 tests, all passed.** Confirmed the rewritten `useKnowledgeBase.test.ts` covers all 9 hooks including both the 409 and non-409 branches of `useSubmitFeedbackMutation` and the feedback-list `Date`/`undefined` mapping.

## Non-blocking

The deferred backend `[ProducesResponseType(..., 409)]` annotation on `KnowledgeBaseController.SubmitFeedback` is correctly scoped out — architecture-01.md marked it optional and confirmed the 409 branch is reachable today without it (NSwag's default catch-all). No objection to leaving it for a follow-up.

No functional requirement, architectural conflict, missing required test, or correctness bug found.

```json
{"outcome": "done", "summary": "Verified: zero `as any`/manual-fetch remain in useKnowledgeBase.ts, all generated-client call sites match actual method signatures, useSubmitFeedbackMutation correctly mirrors the useSubmitArticleFeedbackMutation 409 try/catch precedent per architecture-01.md's required fix, consumer fallout fixes are correct and behavior-preserving, and build/lint/targeted-tests all pass as claimed (re-ran them independently)."}
```
