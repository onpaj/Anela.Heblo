# Design: Leaflet Generate Mutation Hook

## Component Design

No new UI, no visual or interaction change — this is a data-fetching pattern refactor only. `LeafletForm.tsx` and `LeafletResult.tsx` keep their existing prop contracts unchanged.

### `useGenerateLeafletMutation` (new — `frontend/src/api/hooks/useLeaflet.ts`)
- **Responsibility:** Wrap the existing `leaflet_Generate` generated-client call in a React Query mutation, matching the one-hook-per-operation convention already used by every other Leaflet operation in this file (`useLeafletDocumentsQuery`, `useDeleteLeafletDocumentMutation`, `useUploadLeafletDocumentMutation`, `useSubmitLeafletFeedbackMutation`, etc.).
- **Placement:** After `useUploadLeafletDocumentMutation`, before `useSubmitLeafletFeedbackMutation`.
- **Interface:**
  ```typescript
  useGenerateLeafletMutation(): UseMutationResult<GenerateLeafletResponse, unknown, GenerateLeafletParams>
  ```
- **Behavior:**
  - Takes no hook-level arguments; all params passed at `mutate`/`mutateAsync` call time (consistent with sibling mutations in this file).
  - `mutationFn` calls `getAuthenticatedApiClient().leaflet_Generate(new GenerateLeafletRequest(params))` directly — it does **not** reimplement the call as a raw `http.fetch` the way the other hooks in this file do, because `leaflet_Generate`'s generated typed-throw behavior (422 → `GenerateLeafletResponse`, 400 → `ProblemDetails`, other → `SwaggerException`) is exactly what the consuming component's error classification depends on, and reimplementing it would duplicate logic for no benefit.
  - No `onSuccess`/`onError`, no query invalidation, no new `leafletKeys` entry — a generation result isn't cached/read by ID anywhere in the app, and generating doesn't change the documents/content-types/feedback lists. `useSubmitLeafletFeedbackMutation` already establishes this no-invalidation precedent in the same file.
  - Resolves with the **typed `GenerateLeafletResponse` instance** (not a plain object), and rejects with whatever `leaflet_Generate` throws, unmodified — required so the component's `instanceof GenerateLeafletResponse` check keeps working on both paths.

### `LeafletGenerateTab.tsx` (modified consumer)
- **Responsibility:** Unchanged — form state, submit trigger, result display, error-banner classification, regenerate action. Only the mechanism for invoking the API call and tracking its loading state changes.
- **Contract changes:**
  - Drops the `getAuthenticatedApiClient` import and direct `client.leaflet_Generate(...)` call; drops the `GenerateLeafletRequest` import (construction now lives inside the hook).
  - Drops local `isLoading` state (`useState`, both `setIsLoading` calls, the `finally` block); every read site (the `LeafletForm` `isLoading` prop, the skeleton-vs-result conditional) is replaced by `generateLeaflet.isPending` from the mutation object.
  - Keeps `result`, `generationId`, and `errorBanner` as local component state, **not** derived from `generateLeaflet.data` — this is a deliberate scope boundary, not an oversight, to keep the diff minimal and preserve current "loading fully replaces the result panel" behavior.
  - `generate()` keeps its exact current shape: reset `generationId`/`errorBanner` → `await generateLeaflet.mutateAsync({ topic, audience, length })` inside `try/catch` → on success set `result`/`generationId`; on catch, classify via `instanceof GenerateLeafletResponse && errorCode === ErrorCodes.LeafletEmptyRetrieval` into the amber "insufficient" banner or the red "transient" banner, with unchanged Czech copy and Tailwind classes.
  - `mutateAsync` (not `mutate`) is used specifically so this `try/catch/async` control flow can be preserved verbatim rather than converted to `onSuccess`/`onError` callbacks.
  - `onRegenerate={generate}` on `LeafletResult` is unchanged.

### Test doubles (modified)
- `LeafletGenerateTab.test.tsx`: render wrapped in a `QueryClientProvider` (`retry: false` for queries and mutations, matching `useLeaflet.test.ts`'s `createWrapper`); the `jest.mock('../../../api/hooks/useLeaflet', ...)` factory uses `jest.requireActual` to keep the real `useGenerateLeafletMutation` while continuing to override only `useSubmitLeafletFeedbackMutation`. `getAuthenticatedApiClient` mock (`{ leaflet_Generate: mockGenerate }`) is unchanged.
- `useLeaflet.test.ts`: new `describe('useGenerateLeafletMutation', ...)` block using the existing `renderHook` + `createWrapper` pattern, covering a successful resolve and a rejected (422-shaped `GenerateLeafletResponse`) call.

## Data Schemas

No new data entities and no backend/API contract change — `POST /api/leaflet/generate` and its DTOs are untouched. All types are reused from `frontend/src/api/generated/api-client.ts`:

```typescript
// Existing generated types (unchanged), now also imported into useLeaflet.ts
class GenerateLeafletRequest {
  topic: string;
  audience: AudienceType;
  length: LeafletLength;
}

class GenerateLeafletResponse extends BaseResponse {
  // BaseResponse fields
  success?: boolean;
  errorCode?: ErrorCodes;
  params?: { [key: string]: any };
  // own fields
  content?: string;
  id?: string;
  kbSourceCount?: number;
  leafletSourceCount?: number;
}
```

New TypeScript-only shape, added to `useLeaflet.ts` as the hook's mutate-time input (mirrors `GenerateLeafletRequest`'s fields as a plain interface, matching this file's existing convention for hook params such as `LeafletFeedbackListParams` — not a class, so it stays decoupled from the generated client's construction/serialization concerns):

```typescript
export interface GenerateLeafletParams {
  topic: string;
  audience: AudienceType;
  length: LeafletLength;
}
```

Call path (unchanged over the wire, only the call site moves):

| Before | After |
|---|---|
| `LeafletGenerateTab.tsx` → `getAuthenticatedApiClient().leaflet_Generate(new GenerateLeafletRequest({ topic, audience, length }))` | `LeafletGenerateTab.tsx` → `useGenerateLeafletMutation().mutateAsync({ topic, audience, length })` → hook internally calls `getAuthenticatedApiClient().leaflet_Generate(new GenerateLeafletRequest(params))` |

Response/error shapes on the wire are identical to today:
- `200` → `GenerateLeafletResponse` (resolved value)
- `422` → `GenerateLeafletResponse` with `errorCode` set (thrown/rejected value)
- `400` → `ProblemDetails` (thrown/rejected value)
- other non-200 → `SwaggerException` (thrown/rejected value)
