All changes committed. Here's the output summary:

---

# Implementation: arch-review gap fixes for useSubmitArticleFeedbackMutation

## What was implemented

Addressed all remaining gaps identified by the arch-review after the r1 implementation round:

1. **Header merge order fix** — `getAuthenticatedFetch` now spreads caller headers first, auth headers last (`{ ...(init.headers ?? {}), ...headers }`), so the helper's real `Authorization` always wins over any caller-supplied value.
2. **Auth-wins unit test** — New test in `client.test.ts` asserts that a caller-supplied `Authorization: Bearer caller-bogus` is replaced by `Bearer test-token` (the real mock token).
3. **Regression guard tightened** — `authenticated-api-usage.test.ts` removes `(apiClient as any).http.fetch` from the "valid pattern" allowlist; adds new test banning `(apiClient as any)`, `as any).http`, `as any).baseUrl` in migrated hooks (scoped via `MIGRATED_HOOKS` set for incremental rollout).
4. **Test isolation fix** — `useArticles.test.ts` adds `mockGetApiBaseUrl` reference and restores its return value in `beforeEach` after `jest.clearAllMocks()` wiped the factory implementation.
5. **Docs rewritten** — `api-client-generation.md` replaces both `(apiClient as any).baseUrl` "✅ CORRECT" examples with the new helpers pattern; adds AVOID callout and enforcement rule.
6. **JSDoc expanded** — `getAuthenticatedFetch` JSDoc now documents: 409-branching use case, no-toast/no-401-redirect divergence from `getAuthenticatedApiClient`, and auth-wins header precedence.

## Files created/modified

- `frontend/src/api/client.ts` — fixed header merge order; expanded JSDoc
- `frontend/src/api/__tests__/client.test.ts` — added auth-wins test (asserts `Bearer test-token` wins over `Bearer caller-bogus`)
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts` — tightened guards, added ban test with `MIGRATED_HOOKS` allowlist
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — added `mockGetApiBaseUrl` + `mockReturnValue` fix
- `docs/development/api-client-generation.md` — replaced `as any` examples, added AVOID callout

## Tests

All 22 targeted tests pass:
- `src/api/__tests__/client.test.ts` — 8 passed (including new auth-wins test)
- `src/api/__tests__/authenticated-api-usage.test.ts` — 4 passed (including new ban test)
- `src/api/hooks/__tests__/useArticles.test.ts` — 10 passed

## How to verify

```bash
cd frontend
CI=true npm test -- --testPathPattern="src/api/__tests__/client\.test|src/api/__tests__/authenticated-api-usage|src/api/hooks/__tests__/useArticles" --watchAll=false
```

## Notes

The build fails with `Cannot find module '@openfeature/react-sdk'` — this is pre-existing infrastructure: the worktree symlinks to the main branch's `node_modules`, which doesn't have packages added on this feature branch. Our changes don't touch any feature-flag or app-insights imports. Lint passes cleanly on all modified files.

The `MIGRATED_HOOKS` set in the ban test is intentionally limited to `["useArticles.ts"]` — 160+ other hooks still use the old `(apiClient as any)` pattern and are out of scope for this PR.

## PR Summary

Fixes the three arch-review gaps that remained open after the r1 implementation: (1) auth header precedence bug in `getAuthenticatedFetch`, (2) stale guard test that whitelisted the very patterns it was supposed to ban, (3) documentation that continued teaching the old `(apiClient as any)` approach.

### Changes
- `frontend/src/api/client.ts` — flipped spread order so auth wins; expanded JSDoc with 409 use case, no-toast warning, and auth-precedence note
- `frontend/src/api/__tests__/client.test.ts` — auth-wins test with specific `Bearer test-token` assertion
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts` — removed `as any` from valid-pattern allowlist; added `MIGRATED_HOOKS`-scoped ban test
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — `mockGetApiBaseUrl` reset fix
- `docs/development/api-client-generation.md` — replaced `(apiClient as any)` examples with helper patterns; added AVOID callout

## Status
DONE