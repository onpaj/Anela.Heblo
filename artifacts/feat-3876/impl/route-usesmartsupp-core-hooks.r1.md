# Implementation: route-usesmartsupp-core-hooks

## What was implemented
Rewrote `frontend/src/api/hooks/useSmartsupp.ts` to route its core hooks
(`useSmartsuppConversations`, `useSmartsuppConversation`, `useCloseConversation`,
`usePresenceHeartbeat`) through the generated typed API client
(`getAuthenticatedApiClient()`), removing the hand-declared DTO/response interfaces and the
`../smartsuppClient` escape-hatch wrapper (`getClientAndBaseUrl`/`apiGet`/`apiPost`/`apiDelete`)
they previously used. `useSmartsuppShoptetInfo` and `useSmartsuppVisitorInfo` intentionally stay
on the `getAuthenticatedFetch()` escape hatch (documented in-code why), now built directly on
`getApiBaseUrl()` + `getAuthenticatedFetch()` from `../client` instead of the deleted
`smartsuppClient` module. `otherActiveViewers` is unchanged in behavior.

All generated-client names referenced in the task spec (`ConversationDto`,
`ConversationPresenceDto`, `ConversationSummaryDto`, `MessageDto`, `ListConversationsResponse`,
`GetConversationResponse`, `GetSmartsuppContactShoptetInfoResponse`, `GetVisitorInfoResponse`,
`CloseConversationResponse`, `ErrorCodes`, `smartsupp_GetConversations`,
`smartsupp_GetConversation`, `smartsupp_CloseConversation`, `smartsupp_RecordPresence`,
`smartsupp_RemovePresence`) were verified present and correctly named in
`frontend/src/api/generated/api-client.ts` before writing the file — no deviations were needed
there. `../client`'s `getAuthenticatedApiClient`, `getApiBaseUrl`, `getAuthenticatedFetch`, and
`QUERY_KEYS` were likewise verified.

Also closed the `MIGRATED_HOOKS` regression-guard gap in
`frontend/src/api/__tests__/authenticated-api-usage.test.ts` by adding `"useSmartsupp.ts"` to the
set, so any future reintroduction of `(apiClient as any)`-style casts in this file gets caught.

## Files created/modified
- `frontend/src/api/hooks/useSmartsupp.ts` — rewritten per spec; typed client for
  conversations/close/presence, escape hatch retained (via `getApiBaseUrl`/`getAuthenticatedFetch`)
  for shoptet-info/visitor-info only.
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts` — added `useSmartsupp.ts` to
  `MIGRATED_HOOKS`.
- `frontend/src/api/hooks/__tests__/useCloseConversation.test.ts` — rewritten to mock
  `smartsupp_CloseConversation` on the typed client directly instead of the old raw-fetch mock.
- `frontend/src/api/hooks/__tests__/usePresenceHeartbeat.test.ts` — rewritten to mock
  `smartsupp_RecordPresence` on the typed client for the heartbeat, and the
  `getAuthenticatedFetch()` escape hatch for the keepalive DELETE leave call.
- `frontend/src/api/hooks/__tests__/useSmartsuppVisitorInfo.test.ts` — rewritten to mock the
  `getAuthenticatedFetch()` escape hatch (unchanged from prior test's pattern, but now going
  through `getApiBaseUrl()`/`getAuthenticatedFetch()` return-value mocks instead of the removed
  `smartsuppClient`); added an explicit assertion that the escape-hatch call hits the expected URL.

## Tests
- `useCloseConversation.test.ts` — 3 tests: mutation calls the typed client with the id; typed
  `success:false` + `SmartsuppCloseConversationUnavailable` response maps to the Czech
  "nedostupná" message; a thrown/rejected call (untyped 404/503) falls back to a generic message.
- `usePresenceHeartbeat.test.ts` — 6 tests: immediate heartbeat call, no-op when id is null,
  interval-driven repeat beats, keepalive DELETE leave on unmount via the escape hatch, and
  `otherActiveViewers` filtering (current-user exclusion, empty-list case).
- `useSmartsuppVisitorInfo.test.ts` — 4 tests: disabled when id is null, 404 maps to `null` data,
  200 maps to the typed `visitorInfo` payload, and the escape-hatch call hits the expected URL/verb.
- `authenticated-api-usage.test.ts` — unchanged test bodies; `MIGRATED_HOOKS` now includes
  `useSmartsupp.ts`, tightening the forbidden-cast regression guard for this file.

## How to verify
```bash
cd frontend
npm install --legacy-peer-deps   # node_modules was not present in the worktree; CI uses this flag
CI=true npx react-scripts test src/api/hooks/__tests__/useCloseConversation.test.ts \
  src/api/hooks/__tests__/usePresenceHeartbeat.test.ts \
  src/api/hooks/__tests__/useSmartsuppVisitorInfo.test.ts \
  src/api/__tests__/authenticated-api-usage.test.ts --watchAll=false
```
Result: 4 suites, 17 tests, all passing.

## Notes
- **Deviation (test-only, not spec logic):** the task's snippet for
  `usePresenceHeartbeat.test.ts` and `useSmartsuppVisitorInfo.test.ts` mocks `getApiBaseUrl` inline
  as `jest.fn(() => "http://localhost:5001")` directly inside the `jest.mock("../../client", ...)`
  factory. In this repo's actual Jest/Babel/CRA toolchain, a function mocked with an inline
  factory-time default implementation and then *called from inside the module under test* (as
  opposed to called directly by the test file itself) reproducibly returned `undefined` at
  runtime — confirmed with a minimal isolated repro (`jest.fn(() => "x")` in a mock factory,
  called via a live `import` binding from the SUT, returns `undefined`, while the identical mock
  set via `.mockReturnValue()` in a `beforeEach` after the module is loaded works correctly). The
  existing repo pattern for `getAuthenticatedApiClient`/`getAuthenticatedFetch` mocks already uses
  the `jest.fn()` (empty) + `.mockReturnValue()`-in-`beforeEach` style — I applied the exact same
  style to `getApiBaseUrl` for consistency and to make the tests actually pass. No test *behavior*
  or *assertions* changed from the task spec — only how the `getApiBaseUrl` mock's return value is
  wired up.
- `node_modules` was absent in this worktree; installed via `npm install --legacy-peer-deps`
  (matching the flag used in `.github/workflows/ci-*.yml`) before running tests. Left installed;
  not committed (gitignored).
- Per task instructions, `npm run build` was intentionally NOT run — the consumer components
  (`useGenerateDraftReply.ts`, `useSendMessage.ts`, `useSubmitDraftReplyFeedback.ts`,
  `useSmartsuppDraftReplyFeedbackListQuery.ts`, and various `components/customer-support/smartsupp/*`
  UI components) still assume the old hand-declared required-field types and are fixed by later,
  separate tasks in this multi-task plan.
- `artifacts/feat-3876/state.json` had a pre-existing uncommitted modification in the worktree
  before this task started; left untouched and not included in this commit.

## Status
DONE
