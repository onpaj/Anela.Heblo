# Specification: Fix `useArticleFeedbackListQuery` Type/Field Mismatches

## Summary
The `useArticleFeedbackListQuery` hook in `frontend/src/api/hooks/useArticles.ts` uses raw `fetch` and casts the JSON response directly to a TypeScript type that does not match the backend payload. This causes consumers to receive `undefined` for several fields at runtime. The fix is to add an explicit field-by-field mapping (mirroring `useGetArticleQuery`) and to realign the TypeScript types and consuming components with the backend contract.

## Background
`GetArticleFeedbackListHandler` returns a JSON payload whose shape diverges from the frontend `ArticleFeedbackListResponse`/`ArticleFeedbackSummary` types at multiple points:

- **Top-level collection key**: backend serialises `items`, frontend reads `articles`.
- **Per-item timestamp**: backend `CreatedAt` vs. frontend `generatedAt`.
- **Per-item comment indicator**: backend boolean `HasComment` (true when a non-empty comment exists) vs. frontend boolean `hasFeedback`.
- **Per-item phantom field**: frontend declares `feedbackComment: string | null`, which the backend never sends from this endpoint.

The bug is invisible to TypeScript because `return response.json()` is cast directly to the response type, bypassing structural checking. Other hooks in the same file (notably `useGetArticleQuery` at lines ~155–188) handle this correctly by doing an explicit field-by-field mapping after `await response.json()`. The feedback list hook should follow the same pattern. This work is filed by the daily architecture review routine; it is a correctness/contract-alignment fix, not a feature change.

## Functional Requirements

### FR-1: Map backend payload to frontend type inside the query function
`useArticleFeedbackListQuery` must perform an explicit field-by-field mapping from the raw JSON response to the typed result, instead of casting `response.json()` directly. The mapping must mirror the structure used in `useGetArticleQuery` (consistent style across the file).

**Acceptance criteria:**
- The query function awaits `response.json()` into an untyped intermediate value.
- Each field returned to React Query is populated from a known backend field with a safe default for null/undefined values.
- No direct cast of the raw payload to `ArticleFeedbackListResponse` remains.
- The mapping handles `raw.items` being `undefined` (returns `[]`) and provides defaults for pagination/stats fields.

### FR-2: Align top-level collection key
The frontend type and consumer code must expose the list under the field name `articles` (the established frontend term), while the mapping reads from `raw.items` (the backend key).

**Acceptance criteria:**
- `ArticleFeedbackListResponse.articles: ArticleFeedbackSummary[]` remains the public TypeScript shape returned by the hook.
- The hook reads `raw.items ?? []` and assigns it to `articles` after per-item mapping.
- No consumer reads `items` directly from the hook's result.

### FR-3: Align per-item field names with backend
`ArticleFeedbackSummary` must reflect the actual backend contract:

- Replace `generatedAt: string | null` with `createdAt: string | null`.
- Replace `hasFeedback: boolean` with `hasComment: boolean`.
- Remove `feedbackComment: string | null` (this endpoint does not return the comment text).

**Acceptance criteria:**
- `ArticleFeedbackSummary` in `useArticles.ts` exposes exactly: `id`, `topic`, `title`, `requestedBy`, `createdAt`, `precisionScore`, `styleScore`, `hasComment`. (Field types unchanged from current except for the renames and removal above.)
- The mapping in FR-1 emits these names.
- A grep for `generatedAt`, `hasFeedback`, and `feedbackComment` in feedback-list usage sites returns no remaining references after the change.

### FR-4: Update consuming components
Any frontend component that reads from `useArticleFeedbackListQuery` must be updated to the new field names: `articles`, `createdAt`, `hasComment`. Any reference to the removed `feedbackComment` on list rows must be removed or replaced (e.g., a row-level "has comment" indicator that previously relied on `feedbackComment` truthiness should use `hasComment` instead).

**Acceptance criteria:**
- All `.tsx`/`.ts` files that consume the feedback list query compile cleanly against the updated types.
- UI behaviour that previously depended on `generatedAt`/`hasFeedback`/`feedbackComment` continues to render the equivalent information using the renamed fields.
- Existing visual layout, column order, and copy are unchanged unless a consumer was rendering a field that simply never had data (in which case it is replaced with the correct field).

### FR-5: Test coverage for the mapping
Add or update unit tests that exercise the mapping function with representative backend payloads, including: a typical populated row, a row with all nullable fields null, and an empty `items` array.

**Acceptance criteria:**
- A unit test asserts that given a sample raw payload with `items`, `totalCount`, `page`, `pageSize`, `totalPages`, and `stats`, the hook's mapped result has the expected typed shape with all renames applied.
- A unit test asserts that a payload missing `items` yields `articles: []` and does not throw.
- A unit test asserts that `hasComment` is preserved from the backend (i.e., the frontend does not synthesize it from another field).
- Tests live in the existing test file for `useArticles` (or a co-located new file if none exists) and use the project's existing test runner (jest/vitest, whichever is in use for `frontend/`).

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance change is expected. The mapping is an O(n) pass over a paginated list (page size already capped server-side) and runs once per query response. No additional network calls.

### NFR-2: Security
No change in data sensitivity or auth posture. The endpoint and its auth requirements are unchanged. No new fields are exposed; one field (`feedbackComment`) that was never actually delivered is removed from the type.

### NFR-3: Backwards compatibility
This is a frontend-only correctness fix. Because the previous code returned `undefined` for the affected fields, no working consumer can depend on the old names with the old behaviour. The TypeScript renames are breaking at the type level but align the code with runtime reality.

### NFR-4: Code consistency
The mapping must follow the same structural style as `useGetArticleQuery` in the same file so that both hooks read similarly. No new abstraction or helper is introduced solely for this fix (YAGNI).

## Data Model

**Backend (unchanged) — `GetArticleFeedbackListResponse`:**
- `items: ArticleFeedbackSummary[]`
- `totalCount: int`
- `page: int`
- `pageSize: int`
- `totalPages: int`
- `stats: { totalArticles, totalWithFeedback, avgPrecisionScore, avgStyleScore }`

**Backend (unchanged) — `ArticleFeedbackSummary`:**
- `Id`, `Topic`, `Title`, `RequestedBy`
- `CreatedAt: DateTimeOffset`
- `PrecisionScore: int?`, `StyleScore: int?`
- `HasComment: bool` (computed server-side as `!string.IsNullOrWhiteSpace(FeedbackComment)`)

**Frontend (after change) — `ArticleFeedbackListResponse`:**
- `articles: ArticleFeedbackSummary[]` (mapped from `items`)
- `totalCount`, `page`, `pageSize`, `totalPages`, `stats` (unchanged)

**Frontend (after change) — `ArticleFeedbackSummary`:**
- `id`, `topic`, `title`, `requestedBy`
- `createdAt: string | null` (renamed from `generatedAt`)
- `precisionScore: number | null`, `styleScore: number | null`
- `hasComment: boolean` (renamed from `hasFeedback`)
- `feedbackComment` removed

## API / Interface Design

No backend API change. No new endpoints, events, or routes.

The hook signature `useArticleFeedbackListQuery(params)` is unchanged; only the shape of its returned data is corrected. The internal flow becomes:

1. `fetch` the list endpoint as today.
2. `await response.json()` into an untyped `raw` value.
3. Build the typed `ArticleFeedbackListResponse`:
   - `articles = (raw.items ?? []).map(mapSummary)`
   - `totalCount`, `page`, `pageSize`, `totalPages` with numeric defaults
   - `stats` with the existing default object when missing
4. Return the typed object to React Query.

Per-item mapping (`mapSummary`) produces: `{ id, topic, title, requestedBy, createdAt, precisionScore, styleScore, hasComment }` with safe defaults as shown in the brief.

## Dependencies

- React Query (existing, no version change).
- No new libraries.
- Backend `GetArticleFeedbackListHandler` is the source of truth and is not modified.

## Out of Scope

- Migrating `useArticleFeedbackListQuery` to the NSwag-generated client. The brief proposes a localized mapping fix, not a migration; a generated-client migration is a larger architectural change and should be tracked separately if desired.
- Exposing the actual `feedbackComment` text on the list endpoint. If a future UI needs the comment text on list rows, that is a backend change (extend `ArticleFeedbackSummary`) and is not part of this fix.
- Refactoring other hooks in `useArticles.ts` that use raw `fetch`. Only the feedback list hook is in scope.
- Backend changes of any kind.
- E2E test additions. The E2E suite runs nightly and is not gated on this fix; unit tests are sufficient to lock in the mapping.

## Open Questions

None.

## Status: COMPLETE