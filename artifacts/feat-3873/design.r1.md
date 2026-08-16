# Design: Replace hand-rolled `apiFetch` in `useCatalogDocuments.ts` with the generated NSwag client

## Component Design

Single file changed: `frontend/src/api/hooks/useCatalogDocuments.ts`. No new files, no new components, no change to consumer components (`MaterialDocumentsTab.tsx`, `PifDocumentsTab.tsx`, `MaterialUploadDialog.tsx`, `PifUploadDialog.tsx`) or to `frontend/src/api/generated/api-client.ts` / `frontend/src/api/client.ts`.

### Removed

- `apiFetch(path, init)` — the private-field reach-around (`(apiClient as any).baseUrl`, `(apiClient as any).http.fetch`). Deleted entirely.
- Hand-declared local types: `FolderStatus`, `CatalogDocumentDto`, `MaterialDocumentTypeDto`, `ListCatalogDocumentsResponse`, `GetMaterialDocumentTypesResponse`, `UploadDocumentResponse` (current lines 4–37).
- Manual `if (!response.ok) throw new Error(...)` checks and `response.json()` calls in all five hooks.

### Retained, unchanged

- `catalogDocumentsKeys` query-key factory (lines 60–67) — not part of the defect, kept verbatim.
- `UploadMaterialDocumentParams` / `UploadPifDocumentParams` — hook-input parameter shapes, not generated DTOs, out of scope.
- All five exported hook names, signatures, `queryKey`s, `staleTime`s, `enabled` conditions, `retry: 0`, and `onSuccess` invalidation logic.

### Changed — hook internals

Each hook's `queryFn`/`mutationFn` body is replaced with a direct call into the generated client, obtained via the existing `getAuthenticatedApiClient()` accessor (unchanged import from `../client`).

| Hook | Responsibility | New internal call |
|---|---|---|
| `useMaterialDocuments(productCode)` | List material documents for a product | `getAuthenticatedApiClient().catalogDocuments_ListMaterialDocuments(productCode)` |
| `usePifDocuments(productCode)` | List PIF documents for a product | `getAuthenticatedApiClient().catalogDocuments_ListPifDocuments(productCode)` |
| `useMaterialDocumentTypes()` | Fetch the material document type catalog | `getAuthenticatedApiClient().catalogDocuments_GetMaterialDocumentTypes()` |
| `useUploadMaterialDocument()` | Upload a material document (mutation) | `getAuthenticatedApiClient().catalogDocuments_UploadMaterialDocument(productCode, fileParameter, documentTypeCode, lot, commonName, uploadAsIs)` |
| `useUploadPifDocument()` | Upload a PIF document (mutation) | `getAuthenticatedApiClient().catalogDocuments_UploadPifDocument(productCode, fileParameter)` |

Query hooks (`useMaterialDocuments`, `usePifDocuments`, `useMaterialDocumentTypes`) return the generated method's promise directly from `queryFn` (no `await`/post-processing needed — matches the `useKnowledgeBaseContentTypesQuery` convention already in the codebase).

Mutation hooks (`useUploadMaterialDocument`, `useUploadPifDocument`) build a `FileParameter` inline from `params.file`:

```ts
{ data: params.file, fileName: params.file.name }
```

— matching the `useUploadKnowledgeBaseDocumentMutation` convention. No shared helper is introduced for this one-liner.

Failure path: the generated client throws a typed `ApiException` on non-2xx responses (via its internal `throwException`), replacing the manual `!response.ok` check. This propagates to React Query's `error`/`isError` exactly as the hand-thrown `Error` did before; no consumer branches on error type, only on truthiness, so this is a transparent swap.

### Imports

```ts
import {
  ListCatalogDocumentsResponse,
  GetMaterialDocumentTypesResponse,
  UploadDocumentResponse,
  FileParameter,
} from '../generated/api-client';
```

`FolderStatus`, `CatalogDocumentDto`, `MaterialDocumentTypeDto` are not imported by name — consumers access their fields structurally through the response objects (`data?.folderStatus`, `data?.files`), so these types flow through inference without an explicit import, per the arch review's guidance to import only what's referenced by name.

## Data Schemas

No backend or database schema changes. This is a frontend-only type-source swap: hand-declared TypeScript interfaces are deleted in favor of the equivalent classes/enum already generated from the backend OpenAPI contract into `frontend/src/api/generated/api-client.ts`. Field names and shapes are identical between old and new — this table documents the mapping for verification purposes only.

### `ListCatalogDocumentsResponse` (used by `useMaterialDocuments`, `usePifDocuments`)

| Field | Type | Notes |
|---|---|---|
| `success` | `boolean` | |
| `folderStatus` | `FolderStatus` (generated enum: `Found` \| `NotFound` \| `MultipleMatches`) | Was a hand-declared string union; now the generated enum, structurally compatible. |
| `expectedPrefix` | `string` | |
| `basePath` | `string` | |
| `files` | `CatalogDocumentDto[]` | |

### `CatalogDocumentDto` (element type of `files`)

| Field | Type |
|---|---|
| `name` | `string` |
| `webUrl` | `string` |
| `sizeBytes` | `number` |
| `modifiedAt` | `string` |

### `GetMaterialDocumentTypesResponse` (used by `useMaterialDocumentTypes`)

| Field | Type |
|---|---|
| `success` | `boolean` |
| `documentTypes` | `MaterialDocumentTypeDto[]` |

### `MaterialDocumentTypeDto` (element type of `documentTypes`)

| Field | Type |
|---|---|
| `code` | `string` |
| `label` | `string` |
| `lotRequired` | `boolean` |

### `UploadDocumentResponse` (used by `useUploadMaterialDocument`, `useUploadPifDocument`)

| Field | Type |
|---|---|
| `success` | `boolean` |
| `uploadedFilename` | `string` |
| `errorCode` | `number \| undefined` |
| `params` | `Record<string, string> \| undefined` |

### Request shapes (unchanged, hook-internal — not generated DTOs)

```ts
interface UploadMaterialDocumentParams {
  productCode: string;
  file: File;
  documentTypeCode: string;
  lot: string;
  commonName: string;
  uploadAsIs: boolean;
}

interface UploadPifDocumentParams {
  productCode: string;
  file: File;
}
```

### `FileParameter` (generated, replaces hand-built `FormData`)

```ts
interface FileParameter {
  data: File;
  fileName: string;
}
```

Built inline per upload call as `{ data: params.file, fileName: params.file.name }`. The generated `catalogDocuments_Upload*` methods construct the multipart `FormData` internally from this plus the other positional arguments (`documentTypeCode`, `lot`, `commonName`, `uploadAsIs` for material uploads), producing the same wire-level request the hand-built `FormData` produced.

### API call surface (unchanged endpoints/methods, new call path)

| Operation | Endpoint | Generated method |
|---|---|---|
| List material documents | `GET /api/catalog-documents/materials/{productCode}` | `catalogDocuments_ListMaterialDocuments(productCode)` |
| List PIF documents | `GET /api/catalog-documents/pif/{productCode}` | `catalogDocuments_ListPifDocuments(productCode)` |
| Get material document types | `GET /api/catalog-documents/material-document-types` | `catalogDocuments_GetMaterialDocumentTypes()` |
| Upload material document | `POST /api/catalog-documents/materials/{productCode}` | `catalogDocuments_UploadMaterialDocument(productCode, file, documentTypeCode, lot, commonName, uploadAsIs)` |
| Upload PIF document | `POST /api/catalog-documents/pif/{productCode}` | `catalogDocuments_UploadPifDocument(productCode, file)` |

No new events, no new persisted state, no backend contract change.
