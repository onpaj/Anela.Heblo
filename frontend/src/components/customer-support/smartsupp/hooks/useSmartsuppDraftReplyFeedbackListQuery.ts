import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  toLocalFeedbackListResponse,
  type LocalFeedbackListResponse,
} from "../../../feedback/ragFeedbackMapping";

export interface DraftReplyFeedbackListParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  hasFeedback?: boolean;
  userId?: string;
}

export type DraftReplyFeedbackListResponse = LocalFeedbackListResponse;

const QUERY_KEY = ["smartsupp", "draft-reply", "feedback-list"] as const;

export function useSmartsuppDraftReplyFeedbackListQuery(params: DraftReplyFeedbackListParams = {}) {
  return useQuery<DraftReplyFeedbackListResponse>({
    queryKey: [...QUERY_KEY, params],
    queryFn: async () => {
      const generated = await getAuthenticatedApiClient().smartsupp_GetDraftReplyFeedbackList(
        params.pageNumber,
        params.pageSize,
        params.sortBy,
        params.sortDescending,
        params.hasFeedback,
        params.userId,
      );
      return toLocalFeedbackListResponse(generated);
    },
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
}
