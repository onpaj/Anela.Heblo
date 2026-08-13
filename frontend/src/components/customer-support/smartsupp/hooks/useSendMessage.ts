import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  ErrorCodes,
  SendMessageBody,
  GetConversationResponse,
  MessageDto,
  type SendMessageResponse,
} from "../../../../api/generated/api-client";
import { SMARTSUPP_QUERY_KEYS } from "../../../../api/hooks/useSmartsupp";

const SEND_ERROR_MESSAGES: Partial<Record<ErrorCodes, string>> = {
  [ErrorCodes.SmartsuppSendMessageUnavailable]:
    "Nepodařilo se odeslat zprávu — služba je nedostupná. Zkuste to prosím znovu.",
  [ErrorCodes.SmartsuppConversationNotFound]: "Konverzace nebyla nalezena.",
};

function messageForSendError(code?: ErrorCodes): string {
  if (code && SEND_ERROR_MESSAGES[code]) return SEND_ERROR_MESSAGES[code]!;
  return "Nepodařilo se odeslat zprávu.";
}

interface SendMessageVariables {
  content: string;
  /** Set when the message was composed from an AI draft, to link the sent text to that draft's log. */
  draftLogId?: string | null;
}

interface UseSendMessageResult {
  send: (content: string, draftLogId?: string | null) => void;
  isPending: boolean;
  error: string | null;
  justSent: boolean;
  clearSent: () => void;
}

type SendMessageContext = { previous?: GetConversationResponse; optimisticId?: string };

export function useSendMessage(conversationId: string | null): UseSendMessageResult {
  const queryClient = useQueryClient();

  const mutation = useMutation<SendMessageResponse, Error, SendMessageVariables, SendMessageContext>({
    mutationFn: async ({ content, draftLogId }) => {
      if (!conversationId) {
        throw new Error("Není vybrána konverzace.");
      }

      let data: SendMessageResponse;
      try {
        data = await getAuthenticatedApiClient().smartsupp_SendMessage(
          conversationId,
          new SendMessageBody({ content, draftLogId: draftLogId ?? undefined }),
        );
      } catch (e: unknown) {
        const errorCode = (e as { errorCode?: string }).errorCode as ErrorCodes | undefined;
        throw new Error(messageForSendError(errorCode));
      }
      if (!data.success) {
        throw new Error(messageForSendError(data.errorCode));
      }

      return data;
    },
    onMutate: async ({ content }) => {
      if (!conversationId) return {};
      await queryClient.cancelQueries({
        queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      });
      const previous = queryClient.getQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      );
      const optimisticId = `optimistic-${Date.now()}`;
      const optimisticMsg = new MessageDto({
        id: optimisticId,
        authorType: "agent",
        content,
        createdAt: new Date(),
        isFirstReply: false,
        deliveryStatus: "pending",
      });
      queryClient.setQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
        (old) =>
          old
            ? GetConversationResponse.fromJS({
                ...old,
                messages: [...(old.messages ?? []), optimisticMsg],
              })
            : old,
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
            return GetConversationResponse.fromJS({
              ...current,
              messages: (current.messages ?? []).filter((m) => m.id !== optimisticId),
            });
          }
          return GetConversationResponse.fromJS({
            ...current,
            messages: (current.messages ?? []).map((m) =>
              m.id === optimisticId
                ? new MessageDto({
                    ...m,
                    id: data.messageId!,
                    createdAt: data.createdAt ?? m.createdAt,
                    deliveryStatus: "sent",
                  })
                : m,
            ),
          });
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
    send: (content: string, draftLogId?: string | null) =>
      mutation.mutate({ content, draftLogId }),
    isPending: mutation.isPending,
    error: mutation.error ? mutation.error.message : null,
    justSent: mutation.isSuccess,
    clearSent: mutation.reset,
  };
}
