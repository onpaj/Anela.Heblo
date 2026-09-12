# Architecture Review: Leaflet Generate Mutation Hook

## Skip Design: true

## Architectural Fit Assessment

This is a pure frontend data-fetching refactor with no backend, contract, or visual change. It brings `LeafletGenerateTab.tsx` in line with the one-hook-per-operation convention already established by every other Leaflet operation in `frontend/src/api/hooks/useLeaflet.ts` (`useLeafletDocumentsQuery`, `useLeafletContentTypesQuery`, `useLeafletChunkDetailQuery`, `useDeleteLeafletDocumentMutation`, `useUploadLeafletDocumentMutation`, `useSubmitLeafletFeedbackMutation`, `useLeafletFeedbackListQuery`). No module boundaries, DTO ownership rules, or persistence concerns from `docs/architecture/development_guidelines.md` are implicated — the change is entirely inside the React app, one file (`useLeaflet.ts`) plus its consumer (`LeafletGenerateTab.tsx`) plus their tests.

I verified the spec's two load-bearing technical claims directly against the code and they are both accurate:

1. **`leaflet_Generate` already handles the absolute-URL requirement internally.** `frontend/src/api/generated/api-client.ts:5666` builds `url_ = this.baseUrl + "/api/leaflet/generate"` inside the generated method itself. CLAUDE.md's "API hooks use absolute URLs" rule exists to prevent hooks that build their own relative-URL `fetch` calls (the pattern every *other* hook in this file uses via `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch`). Calling `apiClient.leaflet_Generate(...)` directly sidesteps that concern entirely — there's no URL to construct, correctly or otherwise. The rule is satisfied by construction, not by an explicit `baseUrl` concatenation in the new hook.
2. **The generated method's typed-throw behavior is real and is exactly what the component depends on.** `processLeaflet_Generate` (api-client.ts:5685–5715): on HTTP 200 returns `GenerateLeafletResponse.fromJS(...)`; on 422 calls `throwException(..., result422)` where `result422` is a `GenerateLeafletResponse` instance; on 400 calls `throwException(..., result400)` where `result400` is a `ProblemDetails` instance; otherwise calls `throwException(...)` with no `result`, which falls through to `throw new SwaggerException(...)`. `throwException` (api-client.ts:45625) throws `result` directly when non-null. `LeafletGenerateTab.tsx:37`'s `err instanceof GenerateLeafletResponse && err.errorCode === ErrorCodes.LeafletEmptyRetrieval` check relies on exactly this. Re-implementing generate as a raw `http.fetch` (as the other hooks do) would require hand-rolling this branch-and-deserialize logic for no benefit.

Notably, this means **the new hook is the one that follows `docs/development/api-client-generation.md`'s documented default pattern** ("CORRECT — for standard hooks (the default pattern)": call the typed generated method directly), while the *existing* six hooks in this file are all built on the pattern that same doc explicitly labels "AVOID" (`(apiClient as any).baseUrl` / `(apiClient as any).http.fetch` reaching into private generated-client fields). That inconsistency is pre-existing and out of scope here (the spec correctly excludes refactoring the other hooks), but it's worth naming: this feature doesn't introduce a second pattern into the file so much as add the first hook that actually matches the documented standard, alongside five/six hooks that predate it and diverge from it. No action needed — just don't let a future reviewer mistake the raw-fetch hooks for the canonical style when extending this file further.

Everything else — mutation lifecycle via `useMutation`, no query invalidation because generation isn't cached/listed anywhere, `mutateAsync` to preserve `try/catch` control flow, a plain-interface params type mirroring `LeafletFeedbackListParams` — is a direct, low-risk application of patterns already present in this exact file.

## Proposed Architecture

### Component Overview

```
LeafletGenerateTab.tsx (component)
  └─ calls useGenerateLeafletMutation()          [NEW — frontend/src/api/hooks/useLeaflet.ts]
        └─ mutationFn: getAuthenticatedApiClient().leaflet_Generate(new GenerateLeafletRequest(params))
              └─ generated client (api-client.ts) → POST /api/leaflet/generate
                    ├─ 200 → resolves GenerateLeafletResponse
                    ├─ 422 → throws GenerateLeafletResponse (typed)
                    ├─ 400 → throws ProblemDetails
                    └─ other → throws SwaggerException
  └─ generate() keeps its own try/catch around mutateAsync(...) to classify the error
       and keeps result/generationId/errorBanner as local component state (unchanged)
```

No new components, no new files, no new backend surface. The only new unit is one hook function plus one exported interface, both inside the existing `useLeaflet.ts`.

### Key Design Decisions

#### Decision 1: `mutationFn` calls the generated client method directly, not a raw `http.fetch`
**Options considered:**
- (a) Mirror the other five hooks in the file: build the URL from `(apiClient as any).baseUrl`, call `(apiClient as any).http.fetch`, manually parse/branch the JSON response.
- (b) Call `getAuthenticatedApiClient().leaflet_Generate(request)` directly and let the generated client do the HTTP work, status branching, and typed deserialization.

**Chosen approach:** (b).

**Rationale:** Verified above — `leaflet_Generate` already does correct, non-trivial typed-error branching (422 → `GenerateLeafletResponse`, 400 → `ProblemDetails`, else → `SwaggerException`) that the component's error classification depends on byte-for-byte. Reimplementing that as a raw fetch would (1) duplicate logic that already exists and is tested by the generated client's own conventions, (2) risk silently diverging from it on the next NSwag regeneration, and (3) contradict the doc's own explicit guidance to prefer the typed client call and avoid reaching into `(apiClient as any)` private fields. This is also the only Leaflet operation where the generated method does something the hook actually needs (typed multi-branch errors); the other five don't need this, which is presumably why they predate this convention.

#### Decision 2: No query invalidation, no new query key usage
**Options considered:**
- (a) Wire up the pre-existing but unused `leafletKeys.generation(id)` key and invalidate/set it on success.
- (b) Add no cache interaction at all.

**Chosen approach:** (b).

**Rationale:** A generation result isn't fetched by ID anywhere else in the app (confirmed: no consumer of `leafletKeys.generation` exists), and generating a leaflet doesn't change the documents, content-types, or feedback lists. Adding cache wiring here would be speculative infrastructure with no consumer — exactly the kind of scope creep the codebase's "surgical changes" convention warns against. `useSubmitLeafletFeedbackMutation` already establishes the precedent of a mutation with no `onSuccess`/invalidation in this same file.

#### Decision 3: `mutateAsync` + local `try/catch`, not `mutate` + `onSuccess`/`onError`
**Options considered:**
- (a) `mutate(params, { onSuccess, onError })`.
- (b) `mutateAsync(params)` inside the existing `async generate()` / `try/catch`.

**Chosen approach:** (b).

**Rationale:** Preserves the component's current control flow (reset state → await → set result or classify error) verbatim, which is the spec's explicit goal ("preserving its exact current behavior"). Converting to callback style would be a larger diff for no behavioral gain and risks subtly changing execution order (e.g. state resets relative to the throw).

#### Decision 4: `GenerateLeafletParams` as a plain interface, not the `GenerateLeafletRequest` class
**Options considered:**
- (a) Type the hook's mutate-time argument as `GenerateLeafletRequest` directly (skip the wrapper interface).
- (b) Define a plain `GenerateLeafletParams` interface with the same three fields, and construct `new GenerateLeafletRequest(params)` inside `mutationFn`.

**Chosen approach:** (b).

**Rationale:** Matches this file's existing convention (`LeafletFeedbackListParams`, the `useSubmitLeafletFeedbackMutation` params object) of plain-interface, call-site-shaped input types rather than exposing generated-client classes as the public hook API. It also decouples the call site (`LeafletGenerateTab.tsx`) from needing to import and construct `GenerateLeafletRequest` itself — that construction now lives entirely inside the hook, which is where the other hooks in this file do their request-building too.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edit exactly these four existing files:
- `frontend/src/api/hooks/useLeaflet.ts` — add `GenerateLeafletParams` interface and `useGenerateLeafletMutation` hook, placed after `useUploadLeafletDocumentMutation` (ends line 283) and before `useSubmitLeafletFeedbackMutation` (starts line 289).
- `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx` — remove the inline client call and `isLoading` state; consume the new hook.
- `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx` — wrap render in `QueryClientProvider`; extend the `useLeaflet` module mock to preserve (or explicitly re-export) `useGenerateLeafletMutation`.
- `frontend/src/api/hooks/__tests__/useLeaflet.test.ts` — add a new `describe('useGenerateLeafletMutation', ...)` block.

### Interfaces and Contracts

```typescript
// frontend/src/api/hooks/useLeaflet.ts — new exports
export interface GenerateLeafletParams {
  topic: string;
  audience: AudienceType;
  length: LeafletLength;
}

export const useGenerateLeafletMutation: () => UseMutationResult<
  GenerateLeafletResponse,
  unknown,
  GenerateLeafletParams
>;
```

Import `AudienceType`, `LeafletLength`, `GenerateLeafletRequest`, `GenerateLeafletResponse` into `useLeaflet.ts` from `../generated/api-client` (a new import in this file — it currently imports no generated types, only `getAuthenticatedApiClient`/`QUERY_KEYS` from `../client`). `LeafletGenerateTab.tsx` keeps its own import of `AudienceType`, `LeafletLength`, `ErrorCodes`, `GenerateLeafletResponse` (still needed for local state typing and the `instanceof` check) but drops `GenerateLeafletRequest` (construction moves into the hook) and `getAuthenticatedApiClient` (no longer called from the component).

No backend contract changes; `POST /api/leaflet/generate` and its DTOs are untouched.

### Data Flow

1. User fills the form; `LeafletGenerateTab` holds `topic`/`audience`/`length` in local state (unchanged).
2. `generate()` resets `generationId`/`errorBanner`, then calls `generateLeaflet.mutateAsync({ topic, audience, length })`.
3. Inside the hook, `mutationFn` builds `new GenerateLeafletRequest(params)` and calls `getAuthenticatedApiClient().leaflet_Generate(request)`.
4. Success: hook resolves with the typed `GenerateLeafletResponse`; component sets `result`/`generationId` from it, exactly as today.
5. Failure: the generated client throws `GenerateLeafletResponse` (422), `ProblemDetails` (400), or `SwaggerException` (other); `mutateAsync` rejects with the same thrown value (React Query doesn't wrap it); the component's `catch` block classifies it exactly as it does today (`instanceof GenerateLeafletResponse && errorCode === LeafletEmptyRetrieval` → amber banner, else → red banner).
6. `generateLeaflet.isPending` replaces the local `isLoading` boolean everywhere it's read (form's `isLoading` prop, the skeleton-vs-result conditional).
7. Regenerate button calls the same `generate()` function — unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `LeafletGenerateTab.test.tsx` renders without a `QueryClientProvider`; `useMutation` throws "No QueryClient set" once the component calls the real hook | High (breaks existing tests) | FR-3 already requires wrapping the render with a `QueryClientProvider` (`retry: false` for queries and mutations), matching `GiftsTab.test.tsx`'s `createWrapper` pattern — confirmed present in this repo. Do this in the same commit as the component change, not as a follow-up. |
| `jest.mock('../../../api/hooks/useLeaflet', ...)` in `LeafletGenerateTab.test.tsx` currently replaces the whole module with only `useSubmitLeafletFeedbackMutation` stubbed; `useGenerateLeafletMutation` would resolve to `undefined` and `.mutateAsync` would throw "not a function" | High (breaks existing tests) | Use `jest.requireActual('../../../api/hooks/useLeaflet')` inside the mock factory and spread it, overriding only `useSubmitLeafletFeedbackMutation`, so the real `useGenerateLeafletMutation` runs against the still-mocked `getAuthenticatedApiClient` (`leaflet_Generate: mockGenerate`). This keeps both existing test bodies unchanged, as the spec requires. |
| The file mixes two mutation styles now: five hooks doing manual `http.fetch` + hand-typed interfaces, and this one calling a typed generated method directly | Low | Accept it — this is the direction the doc's own "default pattern" points, and retrofitting the other five hooks is explicitly out of scope. Leave a one-line comment above the new hook (or none — the doc already explains the split) so a future reader doesn't "fix" this hook to match the other five. |
| `GenerateLeafletParams` duplicates `GenerateLeafletRequest`'s shape (three identical fields) | Low | Accepted intentionally per Decision 4 — matches the file's established plain-interface convention over exposing generated classes as hook input types. No action needed. |
| A future reviewer adds `onSuccess`/`onError` cache invalidation to this hook by habit (most mutations in the codebase invalidate something) | Low | Spec's "Out of Scope" section already forecloses this for this PR; the Background section explains why (`leafletKeys.generation(id)` has no reader). Nothing to build now — just don't add it speculatively. |

## Specification Amendments

None required — the spec is technically accurate on every point I could verify against the actual code (generated client behavior, existing hook conventions, existing test setup). One clarification worth folding in for the implementer, not a change to scope:

- FR-2's acceptance criteria says "all four current usages" of `isLoading`. In the current `LeafletGenerateTab.tsx` there are exactly two *read* sites (the `isLoading` prop passed to `LeafletForm` at line 73, and the skeleton/result ternary at line 81) plus the `useState` declaration and the two `setIsLoading` calls (`true` at entry, `false` in `finally`). "Four" is therefore an approximate count across declaration+reads+writes, not four distinct call sites to hunt for. Practically: delete the `useState` line, delete both `setIsLoading` calls and the now-empty-of-purpose `finally` block, and replace both remaining reads with `generateLeaflet.isPending`. (Also note: `LeafletForm.tsx` itself has three more internal reads of its own `isLoading` *prop* — those are unaffected since the prop name/type doesn't change, only the value passed into it.)

## Prerequisites

None. No migrations, no config, no infrastructure, no backend changes, and no OpenAPI regeneration — `leaflet_Generate`, `GenerateLeafletRequest`, `GenerateLeafletResponse`, `AudienceType`, `LeafletLength`, and `ErrorCodes` all already exist in `frontend/src/api/generated/api-client.ts` exactly as the spec describes. Implementation can start immediately.
