## Module
Documents, File Storage & Printing (CatalogDocuments)

## Finding
`frontend/src/api/hooks/useCatalogDocuments.ts:53-58` defines a hand-rolled `apiFetch` helper that reaches into the NSwag-generated client's private fields:

```ts
function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const apiClient = getAuthenticatedApiClient();
  const baseUrl = (apiClient as any).baseUrl as string;
  return (apiClient as any).http.fetch(`${baseUrl}${path}`, init) as Promise<Response>;
}
```

All five hooks in the file (`useMaterialDocuments`, `usePifDocuments`, `useMaterialDocumentTypes`, `useUploadMaterialDocument`, `useUploadPifDocument`) route through it, and the file also hand-declares response/DTO interfaces (`ListCatalogDocumentsResponse`, `UploadDocumentResponse`, `MaterialDocumentTypeDto`, etc. — lines 4-37) that duplicate shapes NSwag already generates.

## Rule violated
`docs/development/api-client-generation.md:147-150`:

> **❌ AVOID**: `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch`
> These reach into private fields of the NSwag-generated class. If NSwag renames those fields, the code breaks at runtime with no compile-time warning.

The generated client already has fully-typed methods for every endpoint this file calls — `catalogDocuments_ListMaterialDocuments`, `catalogDocuments_UploadMaterialDocument`, `catalogDocuments_ListPifDocuments`, `catalogDocuments_UploadPifDocument`, `catalogDocuments_GetMaterialDocumentTypes` (`frontend/src/api/generated/api-client.ts:2376-2560`) — so this isn't covering a genuine client-generation gap.

## Why it matters
An NSwag regeneration that renames the private `http`/`baseUrl` fields silently breaks all catalog-document upload/listing at runtime, with none of the compile-time protection every typed hook in the app otherwise gets. The hand-rolled interfaces also drift risk against the generated DTOs (e.g. a backend field rename updates the generated type but not these manual duplicates, producing a silent shape mismatch instead of a compile error).

This is the same defect class already fixed in roughly fifteen other modules this cycle (e.g. #3818/#3815 Photobank, #3810/#3802/#3797 Manufacture, #3823 Expedition, #3816 DataQuality, #3833 KnowledgeBase, #3772 Purchase, #3750 Configuration) via the identical fix: replace the `as any` cast with the generated client's typed methods. CatalogDocuments has not yet had that pass applied.

## Suggested direction
Replace `apiFetch` calls with the generated client's `catalogDocuments_*` methods and drop the hand-declared response interfaces in favor of the generated types, following the pattern used in the already-fixed sibling modules.
