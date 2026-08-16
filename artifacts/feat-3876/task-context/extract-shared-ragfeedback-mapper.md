### task: extract-shared-ragfeedback-mapper

`smartsupp_GetDraftReplyFeedbackList` and `knowledgeBase_GetFeedbackList` return the identical generated classes (`RagFeedbackLogSummary`, `RagFeedbackStatsDto` — both back the same `RagInteractionLogs` table, per `ragFeedbackTypes.ts`'s own header comment). `useKnowledgeBase.ts` already contains a `toLocalFeedbackChunk`/`toLocalFeedbackListResponse` mapper that converts the generator's `Date`/`undefined` shapes into `ragFeedbackTypes.ts`'s `string`/`null` shapes. This task extracts that mapper into a shared module and reuses it from both hooks, avoiding two independently-maintained copies of the same conversion logic.

**Files:**
- Create: `frontend/src/components/feedback/ragFeedbackMapping.ts`
- Modify: `frontend/src/api/hooks/useKnowledgeBase.ts`
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts`

#### Step 1: Create `ragFeedbackMapping.ts`

Create `frontend/src/components/feedback/ragFeedbackMapping.ts`:

```ts
import type {
  RagFeedbackLogSummary as GeneratedRagFeedbackLogSummary,
  RagFeedbackStatsDto,
} from '../../api/generated/api-client';
import type {
  RagFeedbackChunk,
  RagFeedbackLogSummary,
  RagFeedbackStats,
} from './ragFeedbackTypes';

// smartsupp_GetDraftReplyFeedbackList and knowledgeBase_GetFeedbackList both return this exact
// generated shape (RagFeedbackLogSummary / RagFeedbackStatsDto) — they back the same
// RagInteractionLogs table, per ragFeedbackTypes.ts's header comment. Typed structurally so both
// generated response classes (GetFeedbackListResponse and GetDraftReplyFeedbackListResponse)
// satisfy it without a cross-import between the two feature hook files.
export interface GeneratedFeedbackListShape {
  logs?: GeneratedRagFeedbackLogSummary[];
  totalCount?: number;
  pageNumber?: number;
  pageSize?: number;
  totalPages?: number;
  stats?: RagFeedbackStatsDto;
}

export interface LocalFeedbackListResponse {
  success: boolean;
  logs: RagFeedbackLogSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  stats: RagFeedbackStats;
}

// Keeps the generated RAG DTOs from leaking into ragFeedbackTypes.ts consumers, which assume
// createdAt: string and null (not undefined) for absent optional fields.
export const toLocalFeedbackChunk = (chunk: {
  chunkId?: string;
  documentId?: string;
  filename?: string;
  score?: number;
  content?: string;
}): RagFeedbackChunk => ({
  chunkId: chunk.chunkId ?? '',
  documentId: chunk.documentId ?? '',
  filename: chunk.filename ?? '',
  score: chunk.score ?? 0,
  content: chunk.content ?? '',
});

export const toLocalFeedbackListResponse = (
  generated: GeneratedFeedbackListShape,
): LocalFeedbackListResponse => ({
  success: true,
  logs: (generated.logs ?? []).map((log) => ({
    id: log.id ?? '',
    question: log.question ?? '',
    answer: log.answer ?? '',
    expandedQuery: log.expandedQuery ?? null,
    systemPrompt: log.systemPrompt ?? '',
    retrievedChunks: (log.retrievedChunks ?? []).map(toLocalFeedbackChunk),
    topK: log.topK ?? 0,
    sourceCount: log.sourceCount ?? 0,
    durationMs: log.durationMs ?? 0,
    createdAt: log.createdAt ? log.createdAt.toISOString() : '',
    userId: log.userId ?? null,
    userName: log.userName ?? null,
    conversationId: log.conversationId ?? null,
    topic: log.topic ?? null,
    sentAnswer: log.sentAnswer ?? null,
    wasEdited: log.wasEdited ?? null,
    sentAt: log.sentAt ? log.sentAt.toISOString() : null,
    precisionScore: log.precisionScore ?? null,
    styleScore: log.styleScore ?? null,
    feedbackComment: log.feedbackComment ?? null,
    hasFeedback: log.hasFeedback ?? false,
  })),
  totalCount: generated.totalCount ?? 0,
  pageNumber: generated.pageNumber ?? 0,
  pageSize: generated.pageSize ?? 0,
  totalPages: generated.totalPages ?? 0,
  stats: {
    totalQuestions: generated.stats?.totalQuestions ?? 0,
    totalWithFeedback: generated.stats?.totalWithFeedback ?? 0,
    avgPrecisionScore: generated.stats?.avgPrecisionScore ?? null,
    avgStyleScore: generated.stats?.avgStyleScore ?? null,
  },
});
```

This is a byte-for-byte copy of `useKnowledgeBase.ts`'s existing mapping logic — no behavior change, just relocated and generalized from the KB-specific `GeneratedGetFeedbackListResponse` type to the shared structural `GeneratedFeedbackListShape`.

#### Step 2: Update `useKnowledgeBase.ts` to use the shared mapper

In `frontend/src/api/hooks/useKnowledgeBase.ts`, replace the import block:

```ts
import {
  SearchDocumentsRequest,
  AskQuestionRequest,
  SubmitFeedbackRequest,
  type ISubmitFeedbackRequest,
  type GetDocumentsResponse,
  type GetDocumentContentTypesResponse,
  type SearchDocumentsResponse,
  type AskQuestionResponse,
  type GetChunkDetailResponse,
  type DeleteDocumentResponse,
  type UploadDocumentResponse2,
  type FileParameter,
  type GetFeedbackListResponse as GeneratedGetFeedbackListResponse,
} from '../generated/api-client';
import type {
  RagFeedbackLogSummary,
  RagFeedbackChunk,
  RagFeedbackStats,
} from '../../components/feedback/ragFeedbackTypes';
```

with:

```ts
import {
  SearchDocumentsRequest,
  AskQuestionRequest,
  SubmitFeedbackRequest,
  type ISubmitFeedbackRequest,
  type GetDocumentsResponse,
  type GetDocumentContentTypesResponse,
  type SearchDocumentsResponse,
  type AskQuestionResponse,
  type GetChunkDetailResponse,
  type DeleteDocumentResponse,
  type UploadDocumentResponse2,
  type FileParameter,
} from '../generated/api-client';
import type {
  RagFeedbackLogSummary,
  RagFeedbackStats,
} from '../../components/feedback/ragFeedbackTypes';
import { toLocalFeedbackListResponse } from '../../components/feedback/ragFeedbackMapping';
```

(`RagFeedbackChunk` is no longer imported directly here — it was only used by the now-deleted local `toLocalFeedbackChunk`.)

Then delete the local mapper functions — replace:

```ts
// ---- Feedback-list mapping ----
// Keeps the generated RAG DTOs from leaking into ragFeedbackTypes.ts consumers, which assume
// createdAt: string and null (not undefined) for absent optional fields.

const toLocalFeedbackChunk = (chunk: {
  chunkId?: string;
  documentId?: string;
  filename?: string;
  score?: number;
  content?: string;
}): RagFeedbackChunk => ({
  chunkId: chunk.chunkId ?? '',
  documentId: chunk.documentId ?? '',
  filename: chunk.filename ?? '',
  score: chunk.score ?? 0,
  content: chunk.content ?? '',
});

const toLocalFeedbackListResponse = (
  generated: GeneratedGetFeedbackListResponse,
): GetFeedbackListResponse => ({
  success: true,
  logs: (generated.logs ?? []).map((log) => ({
    id: log.id ?? '',
    question: log.question ?? '',
    answer: log.answer ?? '',
    expandedQuery: log.expandedQuery ?? null,
    systemPrompt: log.systemPrompt ?? '',
    retrievedChunks: (log.retrievedChunks ?? []).map(toLocalFeedbackChunk),
    topK: log.topK ?? 0,
    sourceCount: log.sourceCount ?? 0,
    durationMs: log.durationMs ?? 0,
    createdAt: log.createdAt ? log.createdAt.toISOString() : '',
    userId: log.userId ?? null,
    userName: log.userName ?? null,
    conversationId: log.conversationId ?? null,
    topic: log.topic ?? null,
    sentAnswer: log.sentAnswer ?? null,
    wasEdited: log.wasEdited ?? null,
    sentAt: log.sentAt ? log.sentAt.toISOString() : null,
    precisionScore: log.precisionScore ?? null,
    styleScore: log.styleScore ?? null,
    feedbackComment: log.feedbackComment ?? null,
    hasFeedback: log.hasFeedback ?? false,
  })),
  totalCount: generated.totalCount ?? 0,
  pageNumber: generated.pageNumber ?? 0,
  pageSize: generated.pageSize ?? 0,
  totalPages: generated.totalPages ?? 0,
  stats: {
    totalQuestions: generated.stats?.totalQuestions ?? 0,
    totalWithFeedback: generated.stats?.totalWithFeedback ?? 0,
    avgPrecisionScore: generated.stats?.avgPrecisionScore ?? null,
    avgStyleScore: generated.stats?.avgStyleScore ?? null,
  },
});
```

with nothing (delete the block entirely — the two functions now come from the import).

Everything else in `useKnowledgeBase.ts` (the `GetFeedbackListResponse`/`FeedbackLogSummary`/`FeedbackStatsDto` local type aliases, `useKnowledgeBaseFeedbackListQuery`'s body calling `toLocalFeedbackListResponse(generated)`) is unchanged — `toLocalFeedbackListResponse`'s return type (`LocalFeedbackListResponse` from the new module) is structurally identical to `useKnowledgeBase.ts`'s own `GetFeedbackListResponse` interface, so it satisfies the existing `queryFn: async (): Promise<GetFeedbackListResponse> => {...}` return-type annotation with no cast.

#### Step 3: Rewrite `useSmartsuppDraftReplyFeedbackListQuery.ts`

Replace `frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts` with:

```ts
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  toLocalFeedbackListResponse,
  type LocalFeedbackListResponse,
} from "../../../feedback/ragFeedbackMapping";

export interface DraftReplyFeedbackListParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  hasFeedback?: boolean;
  userId?: string;
}

export type DraftReplyFeedbackListResponse = LocalFeedbackListResponse;

const QUERY_KEY = ["smartsupp", "draft-reply", "feedback-list"] as const;

export function useSmartsuppDraftReplyFeedbackListQuery(params: DraftReplyFeedbackListParams = {}) {
  return useQuery<DraftReplyFeedbackListResponse>({
    queryKey: [...QUERY_KEY, params],
    queryFn: async () => {
      const generated = await getAuthenticatedApiClient().smartsupp_GetDraftReplyFeedbackList(
        params.pageNumber,
        params.pageSize,
        params.sortBy,
        params.sortDescending,
        params.hasFeedback,
        params.userId,
      );
      return toLocalFeedbackListResponse(generated);
    },
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
}
```

No manual `URLSearchParams` construction, no manual `!response.ok` check (the generated client throws directly on any non-2xx, which is the only status this endpoint's controller annotates besides 200, so there's nothing else to special-case), and the returned shape is unchanged from the caller's point of view (`frontend/src/components/feedback/adapters/useSmartsuppFeedbackAdapter.ts` reads `query.data?.logs`/`.stats`/`.totalCount`/`.totalPages`/`.pageNumber`, all of which are still present with the same field types as before) — that adapter file needs no edits.

#### Step 4: Run the affected tests

```bash
cd frontend
CI=true npx react-scripts test src/api/hooks/__tests__/useKnowledgeBase.test.ts --watchAll=false
npm run build
```

`useKnowledgeBase.test.ts` should keep passing unchanged (it only exercises `useKnowledgeBaseFeedbackListQuery`'s public behavior, not the private mapper functions, confirmed by inspection before this task started). `npm run build` should be clean.

#### Step 5: Commit

```bash
git add frontend/src/components/feedback/ragFeedbackMapping.ts \
  frontend/src/api/hooks/useKnowledgeBase.ts \
  frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts
git commit -m "Extract shared RAG feedback-list mapper and route useSmartsuppDraftReplyFeedbackListQuery through it"
```

---
