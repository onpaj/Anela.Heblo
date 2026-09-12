# Code Review: Add useGenerateLeafletMutation Hook

## Summary
The implementation adds a `useGenerateLeafletMutation` hook that wraps the generated `leaflet_Generate` client method in a React Query mutation, following the established one-hook-per-operation pattern in `useLeaflet.ts`. The hook correctly preserves the generated client's typed-throw behavior for error responses, and the test coverage validates both success and error paths.

## Review Result: PASS

### task: add-generate-leaflet-mutation-hook
**Status:** PASS

## Detailed Findings

**Spec Compliance:**
- ✓ `GenerateLeafletParams` interface correctly defined with `topic: string`, `audience: AudienceType`, `length: LeafletLength`
- ✓ `useGenerateLeafletMutation` hook exported and positioned between `useUploadLeafletDocumentMutation` and `useSubmitLeafletFeedbackMutation` as specified
- ✓ Imports added correctly: `AudienceType`, `GenerateLeafletRequest`, `GenerateLeafletResponse`, `LeafletLength`
- ✓ Hook calls `getAuthenticatedApiClient().leaflet_Generate(new GenerateLeafletRequest(params))` directly, preserving typed-throw behavior
- ✓ No `onSuccess`/`onError` query invalidation added (correct per spec)
- ✓ Returns `Promise<GenerateLeafletResponse>` with proper typing

**Test Coverage:**
- ✓ "resolves with the GenerateLeafletResponse returned by leaflet_Generate" — validates success path and verifies mock is called exactly once
- ✓ "propagates a rejected GenerateLeafletResponse (422) unchanged out of mutateAsync" — validates error passthrough behavior
- ✓ Both tests use correct mocking pattern (mockGetClient, createWrapper) consistent with existing test suite
- ✓ Tests reference correct generated types: `AudienceType.EndConsumer`, `LeafletLength.Medium`, `ErrorCodes.LeafletEmptyRetrieval`
- ✓ Test execution: 5/5 tests pass (3 existing `useSubmitLeafletFeedbackMutation` + 2 new `useGenerateLeafletMutation`)

**Architecture Adherence:**
- ✓ Follows established one-hook-per-operation convention already used by all other Leaflet mutations in the file
- ✓ Calls generated client method directly (per documented pattern in `docs/development/api-client-generation.md`)
- ✓ Preserves typed-throw error handling: 422 → `GenerateLeafletResponse`, 400 → `ProblemDetails`, other → `SwaggerException`
- ✓ Clear JSDoc comment explains the design rationale

**Correctness:**
- ✓ Proper async/await handling in mutationFn
- ✓ Correct instantiation: `new GenerateLeafletRequest(params)` maps the interface fields to the generated request class
- ✓ No mutation of external state
- ✓ Standard React Query `useMutation` usage pattern
- ✓ Build succeeds: `npm run build` reports "Compiled successfully" with no new TypeScript errors
- ✓ Lint clean: `npx eslint` reports no errors on modified files

**Code Quality:**
- ✓ Minimal, focused implementation (15 lines for hook body)
- ✓ Follows file conventions: JSDoc style, placement order, naming
- ✓ No hardcoded values or magic strings
- ✓ Proper type safety throughout

## Overall Notes
This is a clean, well-tested addition that follows established patterns. The decision to call the generated client method directly (rather than re-implementing as raw `http.fetch` like sibling hooks) is correct and well-justified by the spec — it preserves the exact error-typing behavior that consuming code (`LeafletGenerateTab.tsx` in the full feature) depends on. The test suite is appropriate for the change scope: it validates the mutation's success and error paths without over-specifying implementation details.

Note: This task implements only Phase 1 (the hook addition and its tests). Phase 2 (refactoring `LeafletGenerateTab.tsx` to consume the hook) is a separate task.
