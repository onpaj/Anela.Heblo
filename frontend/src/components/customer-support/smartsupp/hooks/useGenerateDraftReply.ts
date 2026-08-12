import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  ErrorCodes,
  GenerateDraftReplyBody,
  type GenerateDraftReplyResponse,
} from "../../../../api/generated/api-client";

export interface DraftReplySource {
  chunkId: string;
  documentId: string;
  filename: string;
  excerpt: string;
  score: number;
}

export interface DraftReplyResult {
  id: string | null;
  answer: string;
  sources: DraftReplySource[];
}

const ERROR_MESSAGES: Partial<Record<ErrorCodes, string>> = {
  [ErrorCodes.SmartsuppDraftReplyAiUnavailable]:
    "AI služba je momentálně nedostupná. Zkuste to prosím znovu.",
  [ErrorCodes.SmartsuppConversationEmpty]: "Konverzace neobsahuje zprávu zákazníka.",
  [ErrorCodes.SmartsuppConversationNotFound]: "Konverzace nebyla nalezena.",
};

function messageForError(code?: ErrorCodes): string {
  if (code && ERROR_MESSAGES[code]) {
    return ERROR_MESSAGES[code]!;
  }
  return "Nepodařilo se vygenerovat odpověď.";
}

function toDraftReplyResult(data: GenerateDraftReplyResponse): DraftReplyResult {
  return {
    id: data.id ?? null,
    answer: data.answer ?? "",
    sources: (data.sources ?? []).map((s) => ({
      chunkId: s.chunkId ?? "",
      documentId: s.documentId ?? "",
      filename: s.filename ?? "",
      excerpt: s.excerpt ?? "",
      score: s.score ?? 0,
    })),
  };
}

interface UseGenerateDraftReplyResult {
  generate: (topic?: string) => void;
  isLoading: boolean;
  error: string | null;
  result: DraftReplyResult | null;
  reset: () => void;
}

export function useGenerateDraftReply(
  conversationId: string | null,
): UseGenerateDraftReplyResult {
  const mutation = useMutation<DraftReplyResult, Error, string | undefined>({
    mutationFn: async (topic) => {
      if (!conversationId) {
        throw new Error("Není vybrána konverzace.");
      }

      let data: GenerateDraftReplyResponse;
      try {
        data = await getAuthenticatedApiClient().smartsupp_GenerateDraftReply(
          conversationId,
          new GenerateDraftReplyBody({ topic: topic ?? undefined }),
        );
      } catch {
        // 400/404/503 are all untyped ProducesResponseType on this controller action, so the
        // generated client throws without a usable errorCode here.
        throw new Error(messageForError(undefined));
      }
      if (!data.success) {
        throw new Error(messageForError(data.errorCode));
      }

      return toDraftReplyResult(data);
    },
  });

  return {
    generate: (topic?: string) => mutation.mutate(topic),
    isLoading: mutation.isPending,
    error: mutation.error ? mutation.error.message : null,
    result: mutation.data ?? null,
    reset: mutation.reset,
  };
}
