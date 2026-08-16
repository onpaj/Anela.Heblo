# Specification: Replace hand-rolled `apiFetch` in `useCatalogDocuments.ts` with the generated NSwag client

## Summary
`frontend/src/api/hooks/useCatalogDocuments.ts` bypasses the typed, generated `ApiClient` by reaching into its private `baseUrl`/`http` fields via `as any` casts, and hand-declares DTO interfaces that duplicate types NSwag already generates from the backend contract. This is a scoped tech-debt fix: replace the five hooks' internals to call the generated `catalogDocuments_*` methods and consume the generated types directly, with no change to hook signatures, query keys, caching behavior, or UI-facing contracts. This is the same mechanical fix already applied to ~15 sibling modules this cycle (Photobank, Manufacture, Expedition, DataQuality, KnowledgeBase, Purchase, Configuration, etc.).

## Background
`docs/development/api-client-generation.md` mandates using the generated `ApiClient`'s typed methods (`getAuthenticatedApiClient().{operation}`) instead of reaching into the client's private `baseUrl`/`http` fields, precisely because NSwag regeneration can rename those private fields with no compile-time warning, silently breaking runtime behavior. `useCatalogDocuments.ts` predates that pass and still uses the disallowed pattern:

```ts
function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const apiClient = getAuthenticatedApiClient();
  const baseUrl = (apiClient as any).baseUrl as string;
  return (apiClient as any).http.fetch(`${baseUrl}${path}`, init) as Promise<Response>;
}
```

All five exported hooks (`useMaterialDocuments`, `usePifDocuments`, `useMaterialDocumentTypes`, `useUploadMaterialDocument`, `useUploadPifDocument`) route through this helper, and the file hand-declares `FolderStatus`, `CatalogDocumentDto`, `MaterialDocumentTypeDto`, `ListCatalogDocumentsResponse`, `GetMaterialDocumentTypesResponse`, and `UploadDocumentResponse` — all of which already exist as generated classes/enums in `frontend/src/api/generated/api-client.ts` (confirmed present: `FolderStatus`, `CatalogDocumentDto`, `MaterialDocumentTypeDto`, `ListCatalogDocumentsResponse`, `GetMaterialDocumentTypesResponse`, `UploadDocumentResponse`, all with matching field names). The generated client already exposes the exact five operations needed:

- `catalogDocuments_ListMaterialDocuments(productCode: string): Promise<ListCatalogDocumentsResponse>`
- `catalogDocuments_UploadMaterialDocument(productCode: string, file: FileParameter | null | undefined, documentTypeCode: string | null | undefined, lot: string | null | undefined, commonName: string | null | undefined, uploadAsIs: boolean | undefined): Promise<UploadDocumentResponse>`
- `catalogDocuments_ListPifDocuments(productCode: string): Promise<ListCatalogDocumentsResponse>`
- `catalogDocuments_UploadPifDocument(productCode: string, file: FileParameter | null | undefined): Promise<UploadDocumentResponse>`
- `catalogDocuments_GetMaterialDocumentTypes(): Promise<GetMaterialDocumentTypesResponse>`

These generated methods already build the absolute URL from `this.baseUrl` and call `this.http.fetch` internally (through the class's own methods, not a hand-rolled reach-around), throw a typed exception on non-2xx responses, and parse the response body into the generated response classes via `fromJS`. Adopting them removes the private-field reach-around, removes the duplicate DTO declarations, and gets compile-time breakage instead of silent runtime breakage if the backend contract changes.

Three consumer components read the response shape of these hooks and must continue to work unchanged: `frontend/src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx` and `PifDocumentsTab.tsx` read `data?.folderStatus` and `data?.files`; `frontend/src/components/catalog/detail/tabs/shared/MaterialUploadDialog.tsx` and `PifUploadDialog.tsx` read `typesData?.documentTypes`, `data.success`, and `uploadMutation.data?.success`.

## Functional Requirements

### FR-1: Replace `apiFetch`-based query hooks with generated client calls
`useMaterialDocuments`, `usePifDocuments`, and `useMaterialDocumentTypes` must call `getAuthenticatedApiClient().catalogDocuments_ListMaterialDocuments(productCode)`, `.catalogDocuments_ListPifDocuments(productCode)`, and `.catalogDocuments_GetMaterialDocumentTypes()` respectively, instead of building a raw `Response` via `apiFetch` and calling `.json()` on it.

**Acceptance criteria:**
- No hook in the file calls `apiFetch`, `(apiClient as any)`, or any private field of `ApiClient`.
- `useMaterialDocuments(productCode)` returns `Promise<ListCatalogDocumentsResponse>` (the generated class) via `queryFn`, preserving the existing `queryKey`, `staleTime: 30_000`, and `enabled: !!productCode` options.
- `usePifDocuments(productCode)` mirrors the above for the PIF endpoint, unchanged `queryKey`/options.
- `useMaterialDocumentTypes()` returns `Promise<GetMaterialDocumentTypesResponse>`, unchanged `queryKey` and `staleTime: 5 * 60 * 1000`.
- Manual `if (!response.ok) throw new Error(...)` checks are removed — the generated client already throws (a typed `ApiException`, via `throwException`) on non-2xx responses, so this is not a silent behavior loss but a like-for-like replacement of the failure path.

### FR-2: Replace `apiFetch`-based mutation hooks with generated client calls
`useUploadMaterialDocument` and `useUploadPifDocument` must call `getAuthenticatedApiClient().catalogDocuments_UploadMaterialDocument(...)` and `.catalogDocuments_UploadPifDocument(...)` respectively, passing a `FileParameter` (`{ data: file, fileName: file.name }`, imported from `../generated/api-client`) instead of hand-building a `FormData` object.

**Acceptance criteria:**
- `useUploadMaterialDocument()`'s `mutationFn` calls `catalogDocuments_UploadMaterialDocument(params.productCode, { data: params.file, fileName: params.file.name }, params.documentTypeCode, params.lot, params.commonName, params.uploadAsIs)` and returns `Promise<UploadDocumentResponse>`.
- `useUploadPifDocument()`'s `mutationFn` calls `catalogDocuments_UploadPifDocument(params.productCode, { data: params.file, fileName: params.file.name })` and returns `Promise<UploadDocumentResponse>`.
- `retry: 0` and the existing `onSuccess` query-invalidation logic (`catalogDocumentsKeys.materialDocuments(variables.productCode)` / `catalogDocumentsKeys.pifDocuments(variables.productCode)`) are preserved unchanged.
- `UploadMaterialDocumentParams` and `UploadPifDocumentParams` (the hook-level input parameter shapes) are unchanged — they are hook-internal request parameters, not response DTOs, and are out of scope for this fix.

### FR-3: Remove duplicated hand-declared response/DTO types
The hand-declared `FolderStatus`, `CatalogDocumentDto`, `MaterialDocumentTypeDto`, `ListCatalogDocumentsResponse`, `GetMaterialDocumentTypesResponse`, and `UploadDocumentResponse` in `useCatalogDocuments.ts` (lines 4–37) must be deleted and replaced by importing the equivalent generated types from `../generated/api-client`.

**Acceptance criteria:**
- `useCatalogDocuments.ts` contains no local `interface`/type declaration that duplicates a generated type's shape.
- Any hook or consumer that referenced the local types now references the generated ones (`import { ListCatalogDocumentsResponse, GetMaterialDocumentTypesResponse, UploadDocumentResponse, FolderStatus, CatalogDocumentDto, MaterialDocumentTypeDto, FileParameter } from '../generated/api-client';` — import only what is actually used).
- `apiFetch` and the `catalogDocumentsKeys` object's shape are otherwise unaffected; `catalogDocumentsKeys` itself is retained as-is (it is not part of the defect).

### FR-4: Preserve external hook contracts for existing consumers
No consumer of these hooks may require changes beyond what TypeScript's structural typing already makes compatible. The generated classes' field names (`success`, `folderStatus`, `expectedPrefix`, `basePath`, `files`, `documentTypes`, `uploadedFilename`, `errorCode`, `params`) match the previously hand-declared interfaces field-for-field, so consumer code reading `data?.folderStatus`, `data?.files`, `typesData?.documentTypes`, `data.success`, and `uploadMutation.data?.success` should compile and run unchanged.

**Acceptance criteria:**
- `frontend/src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx`, `PifDocumentsTab.tsx`, `frontend/src/components/catalog/detail/tabs/shared/MaterialUploadDialog.tsx`, and `PifUploadDialog.tsx` require no source changes.
- `npm run build` and `npm run lint` (frontend) both pass after the change with no new TypeScript errors in these consumer files.
- Existing tests for `MaterialUploadDialog.test.tsx` (and any other test touching these components/hooks) pass unchanged, other than mock updates required by the different call surface (see NFR-1).

## Non-Functional Requirements

### NFR-1: Test/mock surface update
No dedicated unit test file for `useCatalogDocuments.ts` exists today. Any existing test that mocks `getAuthenticatedApiClient()` for these flows (e.g. `frontend/src/components/catalog/detail/tabs/shared/__tests__/MaterialUploadDialog.test.tsx`) must be updated to mock the generated `catalogDocuments_*` methods on the client mock, in place of mocking `fetch`/`http.fetch`/`baseUrl`, since the code path under test no longer touches those private fields.

**Acceptance criteria:**
- `MaterialUploadDialog.test.tsx` (and any sibling test exercising these hooks) is updated to mock `catalogDocuments_UploadMaterialDocument`/`catalogDocuments_ListMaterialDocuments`/etc. directly, and passes.
- No test asserts on the removed `apiFetch` helper or private-field access.

### NFR-2: No behavioral or performance regression
This is a refactor of the client call mechanism only. Request URLs, HTTP methods, headers, and payload shapes sent to the backend must be identical to today's behavior (the generated methods build the same `/api/catalog-documents/...` paths and the same multipart `FormData` fields observed in the current hand-rolled code). Query caching (`staleTime`, `queryKey`, `enabled`) and mutation invalidation behavior must be unchanged. No new network calls, retries, or latency are introduced.

**Acceptance criteria:**
- Manual/E2E verification (or existing E2E coverage, if any exercises the Material/PIF document tabs) shows identical list/upload behavior before and after the change.

### NFR-3: Security
No change to authentication, authorization, or data sensitivity. The generated client's `catalogDocuments_*` methods are called through the same `getAuthenticatedApiClient()` accessor already used today, so the same bearer-token/auth-header attachment applies unchanged.

## Data Model
No new data model. This fix removes duplicate frontend-only type declarations (`FolderStatus`, `CatalogDocumentDto`, `MaterialDocumentTypeDto`, `ListCatalogDocumentsResponse`, `GetMaterialDocumentTypesResponse`, `UploadDocumentResponse`) in favor of the single source of truth already generated from the backend OpenAPI contract into `frontend/src/api/generated/api-client.ts`. No backend or database changes are required or in scope.

## API / Interface Design
No backend API changes. On the frontend, the internal implementation of five existing hooks changes from a hand-rolled `fetch`-via-private-fields call to a call against the corresponding generated `ApiClient` method:

| Hook | Generated method used |
|---|---|
| `useMaterialDocuments(productCode)` | `catalogDocuments_ListMaterialDocuments(productCode)` |
| `usePifDocuments(productCode)` | `catalogDocuments_ListPifDocuments(productCode)` |
| `useMaterialDocumentTypes()` | `catalogDocuments_GetMaterialDocumentTypes()` |
| `useUploadMaterialDocument()` | `catalogDocuments_UploadMaterialDocument(productCode, fileParameter, documentTypeCode, lot, commonName, uploadAsIs)` |
| `useUploadPifDocument()` | `catalogDocuments_UploadPifDocument(productCode, fileParameter)` |

Public hook signatures (parameters and return shapes as consumed by callers) are unchanged.

## Dependencies
- `frontend/src/api/generated/api-client.ts` (NSwag-generated client) — already contains the required `catalogDocuments_*` methods and DTO classes; no regeneration needed.
- `frontend/src/api/client.ts` — `getAuthenticatedApiClient()`, already the entry point used by both the old and new code.
- Existing pattern reference: `frontend/src/api/hooks/useKnowledgeBase.ts` (`useUploadKnowledgeBaseDocumentMutation`) for the `FileParameter` usage convention.
- `docs/development/api-client-generation.md` — the rule this fix brings the file into compliance with.

## Out of Scope
- Any change to backend controllers, routes, or response contracts.
- Any change to `UploadMaterialDocumentParams` / `UploadPifDocumentParams` (hook input parameter shapes) or `catalogDocumentsKeys`.
- Any change to consumer components' UI, styling, or business logic beyond what's needed to keep existing tests passing against the new mock surface.
- Regenerating or modifying `frontend/src/api/generated/api-client.ts` itself.
- Broader adoption of the typed-exception/`try-catch` 409-style pattern described in `docs/development/api-client-generation.md` for business-outcome status codes — the current endpoints only use success/failure via the `BaseResponse.success` envelope, not distinct HTTP status branching, so this is not applicable here.

## Open Questions
None.

## Status: COMPLETE
