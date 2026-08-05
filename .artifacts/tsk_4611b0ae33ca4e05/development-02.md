# Development — Revision 02

## What this round fixes

`review-01.md` (blocking) found that the backend-side fix from `deed5b4d`/`development-01.md` had
no effect on the operator: `ChangeTransportBoxStateHandler` and siblings now return the 4 new typed
`ErrorCodes` (`TransportBoxCodeRequired`, `TransportBoxCodeInvalidFormat`, `TransportBoxEmpty`,
`TransportBoxInvalidStateTransition`), but the frontend's generated
`frontend/src/api/generated/api-client.ts` `ErrorCodes` enum was never regenerated to include them.
`errorHandler.ts`'s `getErrorMessage` gates its i18n lookup on membership in that *generated* enum
(`typeof errorCode === "string" && errorCode in ErrorCodes`), not on `i18n.ts` directly — so all 4
new codes fell through to the untranslated `"Nastala chyba (neznámý kód: ...)"` fallback, which is
actually a step backward from the pre-fix `"Chyba validace"`.

The review also noted `docs/development/api-client-generation.md` describes a `prebuild` npm script
that auto-regenerates the client, but `frontend/package.json` has no such script today — confirmed
again this round (`grep -n "prebuild\|generate-client" frontend/package.json` → no matches). The
generated file is a manually-committed artifact; the fix requires committing it explicitly.

## Files changed this round

- **`frontend/src/api/generated/api-client.ts`** — added the 4 missing `ErrorCodes` enum members
  (`TransportBoxCodeRequired`, `TransportBoxCodeInvalidFormat`, `TransportBoxEmpty`,
  `TransportBoxInvalidStateTransition`), matching `backend/.../ErrorCodes.cs` 1406–1409.

  I ran the full NSwag regeneration (`dotnet msbuild backend/src/Anela.Heblo.API
  -t:GenerateFrontendClientManual`) to confirm the 4 codes appear, per the review's instruction —
  but the full regen also pulled in ~90 lines of unrelated drift from other in-flight backend work
  (`ManufactureOrder_GetProtocolPdf` return type, `RemoveItemFromBox`'s new `amount` param,
  `RecurringJobDto.timeZoneId`, `GenerateArticleRequest` required-field changes, a new
  `ManufactureOrderNotCompleted` error code, a 401 branch on `GetDepartments`). None of that belongs
  to this task or was touched by `deed5b4d`. Per the review's own fallback ("flag that separately")
  and `CLAUDE.md`'s surgical-changes rule, I reverted the full regen and hand-applied only the 4
  enum lines — same generated shape NSwag would produce for these members, zero unrelated surface
  change. Confirmed via `git diff --stat`: 4 lines added, nothing else touched in that file.

- **`frontend/src/utils/__tests__/errorHandler.test.ts`** — added 4 new `getErrorMessage` test
  cases, one per new code, asserting against the **real, now-updated generated enum** (not a mock),
  so a future regression in this enum/i18n wiring fails a test instead of silently reaching
  production:
  - `TransportBoxCodeRequired` → `"Kód boxu je povinný"`
  - `TransportBoxCodeInvalidFormat` with `{code: "xyz"}` → the Czech format-hint message with `xyz`
    substituted
  - `TransportBoxEmpty` with `{code: "B001"}` → the Czech empty-box message with `B001` substituted
  - `TransportBoxInvalidStateTransition` with `{currentState, allowedStates}` → both substituted
  Also added `TransportBoxEmpty` to the existing "representative codes from each module" coverage
  test.

## Why this closes the gap end-to-end

Previously, `getErrorMessage("TransportBoxEmpty", {code: "B001"})` returned
`"Nastala chyba (neznámý kód: TransportBoxEmpty)"` because `"TransportBoxEmpty" in ErrorCodes` was
`false` against the stale generated enum. With the enum updated, the same call now returns
`"Box B001 neobsahuje žádné položky — nelze jej odeslat prázdný"` — verified by an actual passing
test against the real `ErrorCodes` import (`frontend/src/types/errors.ts` → re-exports from
`api-client.ts`), not a mock.

## How to verify

Backend (unchanged by this round, re-verified after a fresh full build to rule out any interaction
with the client regeneration):
```
export PATH="/Users/rem/.dotnet:$PATH"
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Transport|FullyQualifiedName~LocalizationCoverageTests|FullyQualifiedName~Architecture"
dotnet format Anela.Heblo.sln --verify-no-changes
```
Results: build 0 errors (unrelated pre-existing `AccessMatrixGen` tool crash during the build,
confirmed identical on `main`/unrelated to this change and treated as a non-fatal MSBuild warning);
273/273 tests passed; `dotnet format` produced no diff.

Frontend:
```
cd frontend
CI=true npx react-scripts test src/utils/__tests__/errorHandler.test.ts --watchAll=false
CI=true npm run build
npx eslint src --ext .ts,.tsx
```
Results:
- New errorHandler test suite: 26/26 passed, including the 4 new TransportBox cases.
- `npm run build`: compiled successfully, bundle +54 B (matches the tiny enum addition; no other
  generated-client drift landed).
- `eslint`: 188 pre-existing problems (175 errors/13 warnings), same count as baseline — zero new
  issues from this change.

Manual trace confirming the fix reaches the operator: `errorHandler.ts:getErrorMessage("TransportBoxEmpty", {code: "B001"})`
→ `"TransportBoxEmpty" in ErrorCodes` is now `true` (enum member added) → `i18n.t("errors.TransportBoxEmpty")`
resolves to the Czech template → `formatMessage` substitutes `{code}` → final string
`"Box B001 neobsahuje žádné položky — nelze jej odeslat prázdný"`, replacing both the original opaque
`"Chyba validace"` and the round-1 regression `"Nastala chyba (neznámý kód: TransportBoxEmpty)"`.

## Scope note carried over

I did not touch the `frontend/package.json` prebuild/CI wiring gap the review flagged (no
`prebuild`/`generate-client` script exists, so the generated client remains a manually-maintained,
committed artifact). That's a separate process concern from this task's finding (operator-visible
error messages) and out of scope for a surgical fix — flagging it here in case it should become its
own follow-up.
