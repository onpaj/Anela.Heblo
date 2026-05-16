import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";

export interface DraftReplySource {
  documentId: string;
  filename: string;
  excerpt: string;
  score: number;
}

export interface DraftReplyResult {
  answer: string;
  sources: DraftReplySource[];
}

interface GenerateDraftReplyApiResponse {
  success: boolean;
  errorCode?: string;
  answer?: string;
  sources?: DraftReplySource[];
}

const ERROR_MESSAGES: Record<string, string> = {
  SmartsuppDraftReplyAiUnavailable:
    "AI služba je momentálně nedostupná. Zkuste to prosím znovu.",
  SmartsuppConversationEmpty: "Konverzace neobsahuje zprávu zákazníka.",
  SmartsuppConversationNotFound: "Konverzace nebyla nalezena.",
};

function messageForError(code?: string): string {
  if (code && ERROR_MESSAGES[code]) {
    return ERROR_MESSAGES[code];
  }
  return "Nepodařilo se vygenerovat odpověď.";
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

      const apiClient = getAuthenticatedApiClient();
      const baseUrl = (apiClient as unknown as { baseUrl: string }).baseUrl;
      const http = (apiClient as unknown as {
        http: { fetch: (url: string, init: RequestInit) => Promise<Response> };
      }).http;

      const response = await http.fetch(
        `${baseUrl}/api/smartsupp/conversations/${conversationId}/draft-reply`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ topic: topic ?? null }),
        },
      );

      const data = (await response.json()) as GenerateDraftReplyApiResponse;
      if (!response.ok || !data.success) {
        throw new Error(messageForError(data?.errorCode));
      }

      return { answer: data.answer ?? "", sources: data.sources ?? [] };
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
