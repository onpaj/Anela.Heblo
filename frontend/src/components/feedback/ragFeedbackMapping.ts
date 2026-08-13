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
