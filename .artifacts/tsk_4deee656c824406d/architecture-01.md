# Architecture review: KnowledgeBase hooks — design-01.md vs. codebase invariants

## Verdict

**Approve with one required change.** The design's treatment of 9 of the 10 hooks is a
correct, direct application of the established pattern (`useDataQuality.ts`) and every DTO
claim I checked against the generated client matches exactly (line numbers, field names,
optionality, `Date` vs. `string`). One hook — `useSubmitFeedbackMutation` — was routed to the
wrong sanctioned pattern. Its design must change before implementation starts; everything
else can proceed as written.

## What I verified against the running codebase

- All 9 `knowledgeBase_*` generated methods exist with the exact signatures the design cites
  (`api-client.ts:5196-5567`).
- `DocumentSummary`, `GetDocumentsResponse`, `SearchDocumentsResponse`/`ChunkResult`,
  `GetChunkDetailResponse`/`DocumentType`, `AskQuestionResponse`/`SourceReference`,
  `DeleteDocumentResponse`, `RagFeedbackLogSummary`/`RagFeedbackStatsDto`,
  `SubmitFeedbackRequest`, `UploadDocumentResponse2`/`FileParameter` all match the design's
  field-level tables field-for-field, including the optional/`Date` deltas.
- Every consumer call site the design names (`KnowledgeBaseDocumentsTab.tsx:359,363,366-368`,
  `ChunkDetailModal.tsx:65,68`, `KnowledgeBaseSearchAskTab.tsx:148,150`,
  `KnowledgeBaseAskTab.tsx:115,117`, `KnowledgeBaseSearchTab.tsx:110,115`) exists at the cited
  line with the cited expression. `formatDateTime` (`utils/formatters.ts:26`) already accepts
  `string | Date | null | undefined`, confirming the "no change needed" claim for
  `ChunkDetailModal.tsx`.
- `ragFeedbackTypes.ts` (`RagFeedbackLogSummary`/`RagFeedbackStats`) is exactly the target
  shape the mapping function must produce; the generated `RagFeedbackLogSummary` carries an
  extra `feature: RagFeature` field with no local counterpart, confirming it's safe to drop in
  the mapping (every row from `knowledgeBase_GetFeedbackList` is `RagFeature.KnowledgeBase` by
  construction — the enum only disambiguates when both KB and Smartsupp write to the same
  `RagInteractionLogs` table).
- `knowledgeBase_UploadDocument`'s generated body (`api-client.ts:5528-5567`) builds
  `FormData` with field names `"file"`/`"documentType"` and omits the `Content-Type` header —
  byte-for-byte what the current hand-rolled `FormData` does. The `{ data: file, fileName:
  file.name }` wrapper is correct and there's no behavior change to verify beyond that.

## Required change: `useSubmitFeedbackMutation`

The design keeps this hook on the `getApiBaseUrl()` + `getAuthenticatedFetch()` escape hatch,
reasoning that "the 409-as-success contract isn't expressible through the generated method."
That premise is wrong, and the codebase already proves it wrong in a sibling module.

**`docs/development/api-client-generation.md:150-186`** documents a preferred pattern for
exactly this shape of problem — a business outcome surfaced as a non-2xx status — *before*
reaching for the escape hatch:

> The preferred pattern is to model the business outcome in the OpenAPI contract and let the
> generated client surface it as a typed, non-throwing branch... Until the template is
> activated, use a hook-level `try/catch` to handle the typed exception.

The doc's own worked example is a 409 case, and it names the canonical implementation:
`useSubmitArticleFeedbackMutation` in `frontend/src/api/hooks/useArticles.ts:213-244`. That
hook is functionally identical to `useSubmitFeedbackMutation` — same "already submitted"
business outcome, same 409 status — and it calls the **typed** `client.articles_SubmitFeedback(...)`
inside a `try/catch`, checking `(e as { status?: number }).status === 409`:

```ts
try {
  const response = await client.articles_SubmitFeedback(articleId, request);
  return { precisionScore: response.precisionScore ?? null, ... };
} catch (e: unknown) {
  const err = e as { status?: number };
  if (err.status === 409) return { alreadySubmitted: true };
  throw e;
}
```

This works today, with **no backend annotation required**: NSwag's generated
`processKnowledgeBase_SubmitFeedback` (`api-client.ts:5510-5524`) already throws a
`SwaggerException` (`.status: number`, `api-client.ts:43174-43189`) for *any* status other
than 200/204 — the catch-all path isn't gated by `[ProducesResponseType]` annotations, it's
NSwag's default for unlisted codes. The 409 branch is reachable via the typed client right
now.

Further, `getAuthenticatedApiClient()`'s internal `fetch` (`client.ts:296-334`) already has
purpose-built handling for this exact case: it suppresses the global error toast on a
structured 409 response, with a comment that literally reads *"these 409s are typed business
outcomes (e.g. 'feedback already submitted') and the caller's hook handles them."* Routing
`SubmitFeedback` through the raw `getAuthenticatedFetch()` escape hatch instead means:

1. It bypasses code in this codebase written specifically to support this scenario.
2. It knowingly discards the 401-redirect/toast handling — the design documents this as an
   "accepted... intentional behavior delta," but it's an unforced one; the typed-client route
   doesn't have this cost.
3. It diverges from `useArticles.ts`'s already-established precedent for the identical problem
   shape, when the design's own stated goal is codebase consistency.

**Fix:** change `useSubmitFeedbackMutation` to call `apiClient.knowledgeBase_SubmitFeedback(new
SubmitFeedbackRequest(payload))` wrapped in `try/catch`, mirroring
`useSubmitArticleFeedbackMutation` exactly. This removes the *only* remaining manual-fetch
call in the file — the refactor becomes a 100%, no-exceptions swap to the generated client,
which is a strictly better outcome than the design's "9 of 10 fully typed" plan.

One follow-up, non-blocking: `KnowledgeBaseController.SubmitFeedback` (`KnowledgeBaseController.cs:116-123`)
lacks the `[ProducesResponseType(..., StatusCodes.Status409Conflict)]` annotation that
`ArticlesController.SubmitFeedback` (`ArticlesController.cs:71-72`) has. It isn't required for
the fix to function (see above), but it's a one-line addition that brings the OpenAPI spec in
line with actual behavior and matches the sibling controller. Recommend adding it in the same
PR since the file is already being touched; not a hard requirement.

## Everything else in the design holds

- **`GetFeedbackListResponse` internal-mapping approach (finding #8 / component design's
  `toLocalFeedbackListResponse`)** is the right call. Changing `ragFeedbackTypes.ts` to match
  the generated `Date`/`undefined` shape would ripple into the Smartsupp draft-reply module,
  which is explicitly out of scope and tracked as a separate arch-review target. Keeping the
  mapping function unexported and file-local is consistent with how this codebase scopes
  adapter functions (c.f. `useKbFeedbackAdapter.ts`, which already exists as the sanctioned
  boundary between generated RAG DTOs and shared display types — this hook-internal mapping is
  the same pattern one layer earlier, and does not conflict with or duplicate that adapter).
- **`UploadDocumentResponse2` naming collision** — confirmed real (`UploadDocumentResponse` is
  taken by an unrelated `catalogDocuments_*` DTO at `api-client.ts:19486`). Importing under the
  generated name (or a local alias) is correct; there is no existing precedent elsewhere in the
  codebase for a generated-client file upload to compare against, so this is genuinely
  first-of-its-kind for the hooks layer — reason to be a little more careful in review, not a
  reason to change the approach.
- **Local `DocumentType` union kept as UI-scoped restriction over the generated 4-value
  enum** — correct; the generated `knowledgeBase_UploadDocument`'s `documentType` param is a
  plain `string`, so no assignability conflict.
- **Request-class instantiation (`new SearchDocumentsRequest({...})`, `new
  AskQuestionRequest({...})`)** matches the `useDataQuality.ts` template's use of `new
  RunDqtRequest(...)` — consistent with the codebase's established convention for generated
  request bodies.
- **DTOs remain classes, never records** — no violation risk here since these are all
  generated (not new domain types), consistent with the project rule.

## Prerequisites before implementation

None beyond the fix above — no backend change, no codegen regeneration, no new dependency.
The generated client already contains everything both the original design and the corrected
`useSubmitFeedbackMutation` need.

## Risk note for implementation

`SwaggerException`'s `.status` field is accessed via an `unknown` cast (`e as { status?:
number }`), matching `useArticles.ts`'s existing pattern exactly — do not introduce a
different/stronger typing for this hook, since consistency with the sibling hook is the
point. Confirm during implementation that `knowledgeBase_SubmitFeedback`'s 200-path return
value is actually unused by any caller today (current code returns `{}` on success and no
consumer reads fields off it) — if so, the typed call's success branch can just `return {}`
like today, no need to thread `response` fields through.
