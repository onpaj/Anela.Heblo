# Code Review: Refactor LeafletGenerateTab to use useGenerateLeafletMutation

## Summary

The component refactoring correctly replaces direct API client calls with the new `useGenerateLeafletMutation` hook, eliminating hand-rolled state management in favor of React Query's mutation primitives. The test file is properly updated to wrap renders in `QueryClientProvider` and uses `jest.requireActual` to preserve the real hook while stubbing sibling exports. The minimal adaptation (adding `QUERY_KEYS` to the mock) is well-justified and matches the existing codebase pattern. All acceptance criteria met: both test cases pass, build succeeds, linting is clean.

## Review Result: PASS

### task: refactor-leaflet-generate-tab-to-use-mutation-hook
**Status:** PASS

**Verification:**

1. **Spec compliance** — All requirements from the task context are met:
   - Component now calls `useGenerateLeafletMutation()` instead of `getAuthenticatedApiClient().leaflet_Generate(...)`
   - Hand-rolled `isLoading` state removed entirely; both read sites now use `generateLeaflet.isPending`
   - Reset/error-classification control flow (clear `generationId`/`errorBanner`, catch `GenerateLeafletResponse` + `LeafletEmptyRetrieval` vs. generic transient banner) unchanged
   - Imports cleaned: `getAuthenticatedApiClient` and `GenerateLeafletRequest` removed; `useGenerateLeafletMutation` imported

2. **Architecture adherence** — Component correctly integrates with the new mutation hook:
   - Calls `generateLeaflet.mutateAsync(...)` inside the existing try/catch, preserving error handling semantics
   - Uses generated client's typed exceptions (`GenerateLeafletResponse`, `ErrorCodes.LeafletEmptyRetrieval`) as designed
   - Mutation state (`isPending`) drives both the form's disabled state and the loading skeleton conditional

3. **Test completeness & correctness**:
   - `QueryClientProvider` wrapper is required and correctly configured (retry disabled for predictable unit tests)
   - `jest.requireActual('../../../api/hooks/useLeaflet')` preserves the real `useGenerateLeafletMutation` while allowing selective stubbing of `useSubmitLeafletFeedbackMutation` for the child component
   - Adaptation (adding `QUERY_KEYS: { leaflet: ['leaflet'] }` to the `api/client` mock) is justified: the real module evaluates `leafletKeys` at import time when `jest.requireActual` is used, matching the pattern already established in `useLeaflet.test.ts`
   - Both test cases exercise the full mutation flow (error → banner): insufficient knowledge (422 + `LeafletEmptyRetrieval`) and transient failure (generic error)
   - Test passes confirmed (2/2); no regression in pre-existing `useLeaflet.test.ts` (5/5 pass)

4. **Code quality**:
   - Build passes (`CI=false npm run build` → `Compiled successfully`)
   - Linting passes (no eslint errors on both changed files)
   - Error handling is correct: catches thrown errors from the mutation, distinguishes between known error codes and transient failures

## Docs to Update

No documentation updates needed. This is an internal implementation refactoring of a single component — the component's external interface (props, behavior) and the generated API client remain unchanged. The move from direct API calls to the mutation hook is an architectural improvement internal to the module.

## Overall Notes

The implementation is thorough and clean. The developer understood the task deeply: they recognized that the test needed a `QueryClientProvider` for `useMutation` to work, correctly diagnosed why the real `useLeaflet` module failed to load (the full mock stripped `QUERY_KEYS`), and applied the minimal fix matching the existing project pattern rather than introducing a new approach. The RED→GREEN discipline is clearly evident in the commit history. No changes requested.
