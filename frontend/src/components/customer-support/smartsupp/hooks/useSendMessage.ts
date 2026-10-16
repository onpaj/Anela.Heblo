import { useEffect, useRef } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  ErrorCodes,
  SendMessageBody,
  GetConversationResponse,
  MessageAttachmentDto,
  MessageDto,
  SendMessageResponse,
} from "../../../../api/generated/api-client";
import { SMARTSUPP_QUERY_KEYS } from "../../../../api/hooks/useSmartsupp";
import { getErrorMessage } from "../../../../utils/errorHandler";
import { mimeTypeForFileName } from "../utils/attachmentPolicy";

const SEND_ERROR_MESSAGES: Partial<Record<ErrorCodes, string>> = {
  [ErrorCodes.SmartsuppSendMessageUnavailable]:
    "Nepodařilo se odeslat zprávu — služba je nedostupná. Zkuste to prosím znovu.",
  [ErrorCodes.SmartsuppConversationNotFound]: "Konverzace nebyla nalezena.",
};

const ATTACHMENT_ERROR_CODES: readonly ErrorCodes[] = [
  ErrorCodes.SmartsuppAttachmentInvalid,
  ErrorCodes.SmartsuppAttachmentTooLarge,
  ErrorCodes.SmartsuppAttachmentUploadFailed,
];

const GENERIC_SEND_ERROR = "Nepodařilo se odeslat zprávu.";

function messageForSendError(code?: ErrorCodes): string {
  if (code && ATTACHMENT_ERROR_CODES.includes(code)) return getErrorMessage(code);
  if (code && SEND_ERROR_MESSAGES[code]) return SEND_ERROR_MESSAGES[code]!;
  return GENERIC_SEND_ERROR;
}

async function sendWithAttachments(
  conversationId: string,
  { content, draftLogId, files }: SendMessageVariables,
): Promise<SendMessageResponse> {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/smartsupp/conversations/${encodeURIComponent(conversationId)}/messages/with-attachments`;

  const formData = new FormData();
  if (content.trim() !== "") formData.append("content", content);
  if (draftLogId) formData.append("draftLogId", draftLogId);
  (files ?? []).forEach((file) => formData.append("files", file));

  let response: Response;
  try {
    // No Content-Type header: the browser adds the multipart boundary.
    response = await (apiClient as any).http.fetch(fullUrl, { method: "POST", body: formData });
  } catch {
    throw new Error(GENERIC_SEND_ERROR);
  }

  if (!response.ok) {
    let errorCode: ErrorCodes | undefined;
    try {
      errorCode = ((await response.json()) as { errorCode?: string }).errorCode as ErrorCodes | undefined;
    } catch {
      errorCode = undefined;
    }
    throw new Error(messageForSendError(errorCode));
  }

  try {
    return SendMessageResponse.fromJS(await response.json());
  } catch {
    throw new Error(GENERIC_SEND_ERROR);
  }
}

function toOptimisticAttachments(files: File[]): { attachments: MessageAttachmentDto[]; urls: string[] } {
  const attachments = files.map((file) => {
    const fileType = mimeTypeForFileName(file.name) ?? file.type;
    return new MessageAttachmentDto({
      type: fileType.startsWith("image/") ? "image" : "file",
      fileName: file.name,
      fileType,
      size: file.size,
      url: URL.createObjectURL(file),
    });
  });
  return { attachments, urls: attachments.map((a) => a.url as string) };
}

interface SendMessageVariables {
  content: string;
  /** Set when the message was composed from an AI draft, to link the sent text to that draft's log. */
  draftLogId?: string | null;
  files?: File[];
}

interface UseSendMessageResult {
  send: (content: string, draftLogId?: string | null, files?: File[]) => void;
  isPending: boolean;
  error: string | null;
  justSent: boolean;
  clearSent: () => void;
}

type SendMessageContext = { previous?: GetConversationResponse; optimisticId?: string; objectUrls?: string[] };

export function useSendMessage(conversationId: string | null): UseSendMessageResult {
  const queryClient = useQueryClient();

  // This hook owns the optimistic blob: URLs. They are revoked when the optimistic
  // message is replaced (success), rolled back (error) or the hook unmounts.
  const liveObjectUrls = useRef(new Set<string>());
  const releaseObjectUrls = (urls: string[] = []) => {
    urls.forEach((url) => {
      if (!liveObjectUrls.current.delete(url)) return;
      URL.revokeObjectURL(url);
    });
  };
  useEffect(() => {
    const live = liveObjectUrls.current;
    return () => {
      live.forEach((url) => URL.revokeObjectURL(url));
      live.clear();
    };
  }, []);

  const mutation = useMutation<SendMessageResponse, Error, SendMessageVariables, SendMessageContext>({
    mutationFn: async (variables) => {
      const { content, draftLogId, files } = variables;
      if (!conversationId) {
        throw new Error("Není vybrána konverzace.");
      }

      const hasFiles = !!files && files.length > 0;
      let data: SendMessageResponse;
      if (hasFiles) {
        data = await sendWithAttachments(conversationId, variables);
      } else {
        try {
          data = await getAuthenticatedApiClient().smartsupp_SendMessage(
            conversationId,
            new SendMessageBody({ content, draftLogId: draftLogId ?? undefined }),
          );
        } catch (e: unknown) {
          const errorCode = (e as { errorCode?: string }).errorCode as ErrorCodes | undefined;
          throw new Error(messageForSendError(errorCode));
        }
      }
      if (!data.success) {
        throw new Error(messageForSendError(data.errorCode));
      }

      return data;
    },
    onMutate: async ({ content, files }) => {
      if (!conversationId) return {};
      await queryClient.cancelQueries({
        queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      });
      const previous = queryClient.getQueryData<GetConversationResponse>(
        SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      );
      const optimisticId = `optimistic-${Date.now()}`;
      const optimistic = toOptimisticAttachments(files ?? []);
      optimistic.urls.forEach((url) => liveObjectUrls.current.add(url));
      const optimisticMsg = new MessageDto({
        ...(optimistic.attachments.length > 0 ? { attachments: optimistic.attachments } : {}),
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
      return { previous, optimisticId, objectUrls: optimistic.urls };
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
      releaseObjectUrls(context?.objectUrls);
      if (context?.previous !== undefined && conversationId) {
        queryClient.setQueryData(
          SMARTSUPP_QUERY_KEYS.conversation(conversationId),
          context.previous,
        );
      }
    },
  });

  return {
    send: (content: string, draftLogId?: string | null, files?: File[]) =>
      mutation.mutate({ content, draftLogId, files }),
    isPending: mutation.isPending,
    error: mutation.error ? mutation.error.message : null,
    justSent: mutation.isSuccess,
    clearSent: mutation.reset,
  };
}
