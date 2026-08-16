# Implementation: route-usesendmessage-hook

## What was implemented
Rewrote `useSendMessage.ts` to route through the generated typed API client
(`getAuthenticatedApiClient().smartsupp_SendMessage(conversationId, new SendMessageBody(...))`)
instead of the old hand-rolled `getClientAndBaseUrl()` / `apiPost()` /
untyped-JSON path via `../../../../api/smartsuppClient`. Error mapping now
keys off the generated `ErrorCodes` enum (`SmartsuppSendMessageUnavailable`,
`SmartsuppConversationNotFound`) instead of raw strings, and the mutation's
generic types now use the generated `SendMessageResponse` instead of a
hand-declared `SendMessageApiResponse` interface. The optimistic-update /
rollback logic (pending message shown immediately, replaced with the real
`messageId` on success, rolled back to the previous cache snapshot on
failure) is preserved as-is — only the transport and typing changed.

Rewrote the test file to match: mocks `smartsupp_SendMessage` directly
instead of a raw `fetch`, and asserts against the typed call shape.

## Deviation from the spec (required to make the build pass)
The task spec's snippet builds the optimistic cache entries with
`new GetConversationResponse({...old, messages: [...]})`. That does **not**
work under this repo's actual build pipeline (CRA/Babel, `target: es5` in
`tsconfig.json`): `GetConversationResponse extends BaseResponse`, and its own
`constructor(data) { super(data); }` — when Babel compiles the class's own
field declarations (`conversation`, `messages`, `agentNames`) as
post-`super()` assignment statements — resets those three fields back to
`undefined` immediately after `BaseResponse`'s constructor loop had just
copied them in from `data`. `MessageDto` doesn't have this problem (it has no
base class, so its own constructor body runs after its own field inits, and
the copy loop's writes stick).

I confirmed this empirically with a throwaway debug test: `new
GetConversationResponse({success: true, messages: [...]})` produces an
instance with `messages === undefined` in this repo's Jest/Babel runtime,
even though it type-checks fine and even though a plain `tsc` pass alone
wouldn't necessarily surface it (the CLAUDE.md task's snippet was evidently
checked against the type checker, not the actual Babel-compiled runtime
behavior used by both `react-scripts test` and `react-scripts build`).

**Fix applied:** use `GetConversationResponse.fromJS({...})` instead of `new
GetConversationResponse({...})` at both cache-update call sites (`onMutate`
and `onSuccess`). `fromJS` calls `result.init(data)` as a separate step after
construction (not inside the constructor), so it isn't affected by the
field-initializer-after-`super()` ordering issue. `MessageDto` is still
constructed with plain `new MessageDto({...})` as the spec described, since
that pattern is confirmed to work correctly for classes with no base class.

This is a real, reproducible bug in the spec's snippet under this repo's
toolchain, not a stylistic choice — without it, two of the eight new
optimistic-update tests failed (`cached?.messages` was `undefined`).

## Files created/modified
- `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts` — rewritten to use the typed generated client; `GetConversationResponse.fromJS(...)` used instead of `new GetConversationResponse(...)` for the reason above.
- `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts` — rewritten to mock `smartsupp_SendMessage` and assert the typed call shape; covers success, draftLogId forwarding, typed API failure, untyped throw (400/404/503), null conversationId, optimistic-update rollback on failure, isPending, optimistic pending-status display, and replacement of the optimistic message with the real `messageId`/`sent` status on success.

## Tests
`useSendMessage.test.ts` — 9/9 passing (calls typed client and returns
messageId; forwards draftLogId; typed API failure error message; untyped
throw generic error message; no-op + error when conversationId is null;
optimistic rollback on failure; isPending while in flight; optimistic
pending message shown while in flight; optimistic message replaced with real
id/status on success).

Also ran the full Smartsupp component suite as a regression check
(`src/components/customer-support/smartsupp`): 22 suites / 177 tests, all
passing — confirms `ChatComposer.tsx` (the sole consumer of this hook) is
unaffected, since the hook's public return shape (`send`, `isPending`,
`error`, `justSent`, `clearSent`) is unchanged.

## How to verify
```bash
cd frontend
npm install --legacy-peer-deps   # matches CI's install flag; node_modules gitignored
CI=true npx react-scripts test src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts --watchAll=false
CI=true npx react-scripts test src/components/customer-support/smartsupp --watchAll=false   # full module regression check
npx eslint src/components/customer-support/smartsupp/hooks/useSendMessage.ts src/components/customer-support/smartsupp/hooks/__tests__/useSendMessage.test.ts   # clean, 0 problems
npm run build
```

## Notes
**`npm run build` does not pass cleanly** — but not because of anything in
this task's two files. After this fix, the build gets past `useSendMessage.ts`
(which was the previously-documented blocker, per the prior
`fix-smartsupp-consumer-optionality-fallout` task's notes) and now fails one
file later, on a **pre-existing, unrelated** error:

```
TS18048: 'pages' is possibly 'undefined'.
  frontend/src/components/customer-support/smartsupp/VisitorInfoCard.tsx:31
    const visiblePages = expanded ? pages : pages.slice(0, INITIAL_PAGE_LIMIT);
```

Root cause: `VisitorInfoDto.pages` is `pages?: VisitorPageDto[]` (optional)
in the generated client (a consequence of the earlier
`route-usesmartsupp-core-hooks` task switching `useSmartsuppVisitorInfo` to
the fully-optional generated `GetVisitorInfoResponse`/`VisitorInfoDto`
types), but `VisitorInfoCard.tsx`'s own internal destructure/`.slice()`/
`.length` usage of `pages` was never updated to tolerate that. This is
exactly the same class of "consumer optionality fallout" the dedicated
`fix-smartsupp-consumer-optionality-fallout` task fixed for eight other
files — but that task's scope covered call sites that pass props *into*
`VisitorInfoCard` (`conversation.id ?? null`), not `VisitorInfoCard`'s own
internal use of `data.visitorInfo.pages`, so this particular gap was missed
and never previously surfaced because `useSendMessage.ts`'s own build error
was masking it (the compiler fails fast on the first file with an error).

I left `VisitorInfoCard.tsx` untouched, consistent with this task's
"Files" scope (only `useSendMessage.ts` and its test) and CLAUDE.md's
"surgical changes — touch only what the task requires" rule, and consistent
with the precedent set by the prior task in this same pipeline (which
likewise documented rather than fixed a build blocker outside its declared
file scope). This should be filed as a follow-up fix (one-line-style:
`pages ?? []` fallback, same pattern used throughout
`fix-smartsupp-consumer-optionality-fallout`) — flagging it here rather than
silently leaving `npm run build` red without explanation.

Everything within this task's actual scope is clean: the two modified files
pass ESLint with 0 problems, the new/rewritten test file is 9/9 green, and
the full 177-test Smartsupp component suite (including `ChatComposer.test.tsx`,
this hook's only consumer) passes unchanged.

`node_modules` was absent in this worktree; installed via `npm install
--legacy-peer-deps` (matches `.github/workflows/ci-*.yml`), left installed
but not committed (gitignored).

## Status
DONE_WITH_CONCERNS
