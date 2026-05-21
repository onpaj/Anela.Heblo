import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getClientAndBaseUrl, apiPost } from "../../../../api/smartsuppClient";
import {
  SMARTSUPP_QUERY_KEYS,
  type GetConversationResponse,
  type MessageDto,
} from "../../../../api/hooks/useSmartsupp";

interface SendMessageApiResponse {
  success: boolean;
  errorCode?: string;
  messageId?: string;
  createdAt?: string;
}

const SEND_ERROR_MESSAGES: Record<string, string> = {
  SmartsuppSendMessageUnavailable: "Nepodařilo se odeslat zprávu — služba je nedostupná. Zkuste to prosím znovu.",
  SmartsuppConversationNotFound: "Konverzace nebyla nalezena.",
};

function messageForSendError(code?: string): string {
  if (code && SEND_ERROR_MESSAGES[code]) return SEND_ERROR_MESSAGES[code];
  return "Nepodařilo se odeslat zprávu.";
}

interface UseSendMessageResult {
  send: (content: string) => void;
  isPending: boolean;
  error: string | null;
  justSent: boolean;
  clearSent: () => void;
}

type SendMessageContext = { previous?: GetConversationResponse };

export function useSendMessage(conversationId: string | null): UseSendMessageResult {
  const queryClient = useQueryClient();

  const mutation = useMutation<void, Error, string, SendMessageContext>({
    mutationFn: async (content) => {
      if (!conversationId) {
        throw new Error("Není vybrána konverzace.");
      }

      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiPost(
        apiClient,
        `${baseUrl}/api/smartsupp/conversations/${conversationId}/messages`,
        { content },
      );

      if (!response.ok) {
        const errData = await response.json().catch(() => ({})) as Partial<SendMessageApiResponse>;
        throw new Error(messageForSendError(errData?.errorCode));
      }

      const data = (await response.json()) as SendMessageApiResponse;
      if (!data.success) {
        throw new Error(messageForSendError(data?.errorCode));
      }
    },
    onMutate: async (content) => {
      if (!conversationId) return {};
      await queryClient.cancelQueries({
        queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      });
      const previous = queryClient.getQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      );
      const optimisticMsg: MessageDto = {
        id: `optimistic-${Date.now()}`,
        authorType: "agent",
        content,
        createdAt: new Date().toISOString(),
        isFirstReply: false,
      };
      queryClient.setQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        (old) => (old ? { ...old, messages: [...old.messages, optimisticMsg] } : old),
      );
      return { previous };
    },
    onError: (_err, _content, context) => {
      if (context?.previous !== undefined && conversationId) {
        queryClient.setQueryData(
          SMARTSUPP_QUERY_KEYS.conversation(conversationId),
          context.previous,
        );
      }
    },
    onSettled: () => {
      if (conversationId) {
        queryClient.invalidateQueries({
          queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        });
      }
    },
  });

  return {
    send: (content: string) => mutation.mutate(content),
    isPending: mutation.isPending,
    error: mutation.error ? mutation.error.message : null,
    justSent: mutation.isSuccess,
    clearSent: mutation.reset,
  };
}
