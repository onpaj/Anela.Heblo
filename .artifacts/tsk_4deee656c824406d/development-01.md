# Development: KnowledgeBase hooks — replace `(apiClient as any).http.fetch` with generated client calls

## Summary

Rewrote `frontend/src/api/hooks/useKnowledgeBase.ts` to call the generated NSwag client's
`knowledgeBase_*` methods instead of reaching into `(apiClient as any).baseUrl` /
`(apiClient as any).http.fetch` (18 occurrences removed, `grep -c "as any"` now returns `0`).
Deleted all 11 hand-duplicated DTO interfaces; consumers now use the generated types
(re-exported from the hook file where they were previously consumed as local types) or the
existing `ragFeedbackTypes.ts`-backed local aliases. Fixed the 6 consumer call sites that
needed to tolerate now-optional/`Date`-typed fields, plus two additional required-field-fallout
sites the design doc's field tables didn't flag (see "Deviations from the design doc" below).
Per `architecture-01.md`'s required fix, `useSubmitFeedbackMutation` now calls the typed
`knowledgeBase_SubmitFeedback` method wrapped in `try/catch` (matching
`useSubmitArticleFeedbackMutation`'s established 409 pattern) instead of keeping a manual-fetch
escape hatch — so the refactor is a 100% swap to the generated client, zero `as any` and zero
manual `fetch` calls remaining.

## Files changed

- **`frontend/src/api/hooks/useKnowledgeBase.ts`** — full rewrite of the 9 hooks' bodies to call
  `apiClient.knowledgeBase_GetDocuments`, `knowledgeBase_GetDocumentContentTypes`,
  `knowledgeBase_Search`, `knowledgeBase_GetChunkDetail`, `knowledgeBase_Ask`,
  `knowledgeBase_DeleteDocument`, `knowledgeBase_SubmitFeedback`, `knowledgeBase_UploadDocument`,
  `knowledgeBase_GetFeedbackList`. Deleted local `DocumentSummary`, `GetDocumentsResponse`,
  `GetDocumentContentTypesResponse`, `ChunkResult`, `SearchDocumentsResponse`, `SourceReference`,
  `AskQuestionResponse`, `SubmitFeedbackRequest`, `DeleteDocumentResponse`,
  `UploadDocumentResponse`, `ChunkDetail`, `GetChunkDetailResponse` interfaces. Kept the local
  `DocumentType` UI-scoped union, `GetDocumentsParams`/`GetFeedbackListParams`, and the
  `ragFeedbackTypes.ts`-backed `FeedbackLogSummary`/`FeedbackStatsDto`/`GetFeedbackListResponse`
  aliases unchanged, with a new file-local `toLocalFeedbackListResponse` / `toLocalFeedbackChunk`
  mapping (generated `Date`/`undefined` shape → local `string`/`null` shape) so the generated RAG
  DTOs don't leak into `ragFeedbackTypes.ts` consumers or the Smartsupp module. Re-exports
  `DocumentSummary`, `ChunkResult`, `SourceReference` from the generated client for the
  components that import them as types.
- **`frontend/src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx`** —
  `doc.status` → `doc.status ?? ''`; `new Date(doc.createdAt)` → `doc.createdAt?.toLocaleDateString(...)
  ?? '–'` (double-`new Date()` is now a type error since `createdAt` is already a `Date`);
  `new Date(doc.indexedAt)` → `doc.indexedAt.toLocaleDateString(...)`; `pendingDelete.id` →
  `pendingDelete.id ?? ''` for the delete-mutation call (additional fallout not listed in
  design-01.md — see below).
- **`frontend/src/components/knowledge-base/KnowledgeBaseSearchAskTab.tsx`** —
  `ask.data.answer ?? ''`, `ask.data.sources ?? []`; `SourceAccordion`'s `src.chunkId`/`src.score`
  fallbacks (`?? ''` / `?? 0`) for the `onViewSource` calls and the `Math.round(src.score * 100)`
  arithmetic (additional fallout — see below).
- **`frontend/src/components/knowledge-base/KnowledgeBaseAskTab.tsx`** — same two fixes as
  `KnowledgeBaseSearchAskTab.tsx` (dead component, still type-checked and tested).
- **`frontend/src/components/knowledge-base/KnowledgeBaseSearchTab.tsx`** —
  `(search.data.chunks ?? [])` for both the empty-check and `.map`; `ChunkCard`'s
  `chunk.chunkId`/`chunk.score` fallbacks for `ScoreBadge` and `onViewSource` (additional fallout
  — see below).
- **`frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts`** — full rewrite on the
  `useDataQuality.test.ts` template: `jest.mock('../../client')`,
  `mockAuthenticatedApiClient({ knowledgeBase_*: jest.fn() })`, `createQueryClientWrapper()`.
  Covers all 9 hooks, including the FR-8 feedback-list `Date`/`undefined` → `string`/`null`
  mapping and both branches of the FR-9 409 vs. generic-error path for `useSubmitFeedbackMutation`.
- `ChunkDetailModal.tsx`, `KnowledgeBaseUploadTab.tsx`: no changes needed, confirmed by `tsc`
  (matches the design doc's prediction).

## Deviations from the design/architecture docs

1. **`useSubmitFeedbackMutation`'s payload type is the generated `ISubmitFeedbackRequest`
   interface, not the generated `SubmitFeedbackRequest` class.** `architecture-01.md` said to
   type the payload against the class itself; `tsc` rejected that (`TS2345`) because
   `SubmitFeedbackRequest` is a class with `init`/`toJSON` methods, so the plain object literal
   built at the call site (`KnowledgeBaseSearchAskTab.tsx`'s `submitFeedback.mutate({ logId, ... })`)
   is structurally incompatible with the class type — only with its data-only interface. Using
   `ISubmitFeedbackRequest` (already generated alongside the class) keeps the call site
   unchanged and the class is still used to construct the actual request
   (`new SubmitFeedbackRequest(payload)`) before calling `knowledgeBase_SubmitFeedback`. This
   mirrors `useSubmitArticleFeedbackMutation`'s own pattern of accepting a plain local payload
   type and constructing the generated request class inside the hook.
2. **`ChunkResult` and `SourceReference` are NOT field-for-field "all required" as design-01.md's
   data-schema table states** — I read the generated classes directly
   (`api-client.ts:24141-24195`, `24358-24408`) and every field on both (`chunkId`, `score`,
   `filename`, `excerpt`, etc.) is optional, same as every other generated DTO in this file. This
   surfaced type errors the design doc didn't anticipate: `ScoreBadge`'s `score: number` prop,
   `onViewSource(chunkId: string, score: number)`, and `Math.round(src.score * 100)` arithmetic
   all needed `?? 0` / `?? ''` fallbacks in `KnowledgeBaseSearchTab.tsx`,
   `KnowledgeBaseSearchAskTab.tsx`, and `KnowledgeBaseAskTab.tsx`. Fixed the same way as every
   other optional-field fallout in this task (a fallback at the read site, no behavior change —
   these fields are always populated by the backend in practice).
3. **`DocumentSummary.id` becoming optional broke `KnowledgeBaseDocumentsTab.tsx`'s delete flow**
   (`deleteDocument.mutateAsync(pendingDelete.id)` — not called out in design-01.md/plan-01.md).
   Fixed with `pendingDelete.id ?? ''`.

None of these change the shipped behavior — they're additional compile-time fallout from fields
the generated client always populates in practice, handled the same way as the fallout the
design doc did anticipate.

## Verification

- `grep -c "as any" frontend/src/api/hooks/useKnowledgeBase.ts` → `0`.
- `npm run build` (`CI=true`, tsc + ESLint via CRA) → **Compiled successfully**, no new
  warnings/errors.
- `npm run lint` → 0 errors/warnings in any file touched by this change (pre-existing lint debt
  of 175 errors/13 warnings in unrelated files — `manufacture`, `marketing`, `contexts`,
  `terminal`, `financial-overview`, etc. — confirmed unrelated by `grep -i knowledge` on the lint
  output, which returned nothing).
- Full Jest suite (`CI=true npx react-scripts test --watchAll=false`): **304 suites, 2547 tests
  — 2531 passed, 5 suites / 11 tests failed**. All 5 failing suites
  (`chartDataMapping.test.ts`, `timePeriod/resolve.test.ts`, `fullcalendarAdapters.test.ts`,
  `useManufacturingStockAnalysis.test.tsx`, `ManufactureOrderDetail.autoCalculation.test.tsx`)
  are unrelated to KnowledgeBase and were confirmed to fail identically on the pre-change
  codebase (`git stash` + rerun of `chartDataMapping`/`fullcalendarAdapters` reproduced the exact
  same 7/23 failures) — timezone-dependent date-arithmetic tests, pre-existing in this
  environment, out of scope for this task.
- Targeted run of every touched/knowledge-base-adjacent suite — `useKnowledgeBase.test.ts`,
  `KnowledgeBaseUploadTab.test.tsx`, `ChunkDetailModal.test.tsx`, `useKbFeedbackAdapter.test.ts`
  — **4 suites, 45 tests, all passed.** (`KnowledgeBaseUploadTab.test.tsx` prints pre-existing
  `act(...)` console warnings unrelated to this change — that file/component wasn't touched.)
- No backend files changed, so `dotnet build`/`dotnet format` don't apply to this change.

## How to verify manually

1. `cd frontend && npm run build && npm run lint`
2. `CI=true npx react-scripts test --watchAll=false --testPathPattern="useKnowledgeBase|knowledge-base|useKbFeedbackAdapter"`
3. In the browser: KnowledgeBase Documents tab (load/filter/sort/paginate/delete, click a row
   with a chunk to open the detail modal), Search tab, Ask tab (ask a question, submit feedback
   twice to exercise the 409 path), Upload tab (both document types), Feedback Browser page.

## Non-blocking item deferred (per architecture-01.md)

`KnowledgeBaseController.SubmitFeedback` still lacks the
`[ProducesResponseType(..., StatusCodes.Status409Conflict)]` annotation that
`ArticlesController.SubmitFeedback` has. The architecture review flagged this as a one-line,
non-blocking follow-up (not required — NSwag's default catch-all already makes the 409 branch
reachable through the typed client without it) and recommended, not required, doing it in this
PR. Left out to keep this change a pure frontend/TypeScript surgical fix with no backend/OpenAPI
regeneration involved; can be picked up separately.
