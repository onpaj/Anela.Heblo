# Implementation: route-usesubmitdraftreplyfeedback-hook

## What was implemented
Rewrote `useSubmitDraftReplyFeedback` to go through the generated typed API client
(`getAuthenticatedApiClient().smartsupp_SubmitDraftReplyFeedback(...)`) instead of the
hand-rolled `getClientAndBaseUrl`/`apiPost` bypass in `api/smartsuppClient`. The mutation's
`TVariables` type is the generated `ISubmitDraftReplyFeedbackRequest` interface (matching the
precedent in `useKnowledgeBase.ts`'s `useSubmitFeedbackMutation`), so the existing call site in
`DraftReplyFeedback.tsx` (which passes a plain object literal) keeps working unchanged. The
"already submitted" / "log not found" conflict outcomes (both mapped to HTTP 409 server-side) are
now detected via `ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted` /
`ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound` on the thrown error object and converted to
`{ alreadySubmitted: true }` instead of throwing; any other error (including 403 Forbidden) still
rethrows, matching prior behavior.

## Files created/modified
- `frontend/src/components/customer-support/smartsupp/hooks/useSubmitDraftReplyFeedback.ts` —
  rewritten to call the generated `smartsupp_SubmitDraftReplyFeedback` client method with a
  `SubmitDraftReplyFeedbackRequest` instance built from the caller's payload, and to branch on
  `ErrorCodes` instead of raw HTTP status.

## Tests
None added — no existing test file covers this hook
(`**/useSubmitDraftReplyFeedback*.test.ts*` has no matches), and per the migration spec's Out of
Scope section, adding new coverage where none exists is not part of this task.

## How to verify
- `cd frontend && npm run build` — confirmed the file type-checks cleanly. (Note: the full CRA
  build failed on an unrelated pre-existing bug in
  `frontend/src/components/customer-support/smartsupp/VisitorInfoCard.tsx` — `pages` from
  `data.visitorInfo` is possibly `undefined`, TS18048 — verified present on this branch *before*
  this change too, via `git stash`. To confirm this file compiles cleanly independent of that
  pre-existing bug, I temporarily patched `VisitorInfoCard.tsx` locally, reran `npm run build`
  (succeeded, "Compiled successfully"), then reverted that unrelated file with
  `git checkout -- frontend/src/components/customer-support/smartsupp/VisitorInfoCard.tsx` before
  committing — it is not part of this commit.)
- Manually confirmed `frontend/src/components/customer-support/smartsupp/DraftReplyFeedback.tsx`
  (the only caller) needs no changes: it calls `submitFeedback.mutate({logId, precisionScore,
  styleScore, comment}, {onSuccess: (result) => {...}})`, which structurally satisfies
  `ISubmitDraftReplyFeedbackRequest` and consumes `result.alreadySubmitted` as before.
- Confirmed in the generated client (`frontend/src/api/generated/api-client.ts`) that
  `smartsupp_SubmitDraftReplyFeedback`, `SubmitDraftReplyFeedbackRequest`,
  `ISubmitDraftReplyFeedbackRequest`, and both `ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted`
  / `ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound` exist exactly as named in the spec — no
  substitution of error code names was needed.

## Notes
- Pre-existing unrelated build failure in `VisitorInfoCard.tsx` (TS18048 on `pages` possibly
  undefined) blocks a full `npm run build` on this branch as-is; this is out of scope for this
  task and was not touched in the committed diff.
- `node_modules` was not present in the worktree and had to be installed
  (`npm install --legacy-peer-deps`, since `npm ci` fails on a pre-existing `react-i18next`
  peer-dependency conflict with the pinned `typescript@^4.9.5`) purely to run the build/type-check
  locally; no `package.json`/`package-lock.json` changes were made or committed.

## Status
DONE
