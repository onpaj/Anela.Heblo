# Design: KnowledgeBase hooks — replace raw-fetch escape hatch with generated client calls

No UX/UI section — this is a pure data-layer refactor. No component is added, removed, or
visually changed; every screen renders the same markup from the same (renamed-type) data.

## Component design

### `useKnowledgeBase.ts` — responsibility after the change

Today the file mixes three responsibilities: (1) query-key factory, (2) hand-rolled DTOs +
manual fetch/URL-building, (3) React Query hook wiring. After the change it keeps only (1) and
(3); responsibility (2) moves entirely to the generated client (`../generated/api-client.ts`),
except the one case (`useSubmitFeedbackMutation`) that keeps a manual `fetch` because its
control flow (409 → non-error return) has no generated equivalent.

Shape of each hook after the change, following the `useDataQuality.ts` template
(`frontend/src/api/hooks/useDataQuality.ts:37-53`) — `queryFn`/`mutationFn` becomes a thin
pass-through to one `apiClient.knowledgeBase_*` call, params passed positionally in the
method's declared order:

```ts
export const useKnowledgeBaseDocumentsQuery = (params: GetDocumentsParams = {}) => {
  return useQuery({
    queryKey: knowledgeBaseKeys.documents(params),
    queryFn: (): Promise<GetDocumentsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.knowledgeBase_GetDocuments(
        params.pageNumber, params.pageSize, params.sortBy, params.sortDescending,
        params.filenameFilter, params.statusFilter, params.contentTypeFilter,
      );
    },
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};
```

Per-hook mapping (generated method ↔ existing hook, param order taken from
`api-client.ts:5196-5567`):

| Hook | Generated method | Request shape |
|---|---|---|
| `useKnowledgeBaseDocumentsQuery` | `knowledgeBase_GetDocuments(pageNumber, pageSize, sortBy, sortDescending, filenameFilter, statusFilter, contentTypeFilter)` | positional params, same names as local `GetDocumentsParams` |
| `useKnowledgeBaseContentTypesQuery` | `knowledgeBase_GetDocumentContentTypes()` | none |
| `useKnowledgeBaseSearchMutation` | `knowledgeBase_Search(request: SearchDocumentsRequest)` | `new SearchDocumentsRequest({ query, topK })` |
| `useChunkDetailQuery` | `knowledgeBase_GetChunkDetail(id: string)` | `chunkId!` (call still gated by `enabled: !!chunkId`, matching today) |
| `useKnowledgeBaseAskMutation` | `knowledgeBase_Ask(request: AskQuestionRequest)` | `new AskQuestionRequest({ question, topK })` |
| `useDeleteKnowledgeBaseDocumentMutation` | `knowledgeBase_DeleteDocument(id: string)` | `documentId` |
| `useUploadKnowledgeBaseDocumentMutation` | `knowledgeBase_UploadDocument(file: FileParameter, documentType: string)` | `{ data: file, fileName: file.name }`, `documentType` |
| `useKnowledgeBaseFeedbackListQuery` | `knowledgeBase_GetFeedbackList(pageNumber, pageSize, sortBy, sortDescending, hasFeedback, userId)` | positional params; **response mapped** (see Data schemas) |
| `useSubmitFeedbackMutation` | *(none — manual fetch retained)* | `getApiBaseUrl()` + `getAuthenticatedFetch()`, unchanged URL/body/409 logic |

Two collaborators inside `useKnowledgeBaseFeedbackListQuery`'s `queryFn`:

- The generated call itself (`apiClient.knowledgeBase_GetFeedbackList(...)`) — returns the
  generated `GetFeedbackListResponse` (`logs: RagFeedbackLogSummary[]`, `Date`-typed fields).
- A pure mapping function, `toLocalFeedbackListResponse(generated): GetFeedbackListResponse`
  (local type of the same name), defined in this file, not exported. It exists solely to keep
  the generated `RagFeedbackLogSummary`/`RagFeedbackStatsDto` shapes from leaking into
  `ragFeedbackTypes.ts` consumers (`useKbFeedbackAdapter.ts`, `KnowledgeBaseFeedbackPage.tsx`,
  the Smartsupp draft-reply module) that assume `createdAt: string` and `null` (not
  `undefined`) for absent optional fields. This is the one place in the file that still hand-
  shapes a response, and it is scoped to field renaming/coercion only — no manual HTTP.

`useSubmitFeedbackMutation` after the change:

```ts
export const useSubmitFeedbackMutation = () => {
  return useMutation({
    mutationFn: async (payload: SubmitFeedbackRequest): Promise<SubmitFeedbackResult> => {
      const fetchFn = getAuthenticatedFetch();
      const response = await fetchFn(`${getApiBaseUrl()}/api/knowledgebase/feedback`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(payload),
      });
      if (response.status === 409) return { alreadySubmitted: true };
      if (!response.ok) throw new Error(`Submit feedback failed: ${response.status}`);
      return {};
    },
  });
};
```

`payload: SubmitFeedbackRequest` now refers to the generated class
(`api-client.ts:24798-24844`); the local hand-rolled interface of the same name is deleted.
Field set is identical (`logId`, `precisionScore`, `styleScore`, `comment?`), so no call site
changes.

### Consumer components — boundary changes only

No component gains or loses a prop, a handler, or a piece of state. The only boundary change
is the type each component reads off hook data, and — in three places — a literal expression
that now has to tolerate `undefined`/`Date` instead of a required `string`. Each is a one-line
change at the existing call site, not a restructuring:

- `KnowledgeBaseDocumentsTab.tsx:359` — `StatusBadge status={doc.status}` → `doc.status ?? ''`
  (prop stays `status: string`; `DocumentSummary.status` is now optional).
- `KnowledgeBaseDocumentsTab.tsx:363` — `new Date(doc.createdAt).toLocaleDateString('cs-CZ')` →
  `doc.createdAt?.toLocaleDateString('cs-CZ') ?? '–'` (`createdAt` is already a `Date`;
  double-wrapping in `new Date(...)` is now a type error, not just redundant).
- `KnowledgeBaseDocumentsTab.tsx:366-368` — `doc.indexedAt ? new Date(doc.indexedAt)... : '–'`
  → `doc.indexedAt ? doc.indexedAt.toLocaleDateString('cs-CZ') : '–'`.
- `KnowledgeBaseSearchAskTab.tsx:148,150` and the same two lines in the dead-but-compiled
  `KnowledgeBaseAskTab.tsx:115,117` — `ask.data.answer` → `ask.data.answer ?? ''`,
  `ask.data.sources` → `ask.data.sources ?? []`.
- `KnowledgeBaseSearchTab.tsx:110,115` — `search.data.chunks.length` /
  `search.data.chunks.map(...)` → `(search.data.chunks ?? []).length` /
  `(search.data.chunks ?? []).map(...)`.
- `ChunkDetailModal.tsx` — no logic change; `data.documentType === 'Conversation'` (line 65)
  keeps compiling against the generated `DocumentType` enum, and `formatDateTime(data.indexedAt)`
  (line 68) already accepts `Date | undefined` — only the import source of `GetChunkDetailResponse`
  moves from the hook file's re-export to the generated module (re-export retained, so this may
  need no consumer edit at all).
- `KnowledgeBaseUploadTab.tsx` — no logic change; `DocumentType` stays the local
  `'KnowledgeBase' | 'Conversation'` union (UI-scoped restriction over the generated 4-value
  enum), unaffected by this refactor.

No component's props interface, exported type, or test-facing contract changes.

## Data schemas

All response/request shapes below already exist in
`frontend/src/api/generated/api-client.ts` (confirmed present, ranges cited); this section
documents which ones replace which local interface, and the exact field-level deltas that
drive the consumer edits above.

### Documents list

`GetDocumentsResponse` (`api-client.ts:23938-23993`), replacing the local interface of the
same name:

```
GetDocumentsResponse { documents?: DocumentSummary[]; totalCount?: number; pageNumber?: number;
                        pageSize?: number; totalPages?: number }
DocumentSummary       { id?: string; filename?: string; status?: string; contentType?: string;
                        createdAt?: Date; indexedAt?: Date | undefined;
                        firstChunkId?: string | undefined }
```

Delta vs. local: every field goes from required to optional; `createdAt`/`indexedAt` go from
`string`/`string | null` to `Date`/`Date | undefined`. `success: boolean` (present locally) is
dropped — not on the generated class; no consumer read it (`GetDocumentsResponse.success` is
unused in the codebase per the plan's investigation), so no fallout.

### Content types

`GetDocumentContentTypesResponse { contentTypes?: string[] }` (`api-client.ts:24055-24093`) —
structurally identical to the local type minus the unused `success` field.

### Search

Request: `SearchDocumentsRequest { query!: string; topK?: number }` (`api-client.ts:24197-24235`)
— constructed as `new SearchDocumentsRequest({ query, topK })`.

Response: `SearchDocumentsResponse { chunks?: ChunkResult[]; belowThresholdCount?: number }`
(`api-client.ts:24096-24139`). `ChunkResult` (`api-client.ts:24141-24195`) is field-for-field
identical to the local interface (`chunkId`, `documentId`, `content`, `score`,
`sourceFilename`, `sourcePath`, all required in both) — pure drop-in. `chunks` optionality is
the only delta, handled by the `?? []` fallback above. `belowThresholdCount` is new and unused
by any consumer — no fallout, safe to ignore.

### Chunk detail

`GetChunkDetailResponse` (`api-client.ts:24237-24300`):

```
GetChunkDetailResponse { chunkId?: string; documentId?: string; filename?: string;
                          documentType?: DocumentType; indexedAt?: Date | undefined;
                          chunkIndex?: number; summary?: string; content?: string;
                          sourcePath?: string | undefined }
enum DocumentType { KnowledgeBase = "KnowledgeBase", Conversation = "Conversation",
                     Leaflet = "Leaflet", Article = "Article" }
```

`documentType` moves from a 2-value local string union to the 4-value generated enum;
`data.documentType === 'Conversation'` in `ChunkDetailModal.tsx:65` still type-checks (enum
member compared against its own string literal value). `indexedAt` was already handled as
possibly-null by `formatDateTime` — no change needed there.

### Ask

Request: `AskQuestionRequest { question!: string; topK?: number }` (`api-client.ts:24410-24448`)
— constructed as `new AskQuestionRequest({ question, topK })`.

Response: `AskQuestionResponse { id?: string | undefined; answer?: string;
sources?: SourceReference[] }` (`api-client.ts:24309-24356`). `SourceReference`
(`api-client.ts:24358-24408`) is field-for-field identical to the local interface — drop-in.
Delta: `answer`/`sources` become optional (handled by `?? ''` / `?? []` above); `id` was
already `string | null` locally and is guarded (`ask.data.id && ...`) at the one call site —
`string | undefined` guards the same way, no change needed.

### Delete

`DeleteDocumentResponse` (`api-client.ts:24450-24475`) — empty body beyond the inherited
`BaseResponse` fields (`success`, etc.). No consumer reads fields off the delete response
(only used for its resolution/rejection), so this is a no-op swap.

### Upload

Request: `knowledgeBase_UploadDocument(file: FileParameter | null | undefined, documentType: string | null | undefined)`.
`FileParameter { data: any; fileName: string }` (`api-client.ts:43162-43165`) — the
mutationFn builds `{ data: file, fileName: file.name }` from the incoming browser `File`
(a `File` is a valid `Blob`, satisfying `data: any`).

Response: `UploadDocumentResponse2 { document?: DocumentSummary | undefined }`
(`api-client.ts:24846-24877`) — note the generated name has a numeric suffix because
`UploadDocumentResponse` is already taken by an unrelated `catalogDocuments_*` DTO; import it
under that generated name (or a local alias) rather than reusing the deleted local
`UploadDocumentResponse` name, to avoid a collision with any future re-export.

### Feedback list — mapped, not passed through

This is the one endpoint where the generated response is *not* used directly as the hook's
return type. Generated shape (`api-client.ts:24477-24537`, `24538-24665` for the log row,
`24723-...` for stats):

```
GetFeedbackListResponse { logs?: RagFeedbackLogSummary[]; totalCount?: number;
                           pageNumber?: number; pageSize?: number; totalPages?: number;
                           stats?: RagFeedbackStatsDto }
RagFeedbackLogSummary   { id?: string; feature?: RagFeature; question?: string; answer?: string;
                           expandedQuery?: string | undefined; systemPrompt?: string;
                           retrievedChunks?: RagFeedbackChunkDto[]; topK?: number;
                           sourceCount?: number; durationMs?: number; createdAt?: Date;
                           userId?: string | undefined; userName?: string | undefined;
                           conversationId?: string | undefined; topic?: string | undefined;
                           sentAnswer?: string | undefined; wasEdited?: boolean | undefined;
                           sentAt?: Date | undefined; precisionScore?: number | undefined;
                           styleScore?: number | undefined; feedbackComment?: string | undefined;
                           hasFeedback?: boolean }
RagFeedbackStatsDto     { totalQuestions?: number; totalWithFeedback?: number;
                           avgPrecisionScore?: number | undefined; avgStyleScore?: number | undefined }
```

Target local shape — unchanged, from `frontend/src/components/feedback/ragFeedbackTypes.ts:12-43`
(`RagFeedbackLogSummary`/`RagFeedbackStats`, aliased in the hook file as `FeedbackLogSummary`/
`FeedbackStatsDto`):

```
RagFeedbackLogSummary { id: string; question: string; answer: string; expandedQuery: string | null;
                         systemPrompt: string; retrievedChunks: RagFeedbackChunk[]; topK: number;
                         sourceCount: number; durationMs: number; createdAt: string;
                         userId: string | null; userName: string | null;
                         conversationId: string | null; topic: string | null;
                         sentAnswer: string | null; wasEdited: boolean | null; sentAt: string | null;
                         precisionScore: number | null; styleScore: number | null;
                         feedbackComment: string | null; hasFeedback: boolean }
RagFeedbackStats       { totalQuestions: number; totalWithFeedback: number;
                          avgPrecisionScore: number | null; avgStyleScore: number | null }
```

`toLocalFeedbackListResponse` mapping rules (applied per log row and to `stats`):

- `Date` fields (`createdAt`, `sentAt`) → `.toISOString()`; `sentAt` additionally
  `undefined → null`.
- Every `T | undefined` field whose local counterpart is `T | null`
  (`expandedQuery`, `userId`, `userName`, `conversationId`, `topic`, `sentAnswer`, `wasEdited`,
  `precisionScore`, `styleScore`, `feedbackComment`, `avgPrecisionScore`, `avgStyleScore`) →
  `value ?? null`.
- `retrievedChunks: RagFeedbackChunkDto[] → RagFeedbackChunk[]`: field-for-field identical
  (`chunkId`, `documentId`, `filename`, `score`, `content`), map `?? []` then pass through
  unchanged per item.
- `feature: RagFeature` (generated-only, no local counterpart — `RagFeature.KnowledgeBase` /
  `RagFeature.SmartsuppDraftReply`) is dropped in the mapping; it exists to let the shared
  backend endpoint disambiguate KB vs. Smartsupp rows, and every row returned by
  `knowledgeBase_GetFeedbackList` is `RagFeature.KnowledgeBase` by construction — no
  information loss for this hook's consumers.
- Required-but-optional-in-generated fields with no nullable local counterpart (`id`,
  `question`, `answer`, `systemPrompt`, `topK`, `sourceCount`, `durationMs`, `hasFeedback`,
  `totalQuestions`, `totalWithFeedback`) → `value ?? <zero-value>` (`''` for strings, `0` for
  numbers, `false` for booleans) purely to satisfy the local type's non-null contract; in
  practice the backend always populates these for a log row that made it into the list.

This mapping is intentionally the only place in the file that still hand-shapes a response
body — everything else is a direct pass-through of the generated type.

### `SubmitFeedbackRequest` / `SubmitFeedbackResult`

`SubmitFeedbackRequest` (`api-client.ts:24798-24844`) replaces the local interface of the same
name — identical field set (`logId`, `precisionScore`, `styleScore`, `comment?`), so the
manual-fetch payload type swap is a no-op for callers. `SubmitFeedbackResult` (local,
`{ alreadySubmitted?: true }`) is kept as-is — it's a hook-specific sentinel type, not a
duplicated DTO; the generated `SubmitFeedbackResponse` is not used since the generated method
itself is never called (see Component design).

## Cross-cutting notes carried from the architectural review

- Query keys (`knowledgeBaseKeys.*`), `staleTime`/`gcTime`/`enabled` gating, and
  `onSuccess`-triggered invalidations are unchanged — this is a transport-layer swap, not a
  caching-behavior change.
- `useSubmitFeedbackMutation` loses the global 401-redirect/error-toast side effect that
  `getAuthenticatedApiClient()`'s internal fetch provides (`getAuthenticatedFetch()` is
  documented as not providing it — `frontend/src/api/client.ts:414-427`). This matches
  existing precedent elsewhere in the codebase for the same escape hatch and is accepted as
  the one intentional behavior delta.
