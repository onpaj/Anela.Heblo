import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import {
  getAuthenticatedApiClient,
  getApiBaseUrl,
  getAuthenticatedFetch,
  QUERY_KEYS,
} from "../client";
import {
  ErrorCodes,
  type ConversationDto,
  type ConversationPresenceDto,
  type ConversationSummaryDto,
  type MessageDto,
  type ListConversationsResponse,
  type GetConversationResponse,
  type GetSmartsuppContactShoptetInfoResponse,
  type GetVisitorInfoResponse,
  type CloseConversationResponse,
} from "../generated/api-client";

// Re-exported so sibling hooks under components/customer-support/smartsupp/hooks/ can keep
// importing these from this file's existing path instead of reaching into the generated client
// directly.
export type {
  ConversationDto,
  ConversationPresenceDto,
  ConversationSummaryDto,
  MessageDto,
  ListConversationsResponse,
  GetConversationResponse,
};

export const SMARTSUPP_QUERY_KEYS = {
  conversations: (status: string) => ["smartsupp", "conversations", status] as const,
  conversation: (id: string) => ["smartsupp", "conversation", id] as const,
  shoptetInfo: (id: string) => ["smartsupp", "shoptet-info", id] as const,
  visitorInfo: (id: string) => ["smartsupp", "visitor-info", id] as const,
};

export function useSmartsuppConversations(status: "Open" | "Resolved" = "Open") {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.conversations(status),
    queryFn: (): Promise<ListConversationsResponse> =>
      getAuthenticatedApiClient().smartsupp_GetConversations(status, 1, 100),
    refetchInterval: 10_000,
    staleTime: 10_000,
  });
}

export function useSmartsuppConversation(id: string | null) {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.conversation(id ?? ""),
    queryFn: (): Promise<GetConversationResponse> =>
      getAuthenticatedApiClient().smartsupp_GetConversation(id!),
    enabled: !!id,
    refetchInterval: 30_000,
    staleTime: 15_000,
  });
}

// Shoptet/visitor info stay on the escape hatch permanently, not the typed client: the (currently
// unwired) NSwag template-override predicate that would let the generated client return a typed
// non-throwing 404 branch is hardcoded to fire only for HTTP 409 (see
// backend/src/Anela.Heblo.API/nswag-templates/README.md), so a typed try/catch on 404 here would
// buy nothing over the escape hatch. See docs/development/api-client-generation.md for the
// escape-hatch pattern this mirrors.
export function useSmartsuppShoptetInfo(conversationId: string | null) {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.shoptetInfo(conversationId ?? ""),
    queryFn: async (): Promise<GetSmartsuppContactShoptetInfoResponse | null> => {
      const response = await getAuthenticatedFetch()(
        `${getApiBaseUrl()}/api/smartsupp/conversations/${conversationId}/shoptet-info`,
        { method: "GET" },
      );
      if (response.status === 404) return null;
      if (!response.ok) throw new Error(`Shoptet info error: ${response.status}`);
      return (await response.json()) as GetSmartsuppContactShoptetInfoResponse;
    },
    enabled: !!conversationId,
    staleTime: 300_000,
    retry: false,
  });
}

export function useSmartsuppVisitorInfo(conversationId: string | null) {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.visitorInfo(conversationId ?? ""),
    queryFn: async (): Promise<GetVisitorInfoResponse | null> => {
      const response = await getAuthenticatedFetch()(
        `${getApiBaseUrl()}/api/smartsupp/conversations/${conversationId}/visitor-info`,
        { method: "GET" },
      );
      if (response.status === 404) return null;
      if (!response.ok) throw new Error(`Visitor info error: ${response.status}`);
      return (await response.json()) as GetVisitorInfoResponse;
    },
    enabled: !!conversationId,
    staleTime: 600_000,
    retry: false,
  });
}

const CLOSE_ERROR_MESSAGES: Partial<Record<ErrorCodes, string>> = {
  [ErrorCodes.SmartsuppCloseConversationUnavailable]:
    "Nepodařilo se uzavřít konverzaci — služba je nedostupná. Zkuste to prosím znovu.",
  [ErrorCodes.SmartsuppConversationNotFound]: "Konverzace nebyla nalezena.",
};

function messageForCloseError(code?: ErrorCodes): string {
  if (code && CLOSE_ERROR_MESSAGES[code]) return CLOSE_ERROR_MESSAGES[code]!;
  return "Nepodařilo se uzavřít konverzaci.";
}

export function useCloseConversation() {
  const queryClient = useQueryClient();
  return useMutation<CloseConversationResponse, Error, string>({
    mutationFn: async (conversationId: string) => {
      let data: CloseConversationResponse;
      try {
        data = await getAuthenticatedApiClient().smartsupp_CloseConversation(conversationId);
      } catch (e: unknown) {
        const errorCode = (e as { errorCode?: string }).errorCode as ErrorCodes | undefined;
        throw new Error(messageForCloseError(errorCode));
      }
      if (!data.success) {
        throw new Error(messageForCloseError(data.errorCode));
      }
      return data;
    },
    onSuccess: (_data, conversationId) => {
      queryClient.invalidateQueries({
        queryKey: SMARTSUPP_QUERY_KEYS.conversation(conversationId),
      });
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.smartsupp, "conversations"],
      });
    },
  });
}

const PRESENCE_HEARTBEAT_INTERVAL_MS = 20_000;

/**
 * While a conversation detail is open, periodically tell the backend the current operator is
 * viewing it, so other operators see a presence badge. Sends a best-effort "leave" on unmount;
 * the backend TTL cleans up if that never arrives.
 */
export function usePresenceHeartbeat(conversationId: string | null): void {
  useEffect(() => {
    if (!conversationId) return;

    let cancelled = false;
    const apiClient = getAuthenticatedApiClient();

    const beat = () => {
      apiClient.smartsupp_RecordPresence(conversationId).catch(() => {
        /* presence is best-effort; ignore transient failures */
      });
    };

    beat();
    const timer = window.setInterval(() => {
      if (!cancelled) beat();
    }, PRESENCE_HEARTBEAT_INTERVAL_MS);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
      // The generated smartsupp_RemovePresence method builds its RequestInit internally with no
      // way for a caller to pass `keepalive` through its public signature — every NSwag
      // Fetch-template method owns its own options object, so this is a structural gap, not a
      // "not wired yet" one. Stay on the escape hatch for this one call so the "leave" signal
      // survives page/tab unload; smartsupp_RecordPresence above has no such need and stays typed.
      getAuthenticatedFetch()(
        `${getApiBaseUrl()}/api/smartsupp/conversations/${conversationId}/presence`,
        { method: "DELETE", keepalive: true },
      ).catch(() => {
        /* best-effort leave; TTL cleans up otherwise */
      });
    };
  }, [conversationId]);
}

/** Active viewers of a conversation other than the current operator. */
export function otherActiveViewers(
  conversation: Pick<ConversationDto, "activeViewers">,
): ConversationPresenceDto[] {
  return (conversation.activeViewers ?? []).filter((v) => !v.isCurrentUser);
}
