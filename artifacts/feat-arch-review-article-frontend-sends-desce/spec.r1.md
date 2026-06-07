# Specification: Fix Article Feedback List Sort-Direction Parameter Mismatch

## Summary
The article feedback list sort-direction toggle is silently broken because the frontend sends the query parameter `descending` while the backend expects `sortDescending`. This spec aligns the frontend with the backend contract so user-selected sort direction is actually honored.

## Background
`ArticlesController.cs:88` declares the feedback-list endpoint parameter as `[FromQuery] bool sortDescending = true`. ASP.NET Core model binding requires the query string key to match the parameter name (`sortDescending`).

`useArticles.ts:267-268` builds the request with key `descending` instead:

```typescript
if (params.descending !== undefined)
    searchParams.append('descending', params.descending.toString());
```

Because the backend never receives the `sortDescending` key, it always falls back to the default value (`true` / descending). Any UI toggle for sort direction in the feedback list is silently ignored — no error surfaces, no warning is logged, and the list renders in descending order regardless of user choice.

The sibling parameters `sortBy` and `hasFeedback` are spelled correctly on both sides; `sortDescending` is the only mismatched key.

The backend parameter name is the contract (a public API surface consumed by the OpenAPI-generated client and any other future caller). The fix should therefore align the frontend with the backend.

## Functional Requirements

### FR-1: Frontend sends the correct query parameter key
Update the `useArticleFeedbackList` (or equivalent) request builder in `frontend/src/api/hooks/useArticles.ts` so the sort-direction value is appended under the key `sortDescending` rather than `descending`.

**Acceptance criteria:**
- The request URL produced by the feedback-list hook contains `sortDescending=true` or `sortDescending=false` (matching the user's selection), and never `descending=...`.
- When the user toggles the sort direction in the feedback list UI, the network request reflects the new value.
- The backend receives the value and returns rows ordered accordingly (ascending vs descending).

### FR-2: Internal frontend field naming stays consistent
The TypeScript `ArticleFeedbackListParams` (or equivalent params interface) is renamed so the field name matches the wire key — i.e. rename `descending` to `sortDescending` throughout the frontend. This keeps the internal model and the query string identical and prevents the same mismatch from being reintroduced.

**Acceptance criteria:**
- No frontend code references a `descending` field on the feedback-list params type.
- All call sites (hook consumers, UI toggle handlers, tests) compile and pass after the rename.
- `npm run build` and `npm run lint` succeed with no new warnings related to the rename.

### FR-3: UI toggle behavior verified end-to-end
The feedback list page exposes a sort-direction control (column header click or explicit toggle). After the fix, toggling it must change the order of rows.

**Acceptance criteria:**
- Manually toggling sort direction in the feedback list produces a visibly reordered list.
- Default state remains descending (matching the backend default) so existing users see no regression on initial load.
- The active direction is reflected in the UI (arrow indicator or similar) and matches the data ordering.

### FR-4: Regression coverage
A test guards against the parameter-name mismatch returning.

**Acceptance criteria:**
- A unit/integration test on the frontend hook asserts the produced query string contains `sortDescending=...` (and does not contain `descending=...`) when the param is set.
- The test fails if either side of the contract drifts again.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected — this is a query string key rename. Request payload size and backend handler cost are unchanged.

### NFR-2: Security
No security impact. The parameter is a boolean sort flag, not an authorization or data-scoping input. No new attack surface is introduced.

### NFR-3: Backward compatibility
The backend is the contract source of truth and is **not** being changed. No other known callers send `descending`, so no compatibility shim is required. If telemetry later surfaces external callers using the old key, that is addressed separately.

### NFR-4: Observability
None required beyond what already exists. The bug was silent because ASP.NET binding does not warn about unknown query keys; this is a known framework behavior and is out of scope to change.

## Data Model
No data model changes. The fix is confined to the HTTP query string contract between frontend and backend for the article feedback list endpoint.

## API / Interface Design

### Endpoint (unchanged)
The article feedback list endpoint declared in `ArticlesController.cs` continues to accept `sortDescending` as a boolean query parameter with a default of `true`.

### Frontend hook query string
Before:
```
GET /api/articles/.../feedback?...&descending=false
```

After:
```
GET /api/articles/.../feedback?...&sortDescending=false
```

### Frontend params type
Before:
```typescript
interface ArticleFeedbackListParams {
  // ...
  descending?: boolean;
}
```

After:
```typescript
interface ArticleFeedbackListParams {
  // ...
  sortDescending?: boolean;
}
```

All call sites — the feedback list page, the sort-toggle handler, and any tests — are updated to use the new field name.

## Dependencies
- `frontend/src/api/hooks/useArticles.ts` — request-building hook (primary change site).
- `backend/.../ArticlesController.cs` — read-only reference; the contract source of truth, not modified.
- Any UI components that pass `descending` into the hook (feedback list page, sort toggle, column header handlers).
- Existing frontend test infrastructure (Jest/Vitest + React Testing Library) for the regression test.

## Out of Scope
- Renaming the backend `sortDescending` parameter.
- Auditing other endpoints for similar frontend/backend key mismatches (worth doing, but tracked separately).
- Changing the default sort direction or any other feedback-list behavior.
- Adding a generic mechanism (lint rule, codegen check) to detect query-key drift between OpenAPI client and hand-written hooks.
- E2E test additions; unit/integration coverage on the hook is sufficient for this fix. The nightly E2E suite will exercise the page in its existing flows.

## Open Questions
None.

## Status: COMPLETE
