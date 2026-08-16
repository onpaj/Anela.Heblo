# Architecture Review: Replace hand-rolled `apiFetch` in `useCatalogDocuments.ts` with the generated NSwag client

## Skip Design: true

## Architectural Fit Assessment
This is a pure internal refactor of one hook file, with no change to hook signatures, UI, routes, or backend contracts. It fits squarely into an established, repeated remediation pattern (already applied to ~15 sibling modules: Photobank, Manufacture, Expedition, DataQuality, KnowledgeBase, Purchase, Configuration, etc.) that brings hooks into compliance with `docs/development/api-client-generation.md`.

I verified the premise directly against the codebase rather than taking the spec's word for it:
- `frontend/src/api/generated/api-client.ts` already contains all five required typed methods (`catalogDocuments_ListMaterialDocuments` at line 2376, `catalogDocuments_UploadMaterialDocument` at 2413, `catalogDocuments_ListPifDocuments` at 2465, `catalogDocuments_UploadPifDocument` at 2502, `catalogDocuments_GetMaterialDocumentTypes` at 2544) and the corresponding generated types (`ListCatalogDocumentsResponse` at 19740, `FolderStatus` enum at 19793, `CatalogDocumentDto` at 19799, `GetMaterialDocumentTypesResponse` at 19847, `MaterialDocumentTypeDto` at 19888, `UploadDocumentResponse` at 19932, `FileParameter` interface at 44557). No client regeneration is needed; this is purely a call-site swap.
- `frontend/src/api/hooks/useKnowledgeBase.ts` (`useUploadKnowledgeBaseDocumentMutation`, `useKnowledgeBaseDocumentsQuery`, etc.) is a good, current, in-repo reference for exactly this pattern: `queryFn`/`mutationFn` call `getAuthenticatedApiClient().<operation>(...)` directly, and `FileParameter` is built inline (`{ data: file, fileName: file.name }`) for uploads. `useCatalogDocuments.ts` should converge on this same shape.
- The four consumer components (`MaterialDocumentsTab.tsx`, `PifDocumentsTab.tsx`, `MaterialUploadDialog.tsx`, `PifUploadDialog.tsx`) only read `data?.folderStatus`, `data?.files`, `data?.expectedPrefix`, `data?.basePath`, `typesData?.documentTypes`, and `.success` — all fields present on the generated classes with matching names, confirmed by reading `MaterialDocumentsTab.tsx`. No consumer changes are structurally required.
- I checked `MaterialUploadDialog.test.tsx`, the one existing test that exercises these hooks: it mocks the **hook module** itself (`jest.mock('.../api/hooks/useCatalogDocuments')`), not `getAuthenticatedApiClient()`, `fetch`, or any private field. This test requires **no changes** — see Specification Amendments below, this corrects an inaccuracy in NFR-1/FR-4's acceptance criteria.
- No other file in the repo references `catalogDocuments`/`CatalogDocuments`/`apiFetch` outside this hook file and its four consumers/one test, confirmed via a full-repo grep. The blast radius is exactly as the spec describes and no larger.

There is no new architectural surface here: no new module, no new endpoint, no new state shape. The correct engineering posture is "apply the established pattern faithfully," not "design something new."

## Proposed Architecture

### Component Overview
No component-level change. Data flow before and after:

```
MaterialDocumentsTab / PifDocumentsTab / MaterialUploadDialog / PifUploadDialog
                │  (unchanged: same hook names, same field reads)
                ▼
useCatalogDocuments.ts  (five exported hooks — internals only change)
                │
   BEFORE: apiFetch() → (apiClient as any).http.fetch(baseUrl + path) → raw Response → .json()
   AFTER:  getAuthenticatedApiClient().catalogDocuments_*(...)  → typed generated method
                │                                                  (builds URL, calls http.fetch,
                │                                                   throws ApiException on non-2xx,
                │                                                   parses via fromJS internally)
                ▼
frontend/src/api/generated/api-client.ts (NSwag-generated ApiClient)
                │
                ▼
Backend CatalogDocumentsController (/api/catalog-documents/...) — unchanged
```

### Key Design Decisions

#### Decision 1: Call the generated `catalogDocuments_*` methods directly; no intermediate mapping layer
**Options considered:**
1. Call generated methods directly from each hook's `queryFn`/`mutationFn`, returning the generated class instances as-is (this is the `useKnowledgeBaseDocumentsQuery` pattern).
2. Call generated methods but map the result into hand-maintained local interfaces (the `useKnowledgeBaseFeedbackListQuery` / `toLocalFeedbackListResponse` pattern), to shield consumers from the generated shape.

**Chosen approach:** Option 1 — call directly, return the generated types, delete the local interfaces entirely.

**Rationale:** Option 2 exists in `useKnowledgeBase.ts` only because the KB feedback-list consumer (`ragFeedbackTypes.ts`) needs `string`-typed dates and `null` instead of `undefined` for a component shared across RAG features — a genuine shape mismatch. No such mismatch exists here: the spec confirms (and I independently verified against `MaterialDocumentsTab.tsx`) that the generated field names and types line up with what consumers already read. Introducing a mapping layer here would be unjustified indirection that the spec's own FR-3/FR-4 already rule out ("no local interface/type declaration that duplicates a generated type's shape").

#### Decision 2: Let the generated client's thrown `ApiException` be the sole failure path; drop manual `!response.ok` checks
**Options considered:**
1. Keep a manual try/catch per hook to inspect status codes (the `docs/development/api-client-generation.md` "business outcome via HTTP status" pattern, used in `useSubmitFeedbackMutation`/`useSubmitArticleFeedbackMutation` for 409 handling).
2. Do nothing extra — let `ApiException` propagate to React Query's `error` field, matching every non-status-branching hook in the codebase (e.g. `useKnowledgeBaseDocumentsQuery`).

**Chosen approach:** Option 2.

**Rationale:** The spec's own "Out of Scope" section correctly identifies that these five endpoints only use the `BaseResponse.success` envelope for business outcomes, not distinct HTTP status branching — there is no 409/412-style case here. Consumers (`MaterialDocumentsTab.tsx`) already branch on `error` (from `useQuery`) separately from `data?.success`/`folderStatus`, and that pattern is undisturbed by switching the failure trigger from `!response.ok` to a thrown `ApiException`— both surface through React Query's `error`.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Single file modified in place: `frontend/src/api/hooks/useCatalogDocuments.ts`. No changes to `frontend/src/api/generated/api-client.ts`, `frontend/src/api/client.ts`, or any component under `frontend/src/components/catalog/detail/tabs/`.

### Interfaces and Contracts
- Import generated types from `frontend/src/api/generated/api-client.ts` — only what's actually used: `ListCatalogDocumentsResponse`, `GetMaterialDocumentTypesResponse`, `UploadDocumentResponse`, `FileParameter` as the return/parameter types referenced in hook signatures. `FolderStatus`, `CatalogDocumentDto`, `MaterialDocumentTypeDto` do not need explicit import unless a hook or the file references them by name for a local annotation — since consumers access fields structurally (`data?.folderStatus`), these three types will typically flow through inference without a separate import. Do not import types that aren't referenced.
- Keep `UploadMaterialDocumentParams` and `UploadPifDocumentParams` as hand-written hook-input interfaces — these are the hook's own parameter shape, not a duplicate of a generated DTO, and are explicitly out of scope (spec FR-2, FR-3).
- Keep `catalogDocumentsKeys` exactly as-is — it is a query-key factory, not part of the defect.
- Delete `apiFetch` entirely, along with the six hand-declared interfaces/type alias (`FolderStatus`, `CatalogDocumentDto`, `MaterialDocumentTypeDto`, `ListCatalogDocumentsResponse`, `GetMaterialDocumentTypesResponse`, `UploadDocumentResponse`) at lines 4–37 and 53–58 of the current file.
- Mutation `mutationFn`s build a `FileParameter` inline exactly as `useUploadKnowledgeBaseDocumentMutation` does: `{ data: file, fileName: file.name }` — do not introduce a shared helper for this one-liner; matching the sibling module's inline style keeps the diff mechanical and reviewable.

### Data Flow
1. **List flows** (`useMaterialDocuments`, `usePifDocuments`, `useMaterialDocumentTypes`): `queryFn` becomes a direct `return getAuthenticatedApiClient().catalogDocuments_X(...)` (async or returning the promise directly — either is fine; `useKnowledgeBaseContentTypesQuery` returns the promise directly without `await`, which is the more idiomatic form here since there's no post-processing). `queryKey`, `staleTime`, and `enabled` are copied verbatim from the current file.
2. **Upload flows** (`useUploadMaterialDocument`, `useUploadPifDocument`): `mutationFn` builds a `FileParameter` from `params.file`, calls the generated method with the exact positional arguments listed in the spec's API table, and returns its promise. `retry: 0` and the existing `onSuccess` → `queryClient.invalidateQueries(catalogDocumentsKeys.X(variables.productCode))` are unchanged.
3. Failure path: a non-2xx response now surfaces as a thrown `ApiException` from inside the generated method (instead of a hand-thrown `Error` after a manual `!response.ok` check). This flows into React Query's `error`/`isError` exactly as before — no consumer change needed, since none of the four consumer components branch on the *type* of the thrown error, only on truthy `error`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Generated `ApiException` shape differs subtly from the hand-thrown `Error` (e.g. `.message` format), and some consumer or test asserts on exact error text | Low | Grep confirms no consumer or test in the four consumer files/tests asserts on error message content — they only check `error` truthiness. No action needed beyond running the existing test suite. |
| Argument order mismatch when calling `catalogDocuments_UploadMaterialDocument` (five positional params after `productCode` — easy to transpose `lot`/`commonName`) | Medium | Match the exact positional signature confirmed in `api-client.ts:2413` and the spec's FR-2 acceptance criteria one-for-one; add/keep a `MaterialUploadDialog.test.tsx`-style smoke test if one doesn't already assert call args (currently it doesn't — see Specification Amendments). |
| Multipart `FormData` field encoding produced by the generated `catalogDocuments_UploadMaterialDocument` differs from the hand-built `FormData` (e.g. omits an empty-string `lot`/`commonName` field that the backend currently receives) | Low | The generated method is produced from the same OpenAPI contract the backend controller exposes, so field names/multipart encoding are contract-derived, not hand-guessed. If backend behavior depends on receiving an explicit empty string vs. an omitted field, verify manually against `CatalogDocumentsController` before merging (see Prerequisites). |
| Regenerating a spec inaccuracy: NFR-1 implies `MaterialUploadDialog.test.tsx` needs mock-surface changes | None (informational) | Verified false — see Specification Amendments. No risk, just flag so the implementer doesn't spend time on unneeded mock changes. |

## Specification Amendments
- **NFR-1 is not applicable to `MaterialUploadDialog.test.tsx` as written.** I read the test file: it mocks the `useCatalogDocuments` hook module directly (`jest.mock('.../api/hooks/useCatalogDocuments')`) and stubs `useMaterialDocumentTypes`/`useUploadMaterialDocument` return values — it never mocks `getAuthenticatedApiClient()`, `fetch`, `.http.fetch`, or `baseUrl`. This test requires **no changes** for this refactor. Do not spend effort adjusting its mock surface; just confirm it still passes (it should, unchanged, since the hook's external contract is untouched). If a *new* unit test for `useCatalogDocuments.ts` itself is added (none currently exists, confirmed by absence of `useCatalogDocuments.test.ts`), that new test would mock `getAuthenticatedApiClient()` and stub the `catalogDocuments_*` methods — but adding such a test is not required by the spec and is left as an implementer's discretion, not a blocking requirement.
- **FR-3's import list is advisory, not mandatory-as-written.** The spec's suggested import line names all six generated types; per "import only what is actually used," expect the real import list to be a subset (likely just `ListCatalogDocumentsResponse`, `GetMaterialDocumentTypesResponse`, `UploadDocumentResponse`, `FileParameter`), since `FolderStatus`/`CatalogDocumentDto`/`MaterialDocumentTypeDto` are consumed structurally through the response objects' fields rather than named directly in the hook file. This is a clarification, not a scope change.

## Prerequisites
None. No migration, no config, no infrastructure change, no backend change, no client regeneration. The generated methods and types already exist in `frontend/src/api/generated/api-client.ts` on the current branch. Implementation can start immediately; the only pre-flight check worth doing is confirming (via existing `CatalogDocumentsController` source or a quick manual upload) that the generated multipart encoding for empty-string `lot`/`commonName` fields matches what the backend currently tolerates, per the Risks table.
