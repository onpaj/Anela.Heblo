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

type SendMessageContext = { previous?: GetConversationResponse; optimisticId?: string };

export function useSendMessage(conversationId: string | null): UseSendMessageResult {
  const queryClient = useQueryClient();

  const mutation = useMutation<SendMessageApiResponse, Error, string, SendMessageContext>({
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

      return data;
    },
    onMutate: async (content) => {
      if (!conversationId) return {};
      await queryClient.cancelQueries({
        queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      });
      const previous = queryClient.getQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      );
      const optimisticId = `optimistic-${Date.now()}`;
      const optimisticMsg: MessageDto = {
        id: optimisticId,
        authorType: "agent",
        content,
        createdAt: new Date().toISOString(),
        isFirstReply: false,
        deliveryStatus: "pending",
      };
      queryClient.setQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        (old) => (old ? { ...old, messages: [...old.messages, optimisticMsg] } : old),
      );
      return { previous, optimisticId };
    },
    onSuccess: (data, _variables, context) => {
      const optimisticId = context?.optimisticId;
      if (!conversationId || !optimisticId) return;
      queryClient.setQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        (current) => {
          if (!current) return current;
          if (!data.messageId) {
            return {
              ...current,
              messages: current.messages.filter((m) => m.id !== optimisticId),
            };
          }
          return {
            ...current,
            messages: current.messages.map((m) =>
              m.id === optimisticId
                ? {
                    ...m,
                    id: data.messageId!,
                    createdAt: data.createdAt ?? m.createdAt,
                    deliveryStatus: "sent",
                  }
                : m,
            ),
          };
        },
      );
    },
    onError: (_err, _content, context) => {
      if (context?.previous !== undefined && conversationId) {
        queryClient.setQueryData(
          SMARTSUPP_QUERY_KEYS.conversation(conversationId),
          context.previous,
        );
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
