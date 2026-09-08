# Specification: Leaflet Generate Mutation Hook

## Summary
`LeafletGenerateTab.tsx` currently calls the leaflet-generation endpoint by instantiating the NSwag-generated API client directly in the component body and hand-rolling `isLoading`/error state, while every other Leaflet API call in the module goes through a dedicated React Query hook in `frontend/src/api/hooks/useLeaflet.ts`. This spec adds a `useGenerateLeafletMutation` hook to `useLeaflet.ts` and refactors `LeafletGenerateTab.tsx` to consume it, bringing the generate flow in line with the module's established hook pattern while preserving its exact current behavior (topic/audience/length submission, insufficient-knowledge vs. transient error banners, loading skeleton, regenerate action).

## Background
`frontend/src/api/hooks/useLeaflet.ts` already exposes one hook per Leaflet operation: `useLeafletDocumentsQuery`, `useLeafletContentTypesQuery`, `useLeafletChunkDetailQuery`, `useDeleteLeafletDocumentMutation`, `useUploadLeafletDocumentMutation`, `useSubmitLeafletFeedbackMutation`, and `useLeafletFeedbackListQuery`. `LeafletGenerateTab.tsx` (lines 1–51) is the sole holdout: it imports `getAuthenticatedApiClient` directly, builds a `GenerateLeafletRequest`, awaits `client.leaflet_Generate(...)` inline, and manages `isLoading`/`errorBanner` with local `useState`. This was flagged by the automated arch-review routine (brief at `artifacts/feat-4106/brief.md`) as an inconsistency that makes the module harder to maintain and prevents the call from benefiting from React Query's mutation lifecycle (loading/error state, testability in isolation).

Two facts from the existing code materially shape this spec:

1. **All existing hooks in `useLeaflet.ts` bypass the generated client's typed methods** and instead call `(apiClient as any).http.fetch(fullUrl, ...)` directly, manually building URLs and manually parsing JSON responses into hand-written interfaces (e.g. `GetLeafletDocumentsResponse`). This pattern exists because those endpoints don't need the generated client's typed error-branching.
2. **`leaflet_Generate` is different**: the generated method (`frontend/src/api/generated/api-client.ts:5665-5715`) already does meaningful, correct work that the component currently depends on — on HTTP 422 it throws the deserialized `GenerateLeafletResponse` instance itself (via `throwException`, which throws `result` directly when non-null), on HTTP 400 it throws a `ProblemDetails` instance, and on any other non-200 status it throws a `SwaggerException`. The current component's error handling — `err instanceof GenerateLeafletResponse && err.errorCode === ErrorCodes.LeafletEmptyRetrieval` — relies exactly on this typed-throw behavior. Re-implementing generate as a raw `http.fetch` call (mirroring the other hooks in the file) would require hand-rolling this deserialization and lose type safety for no benefit. Per `docs/development/api-client-generation.md`'s own canonical example (`client.weatherForecast()` called directly inside a hook's `queryFn`), calling the generated client method directly from inside a hook is the documented, supported pattern. **Decision: the new hook's `mutationFn` calls `getAuthenticatedApiClient().leaflet_Generate(request)` directly** (does not re-implement it as a raw fetch), preserving today's error-typing behavior exactly.

`leafletKeys.generation(id)` already exists in the query-key factory (`useLeaflet.ts:120-121`) but is currently unused by any query or mutation. This feature does not need it: a leaflet generation is not itself cached/fetched by ID anywhere in the app, and generating a leaflet does not change the documents list, content-types list, or feedback list, so **no query invalidation is required** on success.

## Functional Requirements

### FR-1: Add `useGenerateLeafletMutation` to `useLeaflet.ts`
Add a new exported hook, placed alongside the other mutation hooks in `frontend/src/api/hooks/useLeaflet.ts` (after `useUploadLeafletDocumentMutation`, before `useSubmitLeafletFeedbackMutation`, matching the file's existing top-to-bottom ordering of docs→mutations→feedback), with this shape:

```typescript
export interface GenerateLeafletParams {
  topic: string;
  audience: AudienceType;
  length: LeafletLength;
}

export const useGenerateLeafletMutation = () => {
  return useMutation({
    mutationFn: async (params: GenerateLeafletParams): Promise<GenerateLeafletResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.leaflet_Generate(new GenerateLeafletRequest(params));
    },
  });
};
```

- `GenerateLeafletParams` and the `AudienceType`, `LeafletLength`, `GenerateLeafletRequest`, `GenerateLeafletResponse` types are imported from `../generated/api-client` (the same module `LeafletGenerateTab.tsx` already imports them from).
- The hook takes **no arguments** (consistent with `useDeleteLeafletDocumentMutation`, `useUploadLeafletDocumentMutation`, `useSubmitLeafletFeedbackMutation`, none of which take hook-level arguments — all parameters are passed at `mutate`/`mutateAsync` call time).
- `mutationFn` resolves with the **typed `GenerateLeafletResponse` instance** returned by the generated client (not a plain parsed JSON object) — this is required so that `instanceof GenerateLeafletResponse` checks in the consuming component continue to work on both the success path and the thrown-error path (422).
- No `onSuccess`/`onError` query invalidation is added (see Background — generate has no cache-invalidation side effects).
- No new query key is added to `leafletKeys` (mutations in this file that don't invalidate anything specific don't need one; `useSubmitLeafletFeedbackMutation` sets this precedent).

**Acceptance criteria:**
- `useGenerateLeafletMutation` is exported from `frontend/src/api/hooks/useLeaflet.ts`.
- Calling `mutate`/`mutateAsync` with `{ topic, audience, length }` results in exactly one call to `getAuthenticatedApiClient().leaflet_Generate(...)` with a `GenerateLeafletRequest` constructed from those three fields (matching today's `new GenerateLeafletRequest({ topic, audience, length })` call site).
- On success, the mutation resolves with the `GenerateLeafletResponse` instance as returned by `leaflet_Generate` (with `.content`, `.id`, `.kbSourceCount`, `.leafletSourceCount`, `.success`, `.errorCode` intact).
- On an HTTP 422 response, the mutation's promise rejects with the `GenerateLeafletResponse` instance carrying `errorCode` (i.e. `mutateAsync(...).catch(err => err instanceof GenerateLeafletResponse)` is `true`), unchanged from current `client.leaflet_Generate` behavior.
- On any other non-200 response, the mutation's promise rejects with whatever `leaflet_Generate` currently throws (`ProblemDetails` for 400, `SwaggerException` otherwise) — no new error wrapping is introduced.
- No `invalidateQueries` call is added to this hook.

### FR-2: Refactor `LeafletGenerateTab.tsx` to use the new hook
Replace the inline API call and manual loading-state bookkeeping with the hook, while preserving every other piece of existing state and behavior (form fields, result content, generation id, error banner classification, regenerate action, loading skeleton).

Target shape of the relevant parts of the component:

```tsx
import React, { useState } from 'react';
import LeafletForm from './LeafletForm';
import LeafletResult from './LeafletResult';
import { useGenerateLeafletMutation } from '../../api/hooks/useLeaflet';
import {
  AudienceType,
  ErrorCodes,
  GenerateLeafletResponse,
  LeafletLength,
} from '../../api/generated/api-client';

interface ErrorBanner {
  kind: 'insufficient' | 'transient';
  message: string;
}

const LeafletGenerateTab: React.FC = () => {
  const [topic, setTopic] = useState('');
  const [audience, setAudience] = useState<AudienceType>(AudienceType.EndConsumer);
  const [length, setLength] = useState<LeafletLength>(LeafletLength.Medium);
  const [result, setResult] = useState('');
  const [generationId, setGenerationId] = useState<string | null>(null);
  const [errorBanner, setErrorBanner] = useState<ErrorBanner | null>(null);
  const generateLeaflet = useGenerateLeafletMutation();

  const generate = async () => {
    setGenerationId(null);
    setErrorBanner(null);
    try {
      const response = await generateLeaflet.mutateAsync({ topic, audience, length });
      setResult(response.content ?? '');
      setGenerationId(response.id ?? null);
    } catch (err: unknown) {
      if (err instanceof GenerateLeafletResponse && err.errorCode === ErrorCodes.LeafletEmptyRetrieval) {
        setErrorBanner({
          kind: 'insufficient',
          message: 'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
        });
      } else {
        setErrorBanner({
          kind: 'transient',
          message: 'Generování selhalo. Zkuste to prosím znovu.',
        });
      }
    }
  };

  // ...rest of JSX unchanged, replacing every `isLoading` reference with `generateLeaflet.isPending`
};
```

Specifically:
- Remove the `getAuthenticatedApiClient` import and the direct `client.leaflet_Generate(...)` call.
- Remove the local `isLoading` state (`useState(false)`) and its `setIsLoading(true)` / `setIsLoading(false)` calls (including the `finally` block, which is no longer needed).
- Every current usage of `isLoading` (the `LeafletForm` prop, the skeleton-vs-result conditional, `aria-busy`) is replaced with `generateLeaflet.isPending`.
- Keep `result`, `generationId`, and `errorBanner` as local component state, exactly as today — do not derive them from `generateLeaflet.data`. This keeps the diff minimal (the brief calls out only the inline API call and the manual loading/error state as the problem) and preserves the current "loading state fully replaces the result panel, so stale-data-during-refetch is a non-issue" behavior without depending on React Query's mutation `data`/`reset()` semantics.
- Preserve the exact reset order at the start of `generate()` (clear `generationId` and `errorBanner`, not `result`), and the exact error-classification branching (`instanceof GenerateLeafletResponse && errorCode === ErrorCodes.LeafletEmptyRetrieval` → `'insufficient'` banner; anything else → `'transient'` banner), including the exact Czech copy for both banners and the `bg-amber-100`/`bg-red-100` styling.
- `onRegenerate={generate}` passed to `LeafletResult` is unchanged.
- Use `mutateAsync` (not `mutate`) so the existing `try/catch/async` control flow in `generate()` can be preserved verbatim rather than converted to `mutate(...)`'s `onSuccess`/`onError` callback style.

**Acceptance criteria:**
- `LeafletGenerateTab.tsx` no longer imports `getAuthenticatedApiClient` from `../../api/client`.
- `LeafletGenerateTab.tsx` no longer has a local `isLoading` state variable; all four current usages (`LeafletForm`'s `isLoading` prop, the skeleton/result conditional, and any other reference) use `generateLeaflet.isPending` instead, with identical rendered behavior.
- Submitting the form with a topic calls the mutation with `{ topic, audience, length }` sourced from current component state, matching today's request payload.
- A 422 `LeafletEmptyRetrieval` rejection renders the amber "Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci." banner (`role="alert"`, class containing `bg-amber-100`) — unchanged from current behavior.
- Any other rejection (network error, other error codes, 400, 500, etc.) renders the red "Generování selhalo. Zkuste to prosím znovu." banner (`role="alert"`, class containing `bg-red-100`) — unchanged from current behavior.
- A successful generation populates `LeafletResult` with the returned `content` and `id` exactly as before, and clears any previous error banner.
- Clicking "Generovat znovu" (regenerate) in `LeafletResult` re-invokes the same `generate()` function/mutation.
- No other visual or behavioral change to `LeafletGenerateTab.tsx`, `LeafletForm.tsx`, or `LeafletResult.tsx`.

### FR-3: Update existing tests to match the new call path
`frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx` currently mocks `../../../api/client`'s `getAuthenticatedApiClient` to return `{ leaflet_Generate: mockGenerate }` and renders the component **without** a `QueryClientProvider`. Since the component will now call the mutation through `useGenerateLeafletMutation`, which requires a `QueryClient` in context (per the existing repo pattern — e.g. `GiftsTab.test.tsx`'s `createWrapper()` using `QueryClientProvider`), this test file must be updated:
- Wrap `render(<LeafletGenerateTab />)` with a `QueryClientProvider` using a `QueryClient` configured with `retry: false` for both `queries` and `mutations` (matching `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`'s `createWrapper`).
- Keep mocking `getAuthenticatedApiClient` to return `{ leaflet_Generate: mockGenerate }` — this still works unchanged because the hook's `mutationFn` still calls `getAuthenticatedApiClient().leaflet_Generate(...)` under the hood, so both existing test cases (`LeafletEmptyRetrieval` banner, generic-error banner) should pass with no change to their mock setup or assertions beyond the added `QueryClientProvider` wrapper.
- The existing `jest.mock('../../../api/hooks/useLeaflet', ...)` mock (currently only stubbing `useSubmitLeafletFeedbackMutation`, used by the child `LeafletResult` component) must be extended to also export the real (or a suitably mocked) `useGenerateLeafletMutation` — since `jest.mock` on that module replaces the whole module, the mock factory needs an explicit `useGenerateLeafletMutation` export, or the mock should use `jest.requireActual` to keep the real implementation and only override `useSubmitLeafletFeedbackMutation`.

Add a new test suite for the hook itself in `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`, following the file's existing `renderHook` + `createWrapper` pattern used for `useSubmitLeafletFeedbackMutation`, covering:
- A successful call resolves with the `GenerateLeafletResponse` the mocked `leaflet_Generate` returns.
- A rejection from `leaflet_Generate` (e.g. a `GenerateLeafletResponse` thrown with `errorCode: ErrorCodes.LeafletEmptyRetrieval`, simulating a 422) propagates unchanged out of `mutateAsync`.

**Acceptance criteria:**
- `LeafletGenerateTab.test.tsx`'s two existing test cases (`'shows the insufficient knowledge banner...'`, `'shows the transient failure banner...'`) pass unmodified in assertions, with only the render/wrapper and mock-module setup updated.
- A new test file/suite section for `useGenerateLeafletMutation` exists in `useLeaflet.test.ts` and passes.
- `npm run build` and `npm run lint` succeed with no new warnings/errors introduced by this change.
- All Leaflet-related Jest tests (`useLeaflet.test.ts`, `LeafletGenerateTab.test.tsx`, `LeafletGeneratorPage.test.tsx`) pass.

## Non-Functional Requirements

### NFR-1: Performance
No performance change is expected or required — this is a structural refactor of how an existing HTTP call is invoked (via `useMutation` instead of an inline `await`), not a change to the request/response payload, endpoint, or network behavior. The generate request remains a single `POST /api/leaflet/generate` call per user action.

### NFR-2: Security
No change to authentication or authorization: `useGenerateLeafletMutation` calls `getAuthenticatedApiClient()`, the same authenticated client factory used by every other hook in `useLeaflet.ts` and by the component today. No new data is persisted, cached beyond the React Query mutation's in-memory state, or exposed.

## Data Model
No new data entities. This feature reuses existing generated types unchanged:
- `GenerateLeafletRequest` (`topic: string`, `audience: AudienceType`, `length: LeafletLength`) — request payload, constructed inside the new hook instead of inside the component.
- `GenerateLeafletResponse` (`extends BaseResponse`: `success?: boolean`, `errorCode?: ErrorCodes`, `params?: {...}`; own fields `content?: string`, `id?: string`, `kbSourceCount?: number`, `leafletSourceCount?: number`) — response/rejection payload, unchanged.
- New TypeScript-only interface `GenerateLeafletParams` (`{ topic: string; audience: AudienceType; length: LeafletLength }`) is added to `useLeaflet.ts` as the hook's mutate-time input type, mirroring `GenerateLeafletRequest`'s shape (kept as a plain interface, not a class, matching this file's existing convention of plain-interface hook params like `LeafletFeedbackListParams`).

## API / Interface Design
No backend/API changes — `POST /api/leaflet/generate` is unchanged. Only the frontend call site moves:

| Before | After |
|---|---|
| `LeafletGenerateTab.tsx` calls `getAuthenticatedApiClient().leaflet_Generate(new GenerateLeafletRequest({ topic, audience, length }))` inline | `LeafletGenerateTab.tsx` calls `useGenerateLeafletMutation().mutateAsync({ topic, audience, length })`; the hook internally calls `getAuthenticatedApiClient().leaflet_Generate(new GenerateLeafletRequest(params))` |
| Loading state: local `useState<boolean>` toggled manually around the call | Loading state: `useMutation`'s `isPending` |
| Error state: local `try/catch` around the raw client call | Error state: same `try/catch`, now around `mutateAsync`, with the mutation itself doing the HTTP call |

Hook signature:
```typescript
useGenerateLeafletMutation(): UseMutationResult<GenerateLeafletResponse, unknown, GenerateLeafletParams>
```

## Dependencies
- `@tanstack/react-query`'s `useMutation` (already a dependency, already used by every other mutation hook in `useLeaflet.ts`).
- The generated `leaflet_Generate` method, `GenerateLeafletRequest`, `GenerateLeafletResponse`, `AudienceType`, `LeafletLength`, and `ErrorCodes` from `frontend/src/api/generated/api-client.ts` — no regeneration of this file is needed since the backend contract is unchanged.
- `getAuthenticatedApiClient` from `frontend/src/api/client.ts`.

## Out of Scope
- Any change to the backend `/api/leaflet/generate` endpoint or its contract.
- Any change to `LeafletForm.tsx` or `LeafletResult.tsx` beyond what's already in place (their prop contracts are unchanged).
- Adding cache invalidation, optimistic updates, or retry configuration to the new mutation — the brief's "why it matters" section lists these as *potential* future benefits of using `useMutation`, not requirements of this change. None of the existing sibling mutations in this file configure retry or optimistic updates either, so none is added here.
- Refactoring the other hooks in `useLeaflet.ts` to also call generated client methods directly instead of raw `http.fetch` (the manual-fetch pattern used elsewhere in the file is left untouched — this spec only adds the one new hook).
- Using or wiring up the pre-existing, currently-unused `leafletKeys.generation(id)` query key.
- Converting `result`/`generationId` component state to be derived from the mutation's `data` field (see FR-2 rationale).

## Open Questions
None.

## Status: COMPLETE
