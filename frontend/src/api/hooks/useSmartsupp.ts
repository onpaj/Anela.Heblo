import { useQuery } from "@tanstack/react-query";
import { getClientAndBaseUrl, apiGet } from "../smartsuppClient";

export interface ConversationDto {
  id: string;
  subject?: string | null;
  contactName?: string | null;
  contactEmail?: string | null;
  contactAvatarUrl?: string | null;
  status: string;
  isUnread: boolean;
  lastMessageAt?: string | null;
  lastMessagePreview?: string | null;
  createdAt: string;
  updatedAt: string;
  rating?: number | null;
  ratingText?: string | null;
  closeType?: string | null;
  closedByAgentId?: string | null;
  assignedAgentIds: string[];
  channel?: string | null;
  isServed: boolean;
  finishedAt?: string | null;
  domain?: string | null;
  referer?: string | null;
  locationCountry?: string | null;
  locationCity?: string | null;
  locationCode?: string | null;
  tags: string[];
}

export interface MessageDto {
  id: string;
  authorType: string;
  authorName?: string | null;
  content?: string | null;
  createdAt: string;
  agentId?: string | null;
  subType?: string | null;
  deliveryStatus?: string | null;
  deliveredAt?: string | null;
  responseTime?: number | null;
  isFirstReply: boolean;
  pageUrl?: string | null;
}

export interface ListConversationsResponse {
  success: boolean;
  items: ConversationDto[];
  total: number;
  page: number;
  pageSize: number;
}

export interface GetConversationResponse {
  success: boolean;
  conversation?: ConversationDto | null;
  messages: MessageDto[];
}


async function apiFetch(apiClient: Parameters<typeof apiGet>[0], url: string): Promise<Response> {
  const response = await apiGet(apiClient, url);
  if (!response.ok) {
    throw new Error(`Smartsupp API error: ${response.status} ${response.statusText}`);
  }
  return response;
}

export const SMARTSUPP_QUERY_KEYS = {
  conversations: (status: string) => ["smartsupp", "conversations", status] as const,
  conversation: (id: string) => ["smartsupp", "conversation", id] as const,
};

export function useSmartsuppConversations(status: "Open" | "Resolved" = "Open") {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.conversations(status),
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiFetch(apiClient, `${baseUrl}/api/smartsupp/conversations?status=${status}&page=1&pageSize=100`);
      return response.json() as Promise<ListConversationsResponse>;
    },
    refetchInterval: 60_000,
    staleTime: 30_000,
  });
}

export function useSmartsuppConversation(id: string | null) {
  return useQuery({
    queryKey: SMARTSUPP_QUERY_KEYS.conversation(id ?? ""),
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiFetch(apiClient, `${baseUrl}/api/smartsupp/conversations/${id}`);
      return response.json() as Promise<GetConversationResponse>;
    },
    enabled: !!id,
    refetchInterval: 30_000,
    staleTime: 15_000,
  });
}

