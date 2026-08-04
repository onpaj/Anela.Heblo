# Plan — Photobank hooks: bypass of typed generated client

## Summary

`frontend/src/api/hooks/usePhotobank.ts` and `usePhotobankSettings.ts` reach into
`(apiClient as any).baseUrl` / `(apiClient as any).http.fetch` for every request instead of
calling the generated `photobank_*` methods on `ApiClient`, and hand-declare DTOs that already
exist in `frontend/src/api/generated/api-client.ts`. This plan migrates both files onto the typed
client, per the pattern already applied to Bank/Configuration hook fixes, and fixes the small
ripple this causes in three components that consume `PhotoDto.lastModifiedAt` /
`IndexRootDto.createdAt`/`lastIndexedAt` (which are `Date` in the generated client, `string` in the
hand-rolled one).

## Context

`docs/development/api-client-generation.md` bans `(apiClient as any)` private-field access
specifically because it breaks silently — with no compile error — if NSwag ever renames its
internal `http`/`baseUrl` fields. It also warns that hand-declared response DTOs plus
`data.tags/roots/rules ?? []` fallbacks mask backend contract drift (a renamed field yields an
empty list at runtime, not a build failure). Both files exhibit exactly this pattern. The same
class of bug has already been fixed in FinancialOverview (#3494), Analytics (#3333), Bank (#3395),
Configuration (#3750), and Manufacture (#3802/#3810); this is the same fix applied to Photobank.

One endpoint — `bulk-tag`'s business-rule branch — genuinely cannot be expressed as a
non-throwing typed call today (see FR-2), which is the one place this plan intentionally deviates
from "just call the generated method directly."

## Functional requirements

**FR-1 — `usePhotobank.ts`: route the eight "plain" hooks through the generated client**

Replace `getClientAndBaseUrl`/`apiFetch`/`apiPost`/`apiDelete`/`buildPhotosUrl` and all
`(apiClient as any)` access with `getAuthenticatedApiClient()` + the matching generated method:

| Hook | Generated method |
|---|---|
| `usePhotos` | `photobank_GetPhotos(tags, search, useRegex, withoutTags, page, pageSize)` |
| `usePhotoTags` | `photobank_GetTags()` |
| `useAddPhotoTag` | `photobank_AddPhotoTag(id, new AddPhotoTagBody({ tagName }))` |
| `useRemovePhotoTag` | `photobank_RemovePhotoTag(id, tagId)` |
| `useCreateTag` | `photobank_CreateTag(new CreateTagBody({ name }))` |
| `useDeleteTag` | `photobank_DeleteTag(id)` |
| `useBulkAddPhotoTagByIds` | `photobank_BulkAddPhotoTagByIds(new BulkAddPhotoTagByIdsBody({ photoIds, tagName }))` |
| `useRetagPhotos` | `photobank_RetagPhotos(new RetagPhotosBody({ photoIds, clearExistingAiTags }))` |

`usePhotoTags` currently reads `data.tags ?? []` from the raw JSON; the generated
`photobank_GetTags()` returns a `GetTagsResponse` whose `.tags` is already a typed
`TagWithCountDto[]` — return `response.tags ?? []` from the parsed object instead of a raw fetch
body.

Acceptance:
- `grep -n "apiClient as any" frontend/src/api/hooks/usePhotobank.ts` returns nothing.
- Each of the 8 hooks above calls its listed generated method (verify by reading the diff).
- `PhotoDrawer.tsx`, `PhotobankPage.tsx` (both call `useAddPhotoTag`, `useRemovePhotoTag`,
  `useRetagPhotos`, `useBulkAddPhotoTagByIds`) need no call-site changes — hook signatures
  (params in, `mutateAsync`/`mutate` behavior) stay the same.

**FR-2 — `useBulkAddPhotoTag`: keep the structured business-outcome contract, use `photobank_BulkAddPhotoTag`**

`BulkTagDialog.tsx` depends on the *body* of a 400 response (not just "it failed"): on limit
exceeded (`errorCode === 2606`) it reads `result.params?.Count` / `result.params?.Limit` to render
a specific message; on any other outcome it shows a generic error. The backend controller
(`PhotobankController.BulkAddPhotoTag`) returns this via `HandleResponse`, i.e.
`BadRequest(bulkAddPhotoTagResponseInstance)` — the 400 body *is* a `BulkAddPhotoTagResponse`
(camelCase JSON: `success`, `errorCode`, `params`, `tagId`, `tagName`, `addedCount`,
`alreadyTaggedCount`), not a `ProblemDetails`.

The generated `photobank_BulkAddPhotoTag`'s `processPhotobank_BulkAddPhotoTag` parses the 400 body
as `ProblemDetails.fromJS(...)` and throws that parsed object directly (`throwException` throws
`result`, not a wrapping exception, when `result` is non-null). Because `ProblemDetails` has a
`[key: string]: any` index signature and its `init()` copies every property from the raw JSON
before overwriting the five declared `ProblemDetails` fields, the thrown object still carries
`success`/`errorCode`/`params`/`tagId`/`tagName`/`addedCount`/`alreadyTaggedCount` as own
properties — just not reflected in its TS type. This means the existing consumer contract *can* be
preserved through the generated client, via `try/catch` (this is the documented "discriminate on
the existing `BaseResponse.success` + `errorCode` envelope" escape valve in
`api-client-generation.md`), without falling back to `getApiBaseUrl()`/`getAuthenticatedFetch()`.

Implementation:
```ts
mutationFn: async (params): Promise<BulkAddPhotoTagResult> => {
  const apiClient = getAuthenticatedApiClient();
  try {
    const response = await apiClient.photobank_BulkAddPhotoTag(
      new BulkAddPhotoTagBody({ tags: params.tags, search: params.search, tagName: params.tagName }),
    );
    return { success: true, tagId: response.tagId, tagName: response.tagName,
             addedCount: response.addedCount, alreadyTaggedCount: response.alreadyTaggedCount };
  } catch (e) {
    const err = e as Partial<BulkAddPhotoTagResult> & { success?: boolean };
    if (err && err.success === false && typeof err.errorCode === "number") {
      return { success: false, errorCode: err.errorCode, params: err.params };
    }
    throw e;
  }
},
```
Rethrow anything that isn't a recognizable business-outcome body (403, 500, network error) so
`isError`/`mutateAsync` rejection still behaves as before.

Acceptance:
- `grep -n "apiClient as any" frontend/src/api/hooks/usePhotobank.ts` returns nothing (covers this
  hook too).
- `BulkTagDialog.tsx` needs **no changes** — same `result.success`/`result.errorCode`/
  `result.params?.Count`/`result.params?.Limit`/`result.tagName`/`result.addedCount`/
  `result.alreadyTaggedCount` contract.
- A test exercising the 2606 path (limit exceeded) and the happy path both pass.

**FR-3 — `usePhotobankSettings.ts`: route all seven hooks through the generated client**

| Hook | Generated method |
|---|---|
| `useIndexRoots` | `photobank_GetRoots()` → `response.roots ?? []` |
| `useAddIndexRoot` | `photobank_AddRoot(new AddRootBody({ sharePointPath, displayName: displayName ?? undefined, driveId }))` |
| `useDeleteIndexRoot` | `photobank_DeleteRoot(id)` |
| `useTagRules` | `photobank_GetRules()` → `response.rules ?? []` |
| `useAddTagRule` | `photobank_AddRule(new AddRuleBody({ pathPattern, tagName, sortOrder }))` |
| `useDeleteTagRule` | `photobank_DeleteRule(id)` |
| `useReapplyTagRules` | `photobank_ReapplyRules()` |

`AddIndexRootInput.displayName` is `string | null` (as called from `IndexRootsTab.tsx`:
`displayName: displayName.trim() || null`); the generated `AddRootBody.displayName` is
`string | undefined`. Normalize `null → undefined` inside the hook when constructing the body —
do **not** change `IndexRootsTab.tsx` or `AddIndexRootInput`.

`useReapplyTagRules`'s local `ReapplyRulesResult { photosUpdated: number }` interface duplicates
the generated `ReapplyRulesResponse { photosUpdated }`; nothing outside this hook file imports
`ReapplyRulesResult` (verified), so drop the local interface and let the hook return
`ReapplyRulesResponse` directly — `TagRulesTab.tsx` only reads `.photosUpdated`, unaffected.

Acceptance:
- `grep -n "apiClient as any" frontend/src/api/hooks/usePhotobankSettings.ts` returns nothing.
- `IndexRootsTab.tsx` and `TagRulesTab.tsx` need no call-site changes.

**FR-4 — Replace hand-declared DTOs with re-exports of the generated types**

Delete the hand-written `TagDto`, `PhotoDto`, `TagWithCountDto`, `GetPhotosResponse` interfaces
from `usePhotobank.ts` and `IndexRootDto`, `TagRuleDto` from `usePhotobankSettings.ts`. Re-export
the generated classes under the same names so existing `import type { PhotoDto } from
".../usePhotobank"` call sites (`PhotoDrawer.tsx`, `BulkTagDialog.tsx`, `PhotobankPage.tsx`)
compile unchanged:
```ts
export type { PhotoDto, TagDto, TagWithCountDto, GetPhotosResponse } from "../generated/api-client";
```
and equivalently for `IndexRootDto`, `TagRuleDto` in `usePhotobankSettings.ts`.

Keep the small hand-declared *input* shapes as-is (`GetPhotosParams`, `AddIndexRootInput`,
`AddTagRuleInput`, `BulkAddPhotoTagParams`, `BulkAddPhotoTagByIdsParams`, `RetagPhotosRequest`,
`BulkAddPhotoTagResult`) — these are hook-local parameter/result wrapper shapes, not duplicated
backend response DTOs, and nothing in `docs/development/api-client-generation.md` requires
removing them. `BulkAddPhotoTagResult` in particular must stay (see FR-2).

Acceptance: `grep -n "^export interface \(TagDto\|PhotoDto\|TagWithCountDto\|GetPhotosResponse\|IndexRootDto\|TagRuleDto\)" frontend/src/api/hooks/usePhotobank*.ts` returns nothing.

**FR-5 — Fix the `Date` vs `string` ripple from generated DTOs**

Generated `PhotoDto.lastModifiedAt` and `IndexRootDto.createdAt`/`lastIndexedAt` are typed `Date`
(ISO-8601 strings parsed via NSwag's date handling), not `string` as in the hand-rolled interfaces.
Three call sites feed `photo.lastModifiedAt` into `PhotoThumbnail`'s `modifiedAt: string` prop
(used for a cache-busting `new Date(modifiedAt).getTime()`):
- `PhotoGrid.tsx:111` — `modifiedAt={photo.lastModifiedAt}`
- `PhotoList.tsx:119` — `modifiedAt={photo.lastModifiedAt}`
- `PhotoDrawer.tsx:92` — `modifiedAt={photo.lastModifiedAt}`

Change each to `modifiedAt={photo.lastModifiedAt?.toISOString() ?? ""}` (or widen
`PhotoThumbnail`'s prop to accept `Date`, whichever the dev step judges tidier — passing
`.toISOString()` at the call site is the smaller diff and doesn't touch a shared component's
public prop type).

The other two direct-Date-consumption sites do **not** need changes — `new Date(x)` accepts a
`Date` argument, so these keep compiling and behaving correctly:
- `PhotoList.tsx:146` — `new Date(photo.lastModifiedAt).toLocaleDateString("cs-CZ")`
- `PhotoDrawer.tsx:105` — `new Date(photo.lastModifiedAt).toLocaleDateString("cs-CZ")`
- `IndexRootsTab.tsx:85` — `new Date(root.lastIndexedAt).toLocaleDateString("cs-CZ")`

Acceptance: `npm run build` produces no new TypeScript errors in these five files.

**FR-6 — Update tests to mock the generated client instead of `{baseUrl, http:{fetch}}`**

`usePhotobank.test.ts` and `usePhotobankSettings.test.ts` currently mock
`getAuthenticatedApiClient()` to return `{ baseUrl, http: { fetch: mockFetch } }` and assert on
`mockFetch`'s URL/method/body. Rewrite both, following the already-migrated
`useBankStatements.test.ts` pattern (`mockAuthenticatedApiClient(mockClient)` +
`createQueryClientWrapper()` from `frontend/src/api/testUtils.ts`): mock each `photobank_*` method
directly and assert on call arguments instead of on a raw URL string. Add/keep a case that drives
`useBulkAddPhotoTag` through both the success path and the 2606-limit-exceeded catch path (FR-2).

Component tests (`PhotoGrid.test.tsx`, `PhotoList.test.tsx`, `PhotobankPage.test.tsx`,
`PhotobankPage.selection.test.tsx`) build photo fixtures with `lastModifiedAt` as a plain string
and are untyped (`makePhoto = (overrides = {}) => ({...})`, or an untyped `jest.mock(...)` factory)
— confirmed no compile-time dependency on `PhotoDto`, and `new Date("...")` still works at
runtime, so **no changes needed** here; call this out explicitly rather than touching them.

Acceptance: `npm test -- usePhotobank usePhotobankSettings` and the four component test files
listed above all pass unchanged in behavior.

## Non-functional requirements

- No behavioral, URL, or authorization change — `[FeatureAuthorize(Feature.Marketing_Photobank, ...)]`
  attributes on `PhotobankController` are untouched; this is a client-side refactor only.
- No change to Czech-language UI copy or the `BULK_TAG_LIMIT_EXCEEDED_CODE = 2606` business rule.
- Compile-time safety is the point of this change: after the migration, an NSwag field rename or a
  backend DTO shape change must surface as a `tsc` build error, not a silent empty-list/`undefined`
  at runtime.

## Data model

No new entities. Consumes existing generated types: `GetPhotosResponse`, `PhotoDto`, `TagDto`,
`GetTagsResponse`, `TagWithCountDto`, `GetRootsResponse`, `IndexRootDto`, `GetRulesResponse`,
`TagRuleDto`, plus request bodies (`AddPhotoTagBody`, `BulkAddPhotoTagBody`,
`BulkAddPhotoTagByIdsBody`, `RetagPhotosBody`, `CreateTagBody`, `AddRootBody`, `AddRuleBody`) and
response types (`AddPhotoTagResponse`, `RemovePhotoTagResponse`, `BulkAddPhotoTagResponse`,
`BulkAddPhotoTagByIdsResponse`, `RetagPhotosResponse`, `CreateTagResponse`, `DeleteTagResponse`,
`AddRootResponse`, `DeleteRootResponse`, `AddRuleResponse`, `DeleteRuleResponse`,
`ReapplyRulesResponse`) — all already present in `frontend/src/api/generated/api-client.ts`.

## Interfaces

No backend endpoint changes. Exported hook names, parameters, and `useQuery`/`useMutation` return
shapes in `usePhotobank.ts`/`usePhotobankSettings.ts` are preserved; only their internals and the
re-exported DTO source change. Two DTO fields change TS type (`Date` instead of `string`), handled
per FR-5.

## Dependencies and scope

**In scope:**
- `frontend/src/api/hooks/usePhotobank.ts`, `usePhotobankSettings.ts` and their two test files.
- The FR-5 ripple: `PhotoGrid.tsx`, `PhotoList.tsx`, `PhotoDrawer.tsx` (one-line change each).

**Explicitly out of scope:**
- `photobank_GetThumbnail` / the `photos/{id}/thumbnail/{size}` endpoint — it's binary/streaming
  and isn't touched by either hook file; no photobank hook currently wraps it, so there's nothing
  to migrate here.
- Any backend change to make `BulkAddPhotoTagResponse`'s 400 branch a properly-typed non-throwing
  NSwag branch (see Open Questions) — that's a template/contract change belonging to a separate
  backend-facing issue, not this frontend hook fix.
- `IndexRootsTab.tsx`, `TagRulesTab.tsx`, `TagsTab.tsx`, `PhotobankPage.tsx` beyond what FR-3/FR-5
  require — their call sites already match the hooks' existing public contracts.

## Rough plan

1. `usePhotobankSettings.ts`: swap in the 7 generated methods (FR-3), normalize
   `displayName: null → undefined`, drop the local `ReapplyRulesResult` interface, re-export
   `IndexRootDto`/`TagRuleDto` from the generated client (FR-4).
2. `usePhotobank.ts`: swap in the 7 straightforward generated methods (FR-1), then implement the
   try/catch business-outcome handling for `useBulkAddPhotoTag` (FR-2), and re-export
   `PhotoDto`/`TagDto`/`TagWithCountDto`/`GetPhotosResponse` (FR-4).
3. Fix the three `modifiedAt` call sites in `PhotoGrid.tsx`/`PhotoList.tsx`/`PhotoDrawer.tsx`
   (FR-5).
4. Rewrite `usePhotobank.test.ts` and `usePhotobankSettings.test.ts` to mock the generated client
   methods (FR-6), including a case for the 2606 bulk-tag-limit path.
5. Run `dotnet build`/`dotnet format` (no backend change expected, but confirm untouched),
   `npm run build`, `npm run lint`, and the full frontend test suite; confirm
   `grep -rn "apiClient as any" frontend/src/api/hooks/usePhotobank*.ts` is empty.

## Open questions

- **`BulkAddPhotoTagResponse`'s 400 branch isn't a typed non-throwing NSwag branch** — the
  generated client treats it as an exception path via `ProblemDetails.fromJS`, which happens to
  still carry the real fields at runtime (untyped) because of `ProblemDetails`'s index signature.
  This works but is fragile in the same silent-breakage sense the arch-review flags elsewhere: if a
  future NSwag regeneration or template change stops preserving unknown JSON properties on thrown
  objects, this catch-block field access breaks silently. Default: accept this as the pragmatic fix
  for *this* task (matches the documented "discriminate on the existing envelope" pattern) and flag
  it as a candidate for the proper fix — annotating `BulkAddPhotoTag`/`BulkAddPhotoTagByIds`/
  `RetagPhotos` 400 responses with `[ProducesResponseType(typeof(BulkAddPhotoTagResponse),
  StatusCodes.Status400BadRequest)]` so NSwag can (once the template override is active, per
  `nswag-templates/README.md`) emit a real typed non-throwing branch — as a separate follow-up
  issue rather than blocking this one.
- **Should `PhotoThumbnail`'s `modifiedAt` prop be widened to accept `Date` instead of converting
  at each call site?** Default: convert at the call sites (`.toISOString()`), since it's a 3-line
  change vs. touching a shared component's public prop contract and its own tests/snapshots for no
  functional gain.
