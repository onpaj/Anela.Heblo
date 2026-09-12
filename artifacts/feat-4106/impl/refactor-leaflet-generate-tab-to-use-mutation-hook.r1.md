# Implementation: refactor-leaflet-generate-tab-to-use-mutation-hook

## What was implemented
`LeafletGenerateTab` now calls `useGenerateLeafletMutation()` (from `useLeaflet.ts`,
committed in the prior task) via `mutateAsync` instead of calling
`getAuthenticatedApiClient().leaflet_Generate(...)` directly. The hand-rolled
`isLoading` `useState` and its `setIsLoading(true/false)` calls (including the
`finally` block) are removed; both read sites (`LeafletForm`'s `isLoading` prop
and the loading-skeleton conditional) now read `generateLeaflet.isPending`. The
`getAuthenticatedApiClient` and `GenerateLeafletRequest` imports are gone; the
`useGenerateLeafletMutation` import replaces them. The reset/error-classification
control flow (reset `generationId`/`errorBanner`, catch on
`GenerateLeafletResponse` + `ErrorCodes.LeafletEmptyRetrieval` vs. generic
"transient" banner) is unchanged.

Followed subagent-driven-development's RED→GREEN discipline directly in this
session (no subagent-dispatch tool was available — `ToolSearch` for
`Agent`/`Task`/`TaskCreate` returned no matches — so implementation and review
were done by me in one pass, per the developer-agent system prompt's fallback
instruction).

## Files created/modified
- `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx` — refactored to use the mutation hook; matches the task context's prescribed code verbatim (Step 1).
- `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx` — updated to wrap `render` in a `QueryClientProvider` and to `jest.requireActual` the real `useLeaflet` module (only stubbing `useSubmitLeafletFeedbackMutation`), plus one adaptation described in Notes below.

## Tests
- `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx` — 2 cases: insufficient-knowledge banner on `LeafletEmptyRetrieval`, transient-failure banner on a generic error. Both exercise the real hook's mutation flow end-to-end through a real `QueryClient`.
- `frontend/src/api/hooks/__tests__/useLeaflet.test.ts` — pre-existing, unmodified; re-run to confirm no regression from this task.

## How to verify
From `frontend/`:
1. `CI=true npx react-scripts test --watchAll=false src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx` → 2/2 pass.
2. `CI=true npx react-scripts test --watchAll=false src/api/hooks/__tests__/useLeaflet.test.ts` → 5/5 pass (unchanged).
3. `CI=false npm run build` → `Compiled successfully.`
4. `npx eslint src/features/leaflet-generator/LeafletGenerateTab.tsx src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx --ext .ts,.tsx` → no output, no errors.

## RED → GREEN evidence

**Step 1** (component refactor) applied first, exactly as prescribed.

**Step 2 — RED** (old test file against the new component):
```
FAIL src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx
  LeafletGenerateTab
    ✕ shows the insufficient knowledge banner when the API rejects with LeafletEmptyRetrieval (342 ms)
    ✕ shows the transient failure banner for a generic error (105 ms)

  ● LeafletGenerateTab › shows the insufficient knowledge banner when the API rejects with LeafletEmptyRetrieval
    TypeError: (0 , _useLeaflet.useGenerateLeafletMutation) is not a function
      at LeafletGenerateTab (src/features/leaflet-generator/LeafletGenerateTab.tsx:24:53)
...
Tests:       2 failed, 2 total
```
Matches the task context's predicted RED failure exactly (`useGenerateLeafletMutation` resolving to `undefined` because the old test's `jest.mock('../../../api/hooks/useLeaflet', ...)` replaced the whole module).

**Step 3** (test file rewrite) applied, with one adaptation (see Notes).

**Step 4 — GREEN**:
```
PASS src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx
  LeafletGenerateTab
    ✓ shows the insufficient knowledge banner when the API rejects with LeafletEmptyRetrieval (487 ms)
    ✓ shows the transient failure banner for a generic error (109 ms)

Tests:       2 passed, 2 total
```

## Notes

**Deviation from the prescribed test file (Step 3), adapted minimally:**
The task context's prescribed `jest.mock('../../../api/client', () => ({ getAuthenticatedApiClient: jest.fn() }))` does not include `QUERY_KEYS`. Once the test switches to `jest.requireActual('../../../api/hooks/useLeaflet')` (needed so the real `useGenerateLeafletMutation` runs), the real `useLeaflet.ts` module executes its top-level `leafletKeys` object, which reads `QUERY_KEYS.leaflet` from `../client` — and with `../../../api/client` fully mocked to only `{ getAuthenticatedApiClient }`, `QUERY_KEYS` is `undefined`, so `QUERY_KEYS.leaflet` throws `TypeError: Cannot read properties of undefined (reading 'leaflet')` at module-load time (test suite fails to run, not just the two `it` blocks).

Fix applied: added `QUERY_KEYS: { leaflet: ['leaflet'] }` to the `../../../api/client` mock. This exactly mirrors the pattern already used in `frontend/src/api/hooks/__tests__/useLeaflet.test.ts` (`jest.mock("../../client", () => ({ getAuthenticatedApiClient: jest.fn(), QUERY_KEYS: { leaflet: ["leaflet"] } }))`), committed in the prior task for the same reason — so this keeps the codebase's established mocking convention rather than introducing a new one (e.g. `jest.requireActual` on the whole `client` module, which would also pull in `runtimeConfig`/`mockAuth`/`e2eAuth`/`authRecovery` imports unnecessarily). No other part of the prescribed test file changed.

Everything else — the component's full contents, and the rest of the test file (imports, `createWrapper`, `fillAndSubmit`, both `it` bodies/assertions) — matches the task context verbatim.

No other concerns.

## PR Summary

Refactors `LeafletGenerateTab` to call the `useGenerateLeafletMutation` hook (added in the prior task) instead of invoking `getAuthenticatedApiClient().leaflet_Generate(...)` directly, replacing the component's hand-rolled `isLoading` state with the mutation's `isPending`. This closes the architecture-review gap where the leaflet-generate mutation bypassed the hooks layer used by the rest of the codebase.

### Changes
- `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx` — uses `useGenerateLeafletMutation().mutateAsync`; removed the local `isLoading` state and the now-unneeded `getAuthenticatedApiClient`/`GenerateLeafletRequest` imports.
- `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx` — wraps render in `QueryClientProvider`, uses the real `useLeaflet` module (`jest.requireActual`) so the real hook drives both test cases, and mocks `QUERY_KEYS` on `api/client` (matching `useLeaflet.test.ts`'s existing pattern) since the module is no longer fully mocked away.

Validation: `CI=true react-scripts test` on both `LeafletGenerateTab.test.tsx` (2/2 pass) and `useLeaflet.test.ts` (5/5 pass, no regression); `CI=false npm run build` → `Compiled successfully.`; `eslint` on both changed files → clean.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01ShBJBzrsondLpmHVcTrKUq

## Status
DONE
