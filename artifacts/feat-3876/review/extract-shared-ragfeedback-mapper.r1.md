# Code Review: Extract Shared RAG Feedback Mapper

## Summary

The implementation successfully extracts the duplicated RAG feedback-list mapping logic into a shared module (`ragFeedbackMapping.ts`), updates both hooks to use it, and migrates the Smartsupp hook from manual fetch/URLSearchParams to the typed generated API client. The refactoring maintains type safety, preserves all existing behavior (verified by passing tests), and eliminates code duplication as specified.

## Review Result: PASS

### task: extract-shared-ragfeedback-mapper

**Status:** PASS

The implementation correctly addresses all specification requirements:

1. **New shared module** — `ragFeedbackMapping.ts` correctly exports:
   - `GeneratedFeedbackListShape` as a structural interface that both generated response types satisfy
   - `LocalFeedbackListResponse` (canonical local shape used by both hooks)
   - Both mapper functions (`toLocalFeedbackChunk`, `toLocalFeedbackListResponse`) as byte-for-byte copies from the original `useKnowledgeBase.ts`

2. **useKnowledgeBase.ts updates** — Correctly:
   - Removes local mapper functions
   - Removes `GeneratedGetFeedbackListResponse` import (no longer needed)
   - Imports the shared `toLocalFeedbackListResponse`
   - Maintains structural type compatibility (return type matches `LocalFeedbackListResponse`)

3. **useSmartsuppDraftReplyFeedbackListQuery.ts rewrite** — Properly:
   - Migrates from manual fetch/URLSearchParams/response.json() to typed `getAuthenticatedApiClient().smartsupp_GetDraftReplyFeedbackList(...)`
   - Imports and uses the shared mapper
   - Makes `DraftReplyFeedbackListResponse` a type alias for `LocalFeedbackListResponse`
   - Maintains caller compatibility (fields read by `useSmartsuppFeedbackAdapter.ts` are unchanged)

4. **Tests pass** — `useKnowledgeBase.test.ts` runs with all 15 tests passing, including the mapping test that verifies Date/undefined conversion logic

5. **Date handling** — Now correct in Smartsupp hook: the typed client deserializes Date fields properly, so `.toISOString()` in the mapper operates on actual `Date` objects (not raw JSON strings from manual fetch)

The pre-existing TypeScript build error in `VisitorInfoCard.tsx` (TS18048) is properly documented as unrelated and reproducible without these changes.

## Docs to Update

- `CLAUDE.md` — Consider adding a note to the project-specific rules section: "**Shared mappers for generated API responses** — Date serialization and undefined→null shape conversions are centralized in mapper modules (e.g., `ragFeedbackMapping.ts`) to ensure both typed-client callers and raw-fetch fallbacks handle the same transformation." (Informational only; the pattern is now established and future similar extractions will follow this precedent.)

## Overall Notes

No cross-cutting issues. The structural interface pattern (`GeneratedFeedbackListShape`) is a clean way to allow both generated response types to be accepted by the mapper without creating a dependency between the Knowledge Base and Smartsupp API client modules. The refactoring correctly centralizes the source of truth for shape conversion and enables both hooks to follow identical data-access and mapping paths.
