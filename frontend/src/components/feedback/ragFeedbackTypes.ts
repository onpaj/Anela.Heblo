// Shared shape of a RAG feedback/eval row, returned by both the KnowledgeBase and Smartsupp
// draft-reply feedback-list endpoints (they back the same RagInteractionLogs table).

export interface RagFeedbackChunk {
  chunkId: string;
  documentId: string;
  filename: string;
  score: number;
  content: string;
}

export interface RagFeedbackLogSummary {
  id: string;
  question: string;
  answer: string;
  expandedQuery: string | null;
  systemPrompt: string;
  retrievedChunks: RagFeedbackChunk[];
  topK: number;
  sourceCount: number;
  durationMs: number;
  createdAt: string;
  userId: string | null;
  userName: string | null;
  // Smartsupp-only (null for KnowledgeBase)
  conversationId: string | null;
  topic: string | null;
  sentAnswer: string | null;
  wasEdited: boolean | null;
  sentAt: string | null;
  // Human feedback
  precisionScore: number | null;
  styleScore: number | null;
  feedbackComment: string | null;
  hasFeedback: boolean;
}

export interface RagFeedbackStats {
  totalQuestions: number;
  totalWithFeedback: number;
  avgPrecisionScore: number | null;
  avgStyleScore: number | null;
}
