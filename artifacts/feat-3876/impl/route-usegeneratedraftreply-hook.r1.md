# Implementation: route-usegeneratedraftreply-hook

## What was implemented
Rewrote `useGenerateDraftReply` to call the generated typed API client method
`smartsupp_GenerateDraftReply(id, GenerateDraftReplyBody)` instead of the private-transport-cast
helper (`getClientAndBaseUrl` / `apiPost` from `../../../../api/smartsuppClient`). The hook's
public interface (`generate`, `isLoading`, `error`, `result`, `reset`) and the `DraftReplyResult`
/`DraftReplySource` shapes are unchanged, so existing consumers (`ChatComposer.tsx`,
`DraftReplySourcesModal.tsx`, `DraftReplyToolbar.tsx`) required no changes.

Error handling was adapted to the generated client's actual behavior: the controller action's
400/404/503 responses are untyped (`ProducesResponseType` without a body contract) so the
generated client throws a generic exception on those, with no usable `errorCode` — those are
caught and mapped to the generic Czech fallback message. The 200 response is typed
(`GenerateDraftReplyResponse`), so `success: false` with a typed `errorCode` (an `ErrorCodes`
enum member) is mapped through the existing `ERROR_MESSAGES` table, now keyed by the `ErrorCodes`
enum instead of raw strings.

`topic` is now passed as `topic ?? undefined` (was `topic ?? null`) because
`GenerateDraftReplyBody.topic` is generated as `string | undefined`. Omitting the key from the
outgoing JSON is equivalent server-side to the previous explicit `null`, since the backend's
`Topic` property is a nullable C# string either way.

## Files created/modified
- `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts` — hook now
  calls `getAuthenticatedApiClient().smartsupp_GenerateDraftReply(...)` with a
  `GenerateDraftReplyBody` instance, maps the typed `GenerateDraftReplyResponse` (including
  `ErrorCodes`-keyed error messages) into the existing `DraftReplyResult` shape.
- `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts`
  — rewritten to mock `smartsupp_GenerateDraftReply` on the typed client directly instead of
  mocking `fetch`/`http`.

## Tests
`useGenerateDraftReply.test.ts` covers:
- success path returns `answer`/`sources` from the typed response
- `topic` is passed through to `smartsupp_GenerateDraftReply` as part of the typed body
- a known `ErrorCodes` value in a typed `success: false` response maps to the correct Czech
  message
- a thrown error from the client (representing the controller's untyped 400/404/503 responses)
  maps to the generic Czech fallback message

Ran the full `smartsupp` test suite (22 suites / 177 tests) — all pass, including consumers
(`ChatComposer`, `DraftReplySourcesModal`, `DraftReplyToolbar`) that use this hook.

## How to verify
```bash
cd frontend
CI=true npx react-scripts test src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts --watchAll=false
npm run build
```

## Notes
`npm run build` fails, but the failure (`TS2322: Type 'string' is not assignable to type 'Date'`
in `useSendMessage.ts` line 83) is entirely pre-existing and unrelated to this change — it comes
from the separate, not-yet-completed `route-usesendmessage-hook` task in this same feature
(`MessageDto.createdAt` is now typed as `Date` by the generated client, and `useSendMessage.ts`
still assigns `new Date().toISOString()` to it). Verified this is pre-existing by stashing this
task's changes and rebuilding on the base commit (`4c3addd`): the exact same `TS2322` error
occurs. Also verified in isolation that fixing just that one line surfaces a second, deeper type
error in `useSendMessage.ts` (`MessageDto` requiring `init`/`toJSON` because it's now a generated
class, not a plain interface) — confirming this is a substantial pre-existing gap fully out of
scope for this task, to be resolved by `route-usesendmessage-hook`. Per the task's own
instructions ("do not touch files outside the scope of this task unless required to make the
build pass" — required only for failures *caused by this change*), `useSendMessage.ts` was left
untouched. No diagnostic edits were committed (verified `git diff --stat` shows zero changes to
that file after the investigation).

`npm run lint` (via `npx eslint` on the two changed files) is clean. `dotnet build`/`dotnet
format` were not run since this task touches only frontend TypeScript files.

Note: `frontend/node_modules` was not present in the worktree at task start and had to be
installed with `npm install --legacy-peer-deps` (the root `package.json` pins TypeScript 4.9.5
while `react-i18next@15.7.4`'s peer dependency wants TypeScript `^5`, so plain `npm ci`/`npm
install` fails with an ERESOLVE conflict — pre-existing in the repo, not introduced by this
change). No `package.json`/`package-lock.json` changes resulted (`git status` confirmed clean).

## Status
DONE_WITH_CONCERNS
