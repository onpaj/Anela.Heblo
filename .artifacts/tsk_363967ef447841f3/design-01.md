# Design — Photobank hooks: migrate off `(apiClient as any)` onto the generated client

No UI section: this is an internal refactor of two hook modules and three one-line call-site
fixes. No component is added, removed, or visually changed; every existing screen (`PhotobankPage`,
`PhotoDrawer`, `BulkTagDialog`, `IndexRootsTab`, `TagRulesTab`, `TagsTab`) keeps its current markup,
props, and behavior untouched.

## Component design

### Module boundaries (unchanged)

```
usePhotobank.ts            usePhotobankSettings.ts
  ├─ usePhotos                ├─ useIndexRoots
  ├─ usePhotoTags              ├─ useAddIndexRoot
  ├─ useAddPhotoTag            ├─ useDeleteIndexRoot
  ├─ useRemovePhotoTag         ├─ useTagRules
  ├─ useCreateTag              ├─ useAddTagRule
  ├─ useDeleteTag              ├─ useDeleteTagRule
  ├─ useBulkAddPhotoTag        └─ useReapplyTagRules
  ├─ useBulkAddPhotoTagByIds
  └─ useRetagPhotos
```

Both files keep their current public surface (hook names, parameter shapes, `useQuery`/
`useMutation` return objects). Only the internals change: the hand-rolled
`getClientAndBaseUrl`/`apiFetch`/`apiPost`/`apiDelete`/`buildPhotosUrl` helpers are deleted
entirely — every hook body becomes `const apiClient = getAuthenticatedApiClient(); return
apiClient.photobank_XXX(...)` (or a thin wrapper around it), with `getAuthenticatedApiClient`
imported from `../client` exactly as it already is. No new module, helper file, or abstraction is
introduced — the generated `ApiClient` class already **is** the "typed HTTP boundary" component;
these hooks become plain adapters from hook-shaped calls to its methods, which is the same role
`useBankStatements.ts` plays for the Bank module.

### `usePhotobank.ts` — hook → generated-method mapping

| Hook | Generated call | Notes |
|---|---|---|
| `usePhotos(params)` | `apiClient.photobank_GetPhotos(params.tags, params.search, params.useRegex, params.withoutTags, params.page, params.pageSize)` | Positional params match `GetPhotosParams` field order; `tags` defaults to `[]` → pass as-is (`string[] \| null \| undefined` accepted). |
| `usePhotoTags()` | `apiClient.photobank_GetTags()` → `response.tags ?? []` | `GetTagsResponse.tags?: TagWithCountDto[]`. |
| `useAddPhotoTag(photoId)` | `apiClient.photobank_AddPhotoTag(photoId, new AddPhotoTagBody({ tagName }))` | mutationFn param is still the bare `tagName: string`. |
| `useRemovePhotoTag(photoId)` | `apiClient.photobank_RemovePhotoTag(photoId, tagId)` | mutationFn param stays `tagId: number`. |
| `useCreateTag()` | `apiClient.photobank_CreateTag(new CreateTagBody({ name }))` | returns `CreateTagResponse`. |
| `useDeleteTag()` | `apiClient.photobank_DeleteTag(tagId)` | returns `DeleteTagResponse`. |
| `useBulkAddPhotoTagByIds()` | `apiClient.photobank_BulkAddPhotoTagByIds(new BulkAddPhotoTagByIdsBody({ photoIds, tagName }))` | mutationFn keeps `BulkAddPhotoTagByIdsParams` param type; return value discarded (`Promise<void>` behavior preserved by not returning the response). |
| `useRetagPhotos()` | `apiClient.photobank_RetagPhotos(new RetagPhotosBody({ photoIds, clearExistingAiTags }))` | mutationFn keeps `RetagPhotosRequest` param type. |
| `useBulkAddPhotoTag()` | `apiClient.photobank_BulkAddPhotoTag(new BulkAddPhotoTagBody({ tags, search, tagName }))` inside `try/catch` | see dedicated subsection below — the one hook that isn't a direct pass-through. |

All `onSuccess: () => queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photobank })` blocks are
unchanged; they don't touch the transport.

#### `useBulkAddPhotoTag` — component responsibility split

This hook is the one place a business-outcome (not just success/failure) travels through what the
generated client models as an *exception* path. Its responsibility is to translate that generated
exception shape back into the `BulkAddPhotoTagResult` value contract `BulkTagDialog.tsx` already
depends on — it is a translation boundary, not a new API surface:

```
BulkTagDialog.tsx                         useBulkAddPhotoTag (usePhotobank.ts)          ApiClient (generated)
─────────────────                         ─────────────────────────────────             ──────────────────────
mutateAsync({tags, search, tagName})  →   new BulkAddPhotoTagBody({...})            →    photobank_BulkAddPhotoTag(body)
                                                                                          │
                                                                                          ├─ 200 → BulkAddPhotoTagResponse
                                                                                          └─ 400/403 → throw ProblemDetails
                                                                                                 (carries success/errorCode/
                                                                                                  params/tagId/tagName/
                                                                                                  addedCount/alreadyTaggedCount
                                                                                                  as untyped own properties —
                                                                                                  confirmed via ProblemDetails'
                                                                                                  `[key:string]: any` + init()
                                                                                                  copy-all-properties loop)
result.success / result.errorCode  ←──    catch (e): if e.success===false and
result.params?.Count/Limit               typeof e.errorCode==="number" →
                                          return {success:false, errorCode, params}
                                          else rethrow (403/500/network)
```

Responsibility boundary: `BulkTagDialog.tsx` is unaware this translation happens — it keeps
calling `mutateAsync` and reading `result.success`/`result.errorCode`/`result.params?.Count`/
`result.params?.Limit`/`result.tagName`/`result.addedCount`/`result.alreadyTaggedCount` exactly as
today (`BulkTagDialog.tsx:69-93`, `BULK_TAG_LIMIT_EXCEEDED_CODE = 2606`). The hook is the only
component that knows the generated client throws instead of returning for this branch, and the
only component that knows why that's still safe to rely on today (documented as an accepted,
flagged risk in plan-01.md's Open Questions — no mitigation is designed here beyond a code comment
pointing at that risk, since fixing it requires a backend `[ProducesResponseType(...,
StatusCodes.Status400BadRequest)]` annotation, out of scope per plan-01.md).

Anything that isn't a recognizable `{success:false, errorCode:number}` shape (network failure, 403,
500, a 400 whose body doesn't even parse as JSON) is rethrown unchanged, so `mutateAsync`
rejection / `isError` continues to behave as it does today for those cases.

### `usePhotobankSettings.ts` — hook → generated-method mapping

| Hook | Generated call | Notes |
|---|---|---|
| `useIndexRoots()` | `apiClient.photobank_GetRoots()` → `response.roots ?? []` | `GetRootsResponse.roots?: IndexRootDto[]`. |
| `useAddIndexRoot()` | `apiClient.photobank_AddRoot(new AddRootBody({ sharePointPath, displayName: input.displayName ?? undefined, driveId }))` | Body's `displayName?: string \| undefined`; hook's `AddIndexRootInput.displayName: string \| null` is normalized at the call boundary only — `IndexRootsTab.tsx` is untouched. |
| `useDeleteIndexRoot()` | `apiClient.photobank_DeleteRoot(id)` | |
| `useTagRules()` | `apiClient.photobank_GetRules()` → `response.rules ?? []` | `GetRulesResponse.rules?: TagRuleDto[]`. |
| `useAddTagRule()` | `apiClient.photobank_AddRule(new AddRuleBody({ pathPattern, tagName, sortOrder }))` | |
| `useDeleteTagRule()` | `apiClient.photobank_DeleteRule(id)` | |
| `useReapplyTagRules()` | `apiClient.photobank_ReapplyRules()` | Return type becomes the generated `ReapplyRulesResponse` directly (drops the local `ReapplyRulesResult` interface — `TagRulesTab.tsx` reads only `.photosUpdated`, present on both). |

`ROOTS_QUERY_KEY`/`RULES_QUERY_KEY` and all `invalidateQueries` calls stay exactly as-is — cache
keys are independent of transport.

### DTO re-export boundary (`FR-4`)

Both hook files keep acting as the single import surface consuming components already use, so no
component's import line changes:

```ts
// usePhotobank.ts
export type { PhotoDto, TagDto, TagWithCountDto, GetPhotosResponse } from "../generated/api-client";

// usePhotobankSettings.ts
export type { IndexRootDto, TagRuleDto } from "../generated/api-client";
```

This is the same re-export pattern `useBankStatements.ts` already uses for
`BankStatementImportDto`/`BankStatementImportResultDto`/etc. — not a new pattern being introduced.

Hook-local *input*/*result* wrapper types (`GetPhotosParams`, `AddIndexRootInput`,
`AddTagRuleInput`, `BulkAddPhotoTagParams`, `BulkAddPhotoTagByIdsParams`, `RetagPhotosRequest`,
`BulkAddPhotoTagResult`) are not backend DTOs and are kept hand-declared, unchanged.

### Consumers requiring a one-line change (`FR-5`)

Generated `PhotoDto.lastModifiedAt` and `IndexRootDto.createdAt`/`lastIndexedAt` are typed `Date`
(NSwag `dateTimeType: "Date"`), not `string`. `PhotoThumbnail`'s `modifiedAt` prop is `string`.
Three call sites adapt at the boundary, nowhere else:

- `PhotoGrid.tsx:111` — `modifiedAt={photo.lastModifiedAt?.toISOString() ?? ""}`
- `PhotoList.tsx:119` — `modifiedAt={photo.lastModifiedAt?.toISOString() ?? ""}`
- `PhotoDrawer.tsx:92` — `modifiedAt={photo.lastModifiedAt?.toISOString() ?? ""}`

`PhotoThumbnail`'s own prop type and implementation are untouched — the conversion happens only at
these three call sites, which is a smaller, more localized change than widening a shared
component's public contract. `new Date(photo.lastModifiedAt)` / `new Date(root.lastIndexedAt)`
call sites (`PhotoList.tsx:146`, `PhotoDrawer.tsx:105`, `IndexRootsTab.tsx:85`) need no change —
`Date`'s constructor already accepts a `Date` argument.

Generated DTO fields are all optional (`id?: number`, etc. — NSwag's default TS codegen shape,
already true of every other migrated DTO in this codebase, e.g. `BankStatementImportDto`). This is
an accepted, pre-existing characteristic of the generated client, not a new gap introduced here;
no additional null-guarding is designed beyond what FR-5 already calls out for the two `Date`
fields that actually change a consumer's compile-time type.

### Test module boundary (`FR-6`)

`usePhotobank.test.ts` / `usePhotobankSettings.test.ts` move from mocking the transport shape
(`{ baseUrl, http: { fetch: mockFetch } }`) to mocking the generated client's method surface,
matching `useBankStatements.test.ts`'s established pattern:

```ts
jest.mock("../../client");

let mockClient: {
  photobank_GetPhotos: jest.Mock;
  photobank_GetTags: jest.Mock;
  photobank_AddPhotoTag: jest.Mock;
  // ...one entry per generated method the file under test calls
};

beforeEach(() => {
  mockClient = { photobank_GetPhotos: jest.fn(), /* ... */ };
  mockAuthenticatedApiClient(mockClient);
});
```

`mockAuthenticatedApiClient` and `createQueryClientWrapper` come from `frontend/src/api/testUtils.ts`
(already exist, already used by `useBankStatements.test.ts`) — no new test infrastructure needed.

For `useBulkAddPhotoTag`, the mock must express both branches of the translation boundary above:
`mockClient.photobank_BulkAddPhotoTag.mockResolvedValue(new BulkAddPhotoTagResponse({...}))` for
the happy path, and `.mockRejectedValue({ success: false, errorCode: 2606, params: { Count: "12000", Limit: "5000" } })`
for the limit-exceeded path — asserting the hook returns `{success:false, errorCode:2606,
params:{...}}` rather than rejecting.

Component tests (`PhotoGrid.test.tsx`, `PhotoList.test.tsx`, `PhotobankPage.test.tsx`,
`PhotobankPage.selection.test.tsx`) need no changes — confirmed their fixtures are untyped plain
objects with `lastModifiedAt` as a string literal, which remains valid input to `new Date(...)`.

## Data schemas

No database schema changes; no backend contract changes. This section documents the
request/response shapes now sourced from `frontend/src/api/generated/api-client.ts` instead of
hand-declared, and the one payload shape (`BulkAddPhotoTagResult`) that crosses the throw/catch
translation boundary.

### Request bodies (generated classes, constructed with `new XxxBody({...})`)

| Class | Fields | Used by |
|---|---|---|
| `AddPhotoTagBody` | `tagName?: string` | `useAddPhotoTag` |
| `CreateTagBody` | `name?: string` | `useCreateTag` |
| `BulkAddPhotoTagBody` | `tags?: string[]`, `search?: string`, `tagName?: string` | `useBulkAddPhotoTag` |
| `BulkAddPhotoTagByIdsBody` | `photoIds?: number[]`, `tagName?: string` | `useBulkAddPhotoTagByIds` |
| `RetagPhotosBody` | `photoIds?: number[]`, `clearExistingAiTags?: boolean` | `useRetagPhotos` |
| `AddRootBody` | `sharePointPath?: string`, `displayName?: string`, `driveId?: string` | `useAddIndexRoot` |
| `AddRuleBody` | `pathPattern?: string`, `tagName?: string`, `sortOrder?: number` | `useAddTagRule` |

`photobank_GetPhotos`/`photobank_RemovePhotoTag`/`photobank_DeleteTag`/`photobank_DeleteRoot`/
`photobank_DeleteRule`/`photobank_ReapplyRules` take positional scalar arguments, not a body class
(no request DTO involved).

### Response shapes (generated classes, all extend `BaseResponse { success?, errorCode?: ErrorCodes, params?: {[k:string]:string} }` except plain DTOs)

| Class | Fields | Used by |
|---|---|---|
| `GetPhotosResponse` | `items?: PhotoDto[]`, `total?`, `page?`, `pageSize?: number` | `usePhotos` |
| `PhotoDto` | `id?, sharePointFileId?, driveId?, name?, folderPath?, sharePointWebUrl?, fileSizeBytes?: number`, `lastModifiedAt?: Date`, `tags?: TagDto[]` | re-exported |
| `TagDto` | `id?: number`, `name?: string`, `source?: string` | re-exported |
| `GetTagsResponse` | `tags?: TagWithCountDto[]` | `usePhotoTags` |
| `TagWithCountDto` | `id?, name?, count?` | re-exported |
| `AddPhotoTagResponse` | `tagId?, tagName?` | `useAddPhotoTag` (not currently consumed beyond success) |
| `CreateTagResponse` | `id?, name?, alreadyExisted?: boolean` | `useCreateTag` |
| `DeleteTagResponse` | `removedAssignmentCount?: number` | `useDeleteTag` |
| `BulkAddPhotoTagResponse` | `tagId?, tagName?, addedCount?, alreadyTaggedCount?` (+ inherited `success/errorCode/params`) | `useBulkAddPhotoTag` (200 path) |
| `GetRootsResponse` | `roots?: IndexRootDto[]` | `useIndexRoots` |
| `IndexRootDto` | `id?, sharePointPath?, displayName?, driveId?, rootItemId?, isActive?: boolean`, `createdAt?: Date`, `lastIndexedAt?: Date` | re-exported |
| `AddRootResponse` | `id?: number` | `useAddIndexRoot` |
| `GetRulesResponse` | `rules?: TagRuleDto[]` | `useTagRules` |
| `TagRuleDto` | `id?, pathPattern?, tagName?, isActive?, sortOrder?` | re-exported |
| `AddRuleResponse` | `id?: number` | `useAddTagRule` |
| `ReapplyRulesResponse` | `photosUpdated?: number` | `useReapplyTagRules` |

### `BulkAddPhotoTagResult` — the one payload that crosses a throw/catch boundary

Kept as the existing hand-declared hook-local result type (not a generated DTO, since no
non-throwing generated type models this union):

```ts
export interface BulkAddPhotoTagResult {
  success: boolean;
  errorCode?: number;
  params?: Record<string, string>;
  tagId?: number;
  tagName?: string;
  addedCount?: number;
  alreadyTaggedCount?: number;
}
```

Produced two ways inside `useBulkAddPhotoTag`'s `mutationFn`:
- 200 path: `{ success: true, tagId: response.tagId, tagName: response.tagName, addedCount:
  response.addedCount, alreadyTaggedCount: response.alreadyTaggedCount }`, built from the typed
  `BulkAddPhotoTagResponse`.
- 400 business-outcome path (caught exception): `{ success: false, errorCode: err.errorCode,
  params: err.params }`, read off the untyped-but-runtime-present properties of the thrown
  `ProblemDetails` instance (backend's `params` dictionary keys are `Count`/`Limit`, PascalCase, as
  already consumed by `BulkTagDialog.tsx:85`).

No event payloads are introduced or changed — `trackEvent("PhotobankBulkTagApplied", ...)` in
`BulkTagDialog.tsx` reads only `result.addedCount`/`selectedTagNames.length`, both unaffected.
