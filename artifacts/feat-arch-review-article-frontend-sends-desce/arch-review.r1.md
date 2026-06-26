# Architecture Review: Fix Article Feedback List Sort-Direction Parameter Mismatch

## Skip Design: true

## Architectural Fit Assessment

This is a contract-alignment bug fix, not a feature. The fix lives entirely within an existing seam that already has a proven shape:

- **Backend contract** (`ArticlesController.cs:88`) declares `sortDescending` — this is the source of truth and is **not** changed.
- **Generic feedback abstraction** (`frontend/src/components/feedback/types.ts:31`) already uses `sortDescending` on `GenericFeedbackParams`.
- **Sibling adapters** (`useKbFeedbackAdapter.ts:9`, `useLeafletFeedbackAdapter.ts:9`) already forward `sortDescending: params.sortDescending` straight through to their hooks.
- **Outlier**: only `useArticleFeedbackAdapter.ts:10` renames it to `descending`, and `ArticleFeedbackListParams.descending` in `useArticles.ts:73` is the only field that does not match the backend wire key.

The fix removes the only deviation from an already-consistent pattern. There is no architectural decision to make — the question is simply "make article match its siblings and the backend." Integration points are limited to one hook, one params type, one adapter, and one test fixture.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────┐
│ Feedback UI (page + GenericFeedbackFilters)                  │
│   state: { sortDescending: boolean, ... }                    │
└───────────────────┬──────────────────────────────────────────┘
                    │ GenericFeedbackParams { sortDescending }
                    ▼
┌──────────────────────────────────────────────────────────────┐
│ Adapters (article / kb / leaflet)                            │
│   useArticleFeedbackAdapter   ← change: pass through         │
│   useKbFeedbackAdapter        ← unchanged (already correct)  │
│   useLeafletFeedbackAdapter   ← unchanged (already correct)  │
└───────────────────┬──────────────────────────────────────────┘
                    │ ArticleFeedbackListParams { sortDescending }
                    ▼
┌──────────────────────────────────────────────────────────────┐
│ useArticleFeedbackListQuery (useArticles.ts)                 │
│   builds: ?sortDescending=<bool>                             │
└───────────────────┬──────────────────────────────────────────┘
                    │ HTTP GET
                    ▼
┌──────────────────────────────────────────────────────────────┐
│ ArticlesController.FeedbackList (ASP.NET model binding)      │
│   [FromQuery] bool sortDescending = true   (UNCHANGED)       │
└──────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Align the field name, don't just patch the query key

**Options considered:**
1. Patch only the `searchParams.append` call (`'descending'` → `'sortDescending'`) and leave the TS field named `descending`.
2. Rename the TS field `ArticleFeedbackListParams.descending` → `sortDescending` and update the wire key, so the field name and query key match.

**Chosen approach:** Option 2 — rename the field and the wire key together.

**Rationale:** Option 1 fixes the symptom but leaves a field-name/wire-key divergence that invites the same bug back the next time someone refactors the hook. Sibling adapters (`useKbFeedbackAdapter`, `useLeafletFeedbackAdapter`) already use the field name `sortDescending`; making the article hook match the sibling convention eliminates the special case in `useArticleFeedbackAdapter` (the `descending: params.sortDescending` mapping line disappears).

#### Decision 2: Do not generalize the fix

**Options considered:**
1. Use the OpenAPI-generated client for this endpoint instead of a hand-written `URLSearchParams` builder, eliminating the class of bug entirely.
2. Add a lint rule or codegen check to detect drift between hand-rolled query keys and the OpenAPI contract.
3. Surgical fix only.

**Chosen approach:** Option 3 — surgical fix.

**Rationale:** The spec scopes this explicitly out of scope, and CLAUDE.md mandates surgical changes. The pattern of hand-rolling `URLSearchParams` already exists across other hooks in this codebase (it's how `useArticleFeedbackListQuery` and `useSubmitArticleFeedbackMutation` are built today). Generalizing belongs in a separate audit ticket.

## Implementation Guidance

### Directory / Module Structure

No new files. Touch only:

```
frontend/src/api/hooks/useArticles.ts
  - L73:   ArticleFeedbackListParams.descending → sortDescending
  - L267:  if (params.descending !== undefined)       → params.sortDescending
  - L268:  searchParams.append('descending', ...)     → 'sortDescending'

frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts
  - L10:   descending: params.sortDescending          → sortDescending: params.sortDescending

frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts
  - L103:  descending: true                           → sortDescending: true

frontend/src/api/hooks/__tests__/useArticles.test.ts  (or equivalent — see "Prerequisites")
  - NEW regression test asserting the URL contains sortDescending=... and not descending=...
```

**Out of scope** (do not touch): `ArticlesController.cs`, `GetArticleFeedbackListRequest`, sibling adapters, `GenericFeedbackParams`.

### Interfaces and Contracts

**Wire contract (frozen by backend):**
```
GET /api/articles/feedback/list
  ?hasFeedback=<bool>
  &requestedBy=<string>
  &sortBy=<string>
  &sortDescending=<bool>      ← THE FIX
  &page=<int>
  &pageSize=<int>
```

**TypeScript params (after rename):**
```typescript
export interface ArticleFeedbackListParams {
  hasFeedback?: boolean;
  requestedBy?: string;
  sortBy?: string;
  sortDescending?: boolean;   // was: descending
  page?: number;
  pageSize?: number;
}
```

**Invariant for future maintainers:** every field in `ArticleFeedbackListParams` must use the same name as its query string key. The query string key must match the backend `[FromQuery]` parameter name.

### Data Flow

```
1. User clicks sort toggle in feedback list page
2. GenericFeedbackFilters updates state.sortDescending
3. Page passes GenericFeedbackParams { sortDescending } to useArticleFeedbackAdapter
4. Adapter forwards { sortDescending } unchanged to useArticleFeedbackListQuery
5. Hook appends sortDescending=<value> to URLSearchParams
6. ASP.NET binds the value into bool sortDescending = true
7. MediatR request handler orders the query accordingly
8. Sorted rows render in the UI
```

The default-true behavior on initial load is preserved end-to-end: if the param is `undefined`, the URL omits the key and the backend default (`true`) takes effect.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale `descending` reference in code or tests after rename causes compile failure | Low | `npm run build` and `npm run lint` are required gates; grep for `descending` in the article feedback paths before commit. |
| Regression test asserts the wrong shape (e.g. mocks the hook itself, so the URL is never built) | Medium | Test must intercept the HTTP layer (e.g. via `apiClient.http.fetch` mock or MSW) and inspect the actual URL, not just spy on `useArticleFeedbackListQuery`. |
| External caller still sending the old `descending` key (highly unlikely — this is an internal endpoint) | Low | Out of scope per spec NFR-3; revisit if telemetry shows otherwise. |
| Adapter test at line 103 still asserts `descending: true` and silently passes because the adapter mock is loose | Medium | Update the assertion to `sortDescending: true` and verify it fails before the production code change (TDD red step). |

## Specification Amendments

None substantive. Two clarifications worth recording for the implementer:

1. **Adapter rename is in scope.** Spec FR-2 says "All call sites … are updated." The most consequential call site is `useArticleFeedbackAdapter.ts:10`, where the `descending: params.sortDescending` translation line **becomes a no-op pass-through and should be deleted**, not preserved with the renamed key. After the change, the article adapter matches the sibling adapter shape exactly.
2. **Regression test placement.** Spec FR-4 calls for a hook-level test. The closest existing test infrastructure is `frontend/src/api/hooks/__tests__/`. The test must mock `apiClient.http.fetch` and assert on the URL string the hook constructs — mocking `useArticleFeedbackListQuery` itself (as the adapter test does) cannot verify the wire contract.

## Prerequisites

None. No migrations, no config, no infrastructure changes. The backend contract already exists and is correct; the fix is a pure frontend rename plus one test.