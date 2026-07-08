import { useQuery } from "@tanstack/react-query";
import { getClientAndBaseUrl, apiGet } from "../../../../api/smartsuppClient";
import type {
  RagFeedbackLogSummary,
  RagFeedbackStats,
} from "../../../feedback/ragFeedbackTypes";

export interface DraftReplyFeedbackListParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  hasFeedback?: boolean;
  userId?: string;
}

export interface DraftReplyFeedbackListResponse {
  success: boolean;
  logs: RagFeedbackLogSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  stats: RagFeedbackStats;
}

const QUERY_KEY = ["smartsupp", "draft-reply", "feedback-list"] as const;

export function useSmartsuppDraftReplyFeedbackListQuery(params: DraftReplyFeedbackListParams = {}) {
  return useQuery<DraftReplyFeedbackListResponse>({
    queryKey: [...QUERY_KEY, params],
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();

      const search = new URLSearchParams();
      if (params.pageNumber !== undefined) search.append("pageNumber", String(params.pageNumber));
      if (params.pageSize !== undefined) search.append("pageSize", String(params.pageSize));
      if (params.sortBy !== undefined) search.append("sortBy", params.sortBy);
      if (params.sortDescending !== undefined)
        search.append("sortDescending", String(params.sortDescending));
      if (params.hasFeedback !== undefined) search.append("hasFeedback", String(params.hasFeedback));
      if (params.userId !== undefined) search.append("userId", params.userId);

      const query = search.toString();
      const url = `${baseUrl}/api/smartsupp/draft-reply/feedback/list${query ? `?${query}` : ""}`;
      const response = await apiGet(apiClient, url);

      if (!response.ok) {
        throw new Error(`Failed to fetch Smartsupp feedback list: ${response.status}`);
      }

      return (await response.json()) as DraftReplyFeedbackListResponse;
    },
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
}
