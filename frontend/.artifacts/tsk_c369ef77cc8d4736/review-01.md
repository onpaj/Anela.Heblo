# Review — StatusBar: replace `as any` config fetch with `useConfigurationQuery`

## Verdict: done

## What was checked
- Diff (commit `954c5f58`): `frontend/src/components/StatusBar.tsx` (modified), `frontend/src/components/__tests__/StatusBar.test.tsx` (new). No other files touched — matches the plan/design/architecture scope exactly (`useConfiguration.ts`, `useHealth.ts`, the generated client, and the backend are all untouched).
- Ran `tsc --noEmit` (frontend): clean, no errors.
- Ran `eslint src/components/StatusBar.tsx src/components/__tests__/StatusBar.test.tsx`: clean, no warnings/errors.
- Ran `react-scripts test src/components/__tests__/StatusBar.test.tsx --watchAll=false`: 4/4 passing.

## Conformance to plan/design/architecture

**FR-1 (replace manual fetch)** — met. `getAuthenticatedApiClient`, `.baseUrl`, `.http.fetch`, and the `as any` casts are gone. `useConfigurationQuery` is imported and called; `configData` is typed as `GetConfigurationResponse | undefined` throughout, no `any` remains in the file.

**FR-2 (preserve display/fallback behavior)** — met. The `appInfo` derivation exactly matches the architecture step's prescribed code:
- `version` uses `||` (empty string treated as absent) as required.
- `environment` and `mockAuth` use `??` so an explicit `false` from the backend is respected rather than overwritten by the local fallback — this was called out as a correctness requirement in both design and architecture docs, and it's implemented correctly. Verified by test 4 (`respects an explicit useMockAuth: false from the backend instead of the local fallback`), which sets `getRuntimeConfig().useMockAuth: true` and backend `useMockAuth: false`, and asserts the "Mock Auth" badge does NOT render — confirming `??` behavior over `||`.
- Loading state: `if (isLoading) return null;`, resolving Open Question 1 as designed (blank footer only during initial load, no second blank frame after settle).
- No `console.warn` calls remain, resolving Open Question 2 as designed.

**FR-3 (remove dead code)** — met. `useState`/`useEffect` imports removed (not used elsewhere in the file), `getAuthenticatedApiClient` import removed.

**FR-4 (apiUrl continuity)** — met. `apiUrl: config.apiUrl` still sourced from `getRuntimeConfig()` only, never from `configData` (which has no `apiUrl` field on the generated type).

**Test coverage** — the architecture doc called this a "nice-to-have, not a prerequisite" given no existing coverage; the implementer added one anyway (`StatusBar.test.tsx`, 4 tests) covering exactly the three cases the architecture doc suggested: success path with backend data, loading state, and the `??`-vs-`||` correctness case for `useMockAuth: false`. This exceeds what was required and directly protects the fix's most fragile behavior.

## Correctness
- No logic errors found. The `appInfo` object shape is unchanged (`version`, `environment`, `apiUrl`, `mockAuth`), so the unmodified JSX below it (lines 49–201) continues to work without changes, as intended.
- Scope discipline: no unrelated cleanup, no touching of `useHealth.ts`'s identical `as any` pattern (correctly left out per the architecture doc's "out of scope" note), no changes to `useConfiguration.ts` or the generated client.

## Non-blocking observations
- Architecture doc flagged that a real backend error now costs two requests (initial + `retry: 1`) instead of one before falling back — purely informational, not a regression in user-visible behavior, and already accepted as a known, non-blocking tradeoff in the architecture step. No action needed.

No issues found that block this change. Implementation, tests, and validation are all consistent with the plan, design, and architecture artifacts.
