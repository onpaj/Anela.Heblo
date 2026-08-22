# Code Review: route-usesmartsupp-core-hooks

## Summary
The implementation matches the specified target file and all four specified test-file rewrites verbatim (verified with `diff` against the exact snippets in the task-context file). `useSmartsupp.ts` no longer imports from `smartsuppClient.ts` and contains no `(apiClient as any)` casts; the four core hooks route through `getAuthenticatedApiClient()`, and the two status-code-branching queries stay on the documented `getApiBaseUrl()` + `getAuthenticatedFetch()` escape hatch. Ran the four affected test suites live: 4 suites / 17 tests, all passing.

## Review Result: PASS

### task: route-usesmartsupp-core-hooks
**Status:** PASS

## Overall Notes
- Verified byte-for-byte (via `diff`) that `frontend/src/api/hooks/useSmartsupp.ts`, `useCloseConversation.test.ts`, `usePresenceHeartbeat.test.ts`, `useSmartsuppVisitorInfo.test.ts`, and the `MIGRATED_HOOKS` addition in `authenticated-api-usage.test.ts` match the spec's exact snippets.
- Confirmed every generated-client symbol the new file relies on actually exists with the expected shape in `frontend/src/api/generated/api-client.ts`: `smartsupp_GetConversations(status, page, pageSize)`, `smartsupp_GetConversation(id)`, `smartsupp_CloseConversation(id)`, `smartsupp_RecordPresence(id)`, the `ConversationDto`/`ConversationPresenceDto`/`ConversationSummaryDto`/`MessageDto`/`ListConversationsResponse`/`GetConversationResponse`/`GetSmartsuppContactShoptetInfoResponse`/`GetVisitorInfoResponse`/`CloseConversationResponse` classes, and `ErrorCodes.SmartsuppCloseConversationUnavailable`/`SmartsuppConversationNotFound`. `CloseConversationResponse.success`/`.errorCode` come from the `BaseResponse` base class it extends — confirmed present, so `useCloseConversation`'s two-channel error handling is well-typed, not a bug.
- Confirmed `grep -rn "smartsuppClient"` across `frontend/src` shows zero references from `useSmartsupp.ts` itself; the only remaining references are in the explicitly out-of-scope consumer hooks (`useGenerateDraftReply.ts`, `useSendMessage.ts`, `useSubmitDraftReplyFeedback.ts`, `useSmartsuppDraftReplyFeedbackListQuery.ts`) and the test file's carve-out string check — exactly as the task scoped it.
- Ran `CI=true npx react-scripts test src/api/hooks/__tests__/useCloseConversation.test.ts src/api/hooks/__tests__/usePresenceHeartbeat.test.ts src/api/hooks/__tests__/useSmartsuppVisitorInfo.test.ts src/api/__tests__/authenticated-api-usage.test.ts --watchAll=false` myself: 4 suites passed, 17 tests passed, matching the developer's self-report.
- Ran `npx tsc --noEmit` on the whole project: 38 pre-existing type errors remain, none of them in `useSmartsupp.ts` — consistent with this task's explicit exclusion of fixing consumer components, and consistent with instructions not to flag `npm run build`/consumer fallout as a defect of this task.
- The escape-hatch usage for `useSmartsuppShoptetInfo`/`useSmartsuppVisitorInfo` and the `keepalive` DELETE in `usePresenceHeartbeat`'s cleanup matches the documented pattern in `docs/development/api-client-generation.md` exactly (typed client for standard calls, `getApiBaseUrl()` + `getAuthenticatedFetch()` only for status-code branching cases where the typed client can't yet express the outcome).
- No documentation updates needed; the implementation's in-code comments already point at the relevant docs/README locations.

**Status:** PASS
