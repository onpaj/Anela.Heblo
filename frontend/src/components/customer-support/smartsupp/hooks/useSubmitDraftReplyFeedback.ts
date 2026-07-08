import { useMutation } from "@tanstack/react-query";
import { getClientAndBaseUrl, apiPost } from "../../../../api/smartsuppClient";

export interface SubmitDraftReplyFeedbackRequest {
  logId: string;
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

export interface SubmitDraftReplyFeedbackResult {
  alreadySubmitted?: true;
}

/**
 * Submit precision/style feedback for a generated Smartsupp draft reply.
 * Returns { alreadySubmitted: true } on 409 instead of throwing.
 */
export function useSubmitDraftReplyFeedback() {
  return useMutation<SubmitDraftReplyFeedbackResult, Error, SubmitDraftReplyFeedbackRequest>({
    mutationFn: async (payload) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiPost(
        apiClient,
        `${baseUrl}/api/smartsupp/draft-reply/feedback`,
        payload,
      );

      if (response.status === 409) {
        return { alreadySubmitted: true };
      }

      if (!response.ok) {
        throw new Error(`Submit feedback failed: ${response.status}`);
      }

      return {};
    },
  });
}
