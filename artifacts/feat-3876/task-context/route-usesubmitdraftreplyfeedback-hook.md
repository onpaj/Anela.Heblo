### task: route-usesubmitdraftreplyfeedback-hook

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/useSubmitDraftReplyFeedback.ts`

No existing test file covers this hook (confirmed: `Glob **/useSubmitDraftReplyFeedback*.test.ts*` returns no results), and per the spec's Out of Scope section, adding new test coverage where none currently exists is not part of this migration — this task changes only the implementation file.

#### Step 1: Rewrite `useSubmitDraftReplyFeedback.ts`

Replace the entire file with:

```ts
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
```

Note on the request-variables type: `ISubmitDraftReplyFeedbackRequest` (the generated *interface*, not the class) is used for the mutation's `TVariables`, matching the existing precedent in `frontend/src/api/hooks/useKnowledgeBase.ts`'s `useSubmitFeedbackMutation` (`mutationFn: async (payload: ISubmitFeedbackRequest) => { ... new SubmitFeedbackRequest(payload) ... }`). `DraftReplyFeedback.tsx` (the only caller) already passes a plain object literal (`{logId, precisionScore, styleScore, comment}`); using the interface here keeps that call site working unchanged, whereas typing `TVariables` as the class `SubmitDraftReplyFeedbackRequest` itself would require every caller to also pass class instances (plain object literals don't structurally satisfy NSwag's generated classes — see the note in the `useSendMessage` task).

The 403 (Forbidden — feedback logged by a different user) case is deliberately **not** matched by the errorCode check above and falls through to `throw e`, matching current behavior: the old code's `if (response.status === 409) return {alreadySubmitted:true}; if (!response.ok) throw ...` also only special-cased 409, letting 403 surface as a generic mutation error.

#### Step 2: Manually verify against `DraftReplyFeedback.tsx`

Confirm the call site in `frontend/src/components/customer-support/smartsupp/DraftReplyFeedback.tsx` still type-checks with no changes:

```tsx
submitFeedback.mutate(
  {
    logId,
    precisionScore: data.precisionScore,
    styleScore: data.styleScore,
    comment: data.comment,
  },
  {
    onSuccess: (result) => {
      if (result.alreadySubmitted) setAlreadySubmitted(true);
    },
  },
)
```

No edits needed to this file — it's read-only for this task.

#### Step 3: Type-check

```bash
cd frontend
npm run build
```

Expect zero errors.

#### Step 4: Commit

```bash
git add frontend/src/components/customer-support/smartsupp/hooks/useSubmitDraftReplyFeedback.ts
git commit -m "Route useSubmitDraftReplyFeedback through the generated typed API client"
```

---
