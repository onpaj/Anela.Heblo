# Implementation: extract-shared-ragfeedback-mapper

## What was implemented

Extracted the RAG feedback-list mapping logic that was previously duplicated (as a private local function in `useKnowledgeBase.ts` and as raw `response.json()` casting in `useSmartsuppDraftReplyFeedbackListQuery.ts`) into a single shared module at `frontend/src/components/feedback/ragFeedbackMapping.ts`. Both hooks now import and call `toLocalFeedbackListResponse` from that module. The Smartsupp hook was also migrated from the manual `smartsuppClient`/`URLSearchParams`/`response.json()` pattern to the typed generated API client (`getAuthenticatedApiClient().smartsupp_GetDraftReplyFeedbackList(...)`), which was the root cause identified in the arch review.

## Files created/modified

- `frontend/src/components/feedback/ragFeedbackMapping.ts` — new shared module exporting `GeneratedFeedbackListShape` (structural interface accepted by both generated response classes), `LocalFeedbackListResponse` (canonical local shape), `toLocalFeedbackChunk`, and `toLocalFeedbackListResponse`
- `frontend/src/api/hooks/useKnowledgeBase.ts` — removed `GeneratedGetFeedbackListResponse` import and the local `toLocalFeedbackChunk`/`toLocalFeedbackListResponse` functions; imports `toLocalFeedbackListResponse` from the shared module instead; `RagFeedbackChunk` import also dropped (no longer used directly)
- `frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts` — rewritten to use `getAuthenticatedApiClient().smartsupp_GetDraftReplyFeedbackList(...)` and `toLocalFeedbackListResponse` from the shared module; `DraftReplyFeedbackListResponse` is now a type alias for `LocalFeedbackListResponse` rather than a manually-written duplicate interface

## Tests

- `frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts` — ran unchanged, all 15 tests pass (including `maps generated Date/undefined fields into the local string/null shape`)

## How to verify

```bash
cd frontend
CI=true npx react-scripts test src/api/hooks/__tests__/useKnowledgeBase.test.ts --watchAll=false
```

All 15 tests should pass. The `npm run build` failure (`TS18048: 'pages' is possibly 'undefined'` in `VisitorInfoCard.tsx`) is pre-existing and not introduced by this change — confirmed by reproducing it against the baseline commit with my files stashed.

## Notes

- `useSmartsuppFeedbackAdapter.ts` required no edits: the fields it reads (`query.data?.logs`, `.stats`, `.totalCount`, `.totalPages`, `.pageNumber`) are all still present on `LocalFeedbackListResponse` with the same types.
- The build failure in `VisitorInfoCard.tsx` (`TS18048`) is pre-existing (reproduced without my changes). Not in scope to fix here.
- No behavioral change: the mapper logic is a byte-for-byte copy of what was in `useKnowledgeBase.ts`; the Smartsupp hook now goes through the typed client (which handles Date deserialization) rather than raw `response.json()`, so the `createdAt`/`sentAt` `.toISOString()` calls in the mapper now operate on proper `Date` objects as the generated code produces.

## PR Summary

Extracted the RAG feedback-list mapper shared by the KnowledgeBase and Smartsupp draft-reply hooks into `frontend/src/components/feedback/ragFeedbackMapping.ts` so there is a single authoritative conversion of generated `Date`/`undefined` shapes into the `string`/`null` shapes expected by `ragFeedbackTypes.ts` consumers. The Smartsupp hook was also migrated from the manual fetch/URLSearchParams/`response.json()` pattern (which bypassed Date deserialization) to the typed generated API client, so both hooks now share identical data-access and mapping paths.

### Changes
- `frontend/src/components/feedback/ragFeedbackMapping.ts` — new shared module with `toLocalFeedbackChunk`, `toLocalFeedbackListResponse`, and supporting interfaces
- `frontend/src/api/hooks/useKnowledgeBase.ts` — deleted local mapper functions; now imports from shared module
- `frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts` — rewired to typed generated client and shared mapper

## Status
DONE
