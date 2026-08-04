# Plan — Expedition: replace `(apiClient as any).http.fetch` with typed generated client calls

## Summary

`useExpeditionList.ts` (`useRunExpeditionListPrintFix`, `usePrintExpeditionOrder`) and `useCarrierCooling.ts`
(`getMatrix`, `setCooling`) reach into private fields of the NSwag-generated `ApiClient`
(`(apiClient as any).baseUrl`, `(apiClient as any).http.fetch`) instead of calling the typed methods the
generator already produced for these exact routes. This is a straight refactor to the pattern already used
correctly by the sibling hook `useExpeditionListArchive.ts` — no behavior change is intended beyond what the
investigation below forces.

## Context

`docs/development/api-client-generation.md` explicitly bans `(apiClient as any).http.fetch` /
`(apiClient as any).baseUrl` (lines 128, 147, 209) because it bypasses compile-time checking of request/response
shapes and breaks silently on NSwag regeneration. Four prior fixes in the same review series already applied this
same mechanical change (FinancialOverview #3494, Manufacture #3802/#3810, Photobank #3815, DataQuality #3816).
This is part #11 continuing that series for the Expedition module — the last two of three hooks in this part
still need it (`useExpeditionListArchive.ts` was already correct).

## Investigation findings (read before implementing)

1. **Generated methods exist and match exactly**:
   - `carrierCooling_GetMatrix(): Promise<GetCarrierCoolingMatrixResponse>` — `api-client.ts:1728`
   - `carrierCooling_SetCooling(request: SetCarrierCoolingRequest): Promise<SetCarrierCoolingResponse>` — `api-client.ts:1762`
   - `expeditionList_RunFix(): Promise<RunExpeditionListPrintFixResponse>` — `api-client.ts:3483`
   - `expeditionList_PrintOrder(request: PrintExpeditionOrderRequest): Promise<PrintExpeditionOrderResponse>` — `api-client.ts:3517`
   - The generated response classes (`GetCarrierCoolingMatrixResponse`, `CarrierGroupDto`, `CarrierCoolingRowDto`,
     `Carriers`, `DeliveryHandling`, `Cooling`) are structurally identical to the hand-rolled interfaces
     currently duplicated in `useCarrierCooling.ts:5-29` — those duplicates should be deleted, not kept.

2. **HTTP status behavior differs by endpoint — verified against the controllers, not assumed**:
   - `ExpeditionListController.RunFix` and `.PrintOrder` (`backend/.../ExpeditionListController.cs:23-38`) both call
     `return Ok(response);` **unconditionally** — status is always 200, regardless of `response.Success`.
     This means the stale comment in `useExpeditionList.ts:46-47` ("mapped to 4xx by the ErrorCodes
     HttpStatusCode attribute") does **not** describe current backend behavior for these two routes — it never
     hits a non-2xx branch. `usePrintExpeditionOrder` can just await the typed call and read
     `response.success/errorCode/params` directly, exactly like the already-correct
     `useReprintExpeditionList` in `useExpeditionListArchive.ts:82-100` does for the same reason.
   - `CarrierCoolingController.GetMatrix` also always returns `Ok(...)` (`CarrierCoolingController.cs:22-27`) — no
     error-status branch needed for `getMatrix`.
   - `CarrierCoolingController.SetCooling` (`CarrierCoolingController.cs:29-37`) **does** call
     `HandleResponse(response)`, which maps `ErrorCodes` to real HTTP status codes (400/404/401/403/503/500) via
     `BaseApiController.HandleResponse`. So `setCooling` genuinely can receive a non-2xx response. The generated
     client's `carrierCooling_SetCooling` already throws a `SwaggerException` in that case (via
     `processCarrierCooling_SetCooling` → `throwException`), which is the same outcome the current hand-written
     `if (!response.ok) throw new Error(...)` produces (a rejected promise) — just via a richer exception type.
     `CoolingTab.tsx` (the only consumer of `useSetCarrierCooling`) does not read the error's message anywhere,
     so this switch is behavior-preserving for the actual call site.

3. **Consumers to re-check after the change** (must keep working, not necessarily touched):
   - `PrintOrderModal.tsx:42-54` reads `result.success` / `result.errorCode` / `result.params` from the *resolved*
     value on the happy path, and shows a generic message only on a thrown rejection. Since `PrintExpeditionOrderResponse`
     already carries `success`/`errorCode`/`params` (it extends `BaseResponse`), no changes are needed there —
     but this is the behavior the new `usePrintExpeditionOrder` must preserve.
   - `CoolingTab.tsx` and `CarrierCoolingMatrix.tsx` consume `useCarrierCoolingMatrix`/`useSetCarrierCooling` only
     through the hook's public shape (`data.groups`, `mutate`, `isPending`); as long as the returned shape is
     unchanged, no consumer edits are needed.
   - `ExpeditionListArchivePage.tsx` consumes `useRunExpeditionListPrintFix` — only needs `{ totalCount }`, which
     `RunExpeditionListPrintFixResponse` (has `totalCount`, `skippedCount`) still satisfies.

4. **Existing tests will break and need rewriting, not just adjusting**:
   - `useExpeditionList.test.ts` currently mocks `getAuthenticatedApiClient` to return `{ baseUrl, http: { fetch } }`
     and asserts on raw fetch calls/URLs (lines 32-85). It only covers `useRunExpeditionListPrintFix`, not
     `usePrintExpeditionOrder` — the latter has no direct hook test today (only indirectly via
     `PrintOrderModal`/`ExpeditionListArchivePage` tests, if any). After the change, both hooks call typed client
     methods, so tests must switch to mocking `expeditionList_RunFix` / `expeditionList_PrintOrder` directly, in
     the style already established by `useExpeditionListArchive.test.ts:21-49` (mock the method itself, not
     `http.fetch`).
   - No dedicated `useCarrierCooling.test.ts` exists; only `CarrierCoolingMatrix.test.tsx` (presentational, mocks
     the hooks' return values, unaffected). No new test *must* be added for this task's scope, but adding one
     mirroring `useExpeditionListArchive.test.ts` is reasonable given the type duplication being removed.

## Functional requirements

- **FR-1**: `useRunExpeditionListPrintFix` (`useExpeditionList.ts:9-31`) calls `client.expeditionList_RunFix()`
  instead of the manual fetch, and returns `{ totalCount }` sourced from the typed response.
  *Acceptance*: no reference to `(apiClient as any)` remains in the file; `dotnet`/`npm` type-check passes;
  updated unit test asserts `expeditionList_RunFix` is called with no arguments and the resolved `totalCount`
  is surfaced.
- **FR-2**: `usePrintExpeditionOrder` (`useExpeditionList.ts:33-63`) calls
  `client.expeditionList_PrintOrder(new PrintExpeditionOrderRequest({ orderCode }))` and returns
  `{ success, errorCode, params }` read off the resolved (always-200) response, matching the
  `useReprintExpeditionList` pattern. The now-unreachable "read body on non-ok status" branch is removed since
  the backend never returns non-2xx from this route (finding #2 above) — do not invent a defensive path for a
  status the controller cannot produce.
  *Acceptance*: updated/added test drives a resolved response with `success: false, errorCode: 'X'` and asserts
  the hook returns it unchanged (no thrown error); `PrintOrderModal.tsx` behavior is unchanged (manually verified
  or covered by its existing test, if any).
- **FR-3**: `getMatrix` (`useCarrierCooling.ts:35-46`) calls `client.carrierCooling_GetMatrix()` and returns its
  result directly (or a minimal reshape only if the generated type's optionality — e.g. `groups?: CarrierGroupDto[]`
  — needs defaulting to match current non-optional consumer expectations in `CarrierCoolingMatrix.tsx`).
  *Acceptance*: `useCarrierCoolingMatrix` still resolves `{ groups: [...] }` with the same shape consumers rely on.
- **FR-4**: `setCooling` (`useCarrierCoolingCooling.ts:48-62`) calls
  `client.carrierCooling_SetCooling(new SetCarrierCoolingRequest({ carrier, deliveryHandling, cooling, coolingText }))`.
  Non-2xx now surfaces as the generated client's thrown `SwaggerException` instead of a hand-rolled `Error` —
  acceptable per finding #2, since no consumer inspects the error's shape.
  *Acceptance*: optimistic-update logic in `useSetCarrierCooling` (`onMutate`/`onError`/`onSettled`,
  lines 74-115) is untouched and still rolls back on any rejection, whatever its type.
- **FR-5**: Remove the hand-duplicated types in `useCarrierCooling.ts:9-29`
  (`CarrierCoolingRowDto`, `CarrierGroupDto`, `GetCarrierCoolingMatrixResponse`, `SetCarrierCoolingRequest`) in
  favor of importing the generated equivalents from `../generated/api-client`, re-exporting only the string-literal
  unions (`Carriers`, `DeliveryHandling`, `Cooling`) if they aren't already exported by the generated client (check
  first — NSwag typically does generate these enums/unions too, in which case drop the hand-rolled ones as well).
  *Acceptance*: `grep -rn "CarrierCoolingRowDto\|GetCarrierCoolingMatrixResponse" frontend/src` shows only the
  generated-client definition and its import sites, no second hand-written definition.

## Non-functional requirements

- No change to bundle size/perf expectations beyond the removed manual fetch plumbing.
- Preserve type safety: no new `as any` casts introduced anywhere in either file.
- Keep the fix mechanical and scoped — do not touch `useExpeditionListArchive.ts` (already correct) or unrelated
  hooks in other modules.

## Data model

No new entities. Uses existing generated DTOs: `GetCarrierCoolingMatrixResponse`, `CarrierGroupDto`,
`CarrierCoolingRowDto`, `SetCarrierCoolingRequest`, `SetCarrierCoolingResponse`, `RunExpeditionListPrintFixResponse`,
`PrintExpeditionOrderRequest`, `PrintExpeditionOrderResponse` — all already generated in
`frontend/src/api/generated/api-client.ts`.

## Interfaces

No backend/API surface changes. Frontend-only: the four hook implementations change their transport from raw
`fetch` to the generated client's typed methods; public hook signatures/return shapes are unchanged (verified
against every current consumer in the investigation above).

## Dependencies and scope

- Depends on the generated client already containing the four methods (confirmed present, no regeneration
  needed).
- In scope: `useExpeditionList.ts`, `useCarrierCooling.ts`, and their test files
  (`useExpeditionList.test.ts`, and a new `useCarrierCooling.test.ts` if added).
- Out of scope: any backend controller change (the `PrintOrder`/`RunFix` "always 200" behavior and the stale
  hook comment describing 4xx mapping are pre-existing backend/comment inconsistencies, not part of this frontend
  fix — flag but do not fix as part of this task unless asked). Also out of scope: `useExpeditionListArchive.ts`
  (already correct), and any other module's hooks.

## Rough plan

1. Rewrite `useExpeditionList.ts`: replace both mutation bodies with `client.expeditionList_RunFix()` and
   `client.expeditionList_PrintOrder(new PrintExpeditionOrderRequest({ orderCode }))`; drop the stale comment
   about 4xx mapping since it no longer applies; drop the manual `!response.ok` branches that can no longer be
   reached.
2. Rewrite `useCarrierCooling.ts`: replace `getMatrix`/`setCooling` bodies with the typed client calls; delete
   the hand-duplicated interfaces, importing the generated types instead (check whether `Carriers`/
   `DeliveryHandling`/`Cooling` unions are already exported from `api-client.ts` before deciding whether to keep
   the local ones).
3. Update `useExpeditionList.test.ts` to mock `expeditionList_RunFix`/`expeditionList_PrintOrder` on the client
   object (per the `useExpeditionListArchive.test.ts` pattern) instead of `http.fetch`; add coverage for
   `usePrintExpeditionOrder` (currently untested) including the `success: false` branch.
   Add `useCarrierCooling.test.ts` covering `getMatrix` success and `setCooling` success + thrown-error paths,
   or confirm existing `CarrierCoolingMatrix.test.tsx` coverage is sufficient before skipping it.
4. Run `npm run build` + `npm run lint` in `frontend/`, and the relevant Jest suites
   (`useExpeditionList.test.ts`, `useCarrierCooling.test.ts` if added, `CarrierCoolingMatrix.test.tsx`,
   `ExpeditionListArchivePage.test.tsx`) to confirm no regressions.
5. Manually sanity-check (or via existing component tests) that `PrintOrderModal` and `CoolingTab` still render
   and handle success/failure the same way.

## Open questions

- Whether `Carriers`/`DeliveryHandling`/`Cooling` string-literal unions are already emitted by NSwag under the
  same names — resolved by inspection during implementation (step 2); default assumption: reuse the generated
  ones and delete the hand-rolled duplicates if they match, otherwise keep the local unions only (not the DTOs).
- Whether to add a new `useCarrierCooling.test.ts` file (no direct hook test exists today) — default: add one,
  since the type duplication removal makes the hook's typed-client usage worth locking down, mirroring the
  precedent set by `useExpeditionListArchive.test.ts`.
- The stale "mapped to 4xx" comment in the current `useExpeditionList.ts` and the fact `PrintExpeditionOrderResponse`/
  `RunExpeditionListPrintFixResponse` controllers never call `HandleResponse` (unlike `CarrierCoolingController.SetCooling`)
  looks like a latent backend inconsistency (business failures probably *should* map to a non-200 status the way
  `SetCooling` does, for consistency across the module) — flagging for a separate arch-review item rather than
  fixing here, since fixing it would be a backend behavior change outside this task's stated scope.
