import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  ErrorCodes,
  SubmitDraftReplyFeedbackRequest,
  type ISubmitDraftReplyFeedbackRequest,
} from "../../../../api/generated/api-client";

export interface SubmitDraftReplyFeedbackResult {
  alreadySubmitted?: true;
}

/**
 * Submit precision/style feedback for a generated Smartsupp draft reply.
 * Returns { alreadySubmitted: true } on the "already submitted"/"log not found" conflict outcomes
 * instead of throwing (both are mapped to HTTP 409 by the backend's ErrorCodes attribute).
 */
export function useSubmitDraftReplyFeedback() {
  return useMutation<SubmitDraftReplyFeedbackResult, Error, ISubmitDraftReplyFeedbackRequest>({
    mutationFn: async (payload) => {
      const request = new SubmitDraftReplyFeedbackRequest(payload);
      try {
        await getAuthenticatedApiClient().smartsupp_SubmitDraftReplyFeedback(request);
        return {};
      } catch (e: unknown) {
        // The generated client's 403/409 branches parse a ProblemDetails-shaped object rather
        // than throwing a SwaggerException, so `.status` is not reliably populated here — only
        // the raw JSON body's own fields (blanket-copied onto the thrown object by
        // ProblemDetails.init()) survive, which is why this branches on errorCode instead of
        // HTTP status. See docs/development/api-client-generation.md.
        const err = e as { errorCode?: string };
        if (
          err.errorCode === ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted ||
          err.errorCode === ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound
        ) {
          return { alreadySubmitted: true };
        }
        throw e;
      }
    },
  });
}
