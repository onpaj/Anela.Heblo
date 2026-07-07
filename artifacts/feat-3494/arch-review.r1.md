# Architecture Review: Replace raw `http.fetch` bypass in `useFinancialOverviewQuery`

## Skip Design: true

## Architectural Fit Assessment

This is a call-site substitution inside a single hook file, not an architectural change. It touches no module boundary, no contract, no DTO shape, and no UI. It brings `useFinancialOverview.ts` into line with a convention that is already documented (`docs/development/api-client-generation.md`, "CRITICAL: URL Construction Rules") and already universally followed by every sibling hook I inspected:

- `frontend/src/api/hooks/useWarehouseStatistics.ts:10` — `apiClient.catalog_GetWarehouseStatistics()`
- `frontend/src/api/hooks/useProductMarginSummary.ts:25-32` — `apiClient.analytics_GetProductMarginSummary(...)`

`useFinancialOverview.ts` is the outlier, not the template. The dev-guidelines doc even uses `(apiClient as any).http.fetch` as its canonical **wrong** example and states verbatim: *"Also wrong: uses private fields of the generated client — breaks silently on NSwag regeneration."* The fix this spec describes is literally applying an already-written rule to the one file that violates it.

I verified the three load-bearing claims from the spec directly against source rather than trusting the brief:

1. **The generated method exists with the exact required signature.** `frontend/src/api/generated/api-client.ts:3809`:
   `financialOverview_GetFinancialOverview(months: number | null | undefined, includeStockData: boolean | undefined, excludedDepartments: string[] | null | undefined, includeCurrentMonth: boolean | undefined): Promise<GetFinancialOverviewResponse>`.
   Its internal query-string construction (lines 3810-3823) is byte-for-byte equivalent to the hook's current manual `URLSearchParams` block, including the empty-array case (the generated method also just `forEach`s over `excludedDepartments`, appending nothing when the array is empty).
2. **It still routes through the authenticated pipeline.** The method's implementation ends in `this.http.fetch(url_, options_)` (line 3832), and `this.http` is the same `authenticatedHttp` object `getAuthenticatedApiClient()` (`frontend/src/api/client.ts:276-291`) wires up with auth headers, 401 handling, and error toasts. Nothing about auth/error behavior changes — only the call site stops reaching around the typed method to get to that same object.
3. **Error semantics are compatible.** `processFinancialOverview_GetFinancialOverview` (line 3837) resolves a typed `GetFinancialOverviewResponse` via `.fromJS` on 200, and calls `throwException(...)` (which constructs a `SwaggerException extends Error`) on all other statuses. This satisfies the hook's existing `useQuery<GetFinancialOverviewResponse, Error>` type parameter without any cast.

No sibling instance of this bypass exists elsewhere in `frontend/src/api/hooks/*.ts` (confirmed via `grep -r '\.http\.fetch' frontend/src`) — the only other `.http.fetch` hits are in `client.ts` itself (where it's the correct low-level implementation, not a bypass), two page components (`ManufacturingStockAnalysis.tsx`, `TransportBoxDetail.tsx`), and a test file exercising `client.ts` internals directly. Those two page-component instances are pre-existing, separate violations of the same rule but are out of this task's scope per the spec — flagged below under Specification Amendments so they aren't silently forgotten, not because this task should touch them.

**Skip Design confirmed true.** There is no new component, no new screen, no changed layout, no changed visual state (loading/error/empty already render identically — the `Error` shape consumed by the UI is unchanged). This is an internal data-fetching implementation swap behind an unchanged hook signature and return type.

## Proposed Architecture

### Component Overview
No new components. One existing file changes: `frontend/src/api/hooks/useFinancialOverview.ts`. The `queryFn` body is replaced; everything else (import list minus `URLSearchParams` usage, re-exported types, hook signature, `queryKey`, `staleTime`, `gcTime`) stays as-is.

### Key Design Decisions

#### Decision 1: Call the generated method with `excludedDepartments` passed through unmodified (no ternary)
**Options considered:**
- (a) `apiClient.financialOverview_GetFinancialOverview(months, includeStockData, excludedDepartments.length > 0 ? excludedDepartments : undefined, includeCurrentMonth)` — the brief's original suggestion.
- (b) `apiClient.financialOverview_GetFinancialOverview(months, includeStockData, excludedDepartments, includeCurrentMonth)` — pass the array through as-is.

**Chosen approach:** (b), per the spec.

**Rationale:** Verified directly in the generated code (api-client.ts:3817-3818): the method already guards with `excludedDepartments !== undefined && excludedDepartments !== null` before `forEach`-ing, and an empty array produces zero appended query params either way. The ternary in option (a) is dead logic that adds a branch with no behavioral difference — option (b) is simpler and matches this codebase's convention of passing hook parameters straight through to generated methods (see `useProductMarginSummary.ts`, which forwards all its parameters unconditionally).

#### Decision 2: Do not add `try/catch` or a `getApiBaseUrl()`/`getAuthenticatedFetch()` escape hatch
**Options considered:**
- (a) Keep a thin wrapper that catches and re-throws with a custom message (mimicking the current `Failed to fetch financial overview: ${response.statusText}` string).
- (b) Let the generated method's `SwaggerException` propagate untouched.
- (c) Switch to the `getApiBaseUrl()` + `getAuthenticatedFetch()` escape hatch described in `docs/architecture/development_guidelines.md`.

**Chosen approach:** (b).

**Rationale:** The escape hatch (c) is documented as reserved for endpoints whose business outcome can't yet be expressed by the generated client (e.g. a not-yet-annotated 412 status) — that doesn't apply here; a plain typed GET with only success/error is exactly the case the generated method is meant to cover directly. Option (a) would reintroduce a hand-rolled error path for no benefit: `SwaggerException extends Error` and already carries a populated `message`, satisfying `useQuery<GetFinancialOverviewResponse, Error>` and the consuming UI's `error.message` usage with no adapter needed. Every sibling hook (`useWarehouseStatistics`, `useProductMarginSummary`) does zero wrapping around the generated call — matching that convention keeps this hook unsurprising to the next reader.

## Implementation Guidance

### Directory / Module Structure
No structural change. Single file: `frontend/src/api/hooks/useFinancialOverview.ts`.

### Interfaces and Contracts
No contract changes. Hook signature, return type (`UseQueryResult<GetFinancialOverviewResponse, Error>`), `queryKey` shape, and the re-exported generated types (`GetFinancialOverviewResponse`, `MonthlyFinancialDataDto`, `FinancialSummaryDto`, `StockChangeDto`, `StockSummaryDto`) are all unchanged, as required by the spec's acceptance criteria.

The new `queryFn` (verbatim, matches spec FR-1):
```typescript
queryFn: async () => {
  const apiClient = getAuthenticatedApiClient();
  return await apiClient.financialOverview_GetFinancialOverview(
    months,
    includeStockData,
    excludedDepartments,
    includeCurrentMonth,
  );
},
```
Everything above this in the file (imports, re-exports, `queryKey`, `staleTime`, `gcTime`) stays untouched. Note `getAuthenticatedApiClient()` is synchronous (`frontend/src/api/client.ts:276-278` returns `ApiClient`, not `Promise<ApiClient>`) — keep the existing no-`await` call, do not add `await` even though a couple of sibling hooks (`useWarehouseStatistics.ts`, `useProductMarginSummary.ts`) inconsistently do; that's a pre-existing harmless-but-sloppy pattern elsewhere, not something to propagate.

### Data Flow
Unchanged end-to-end: `FinancialOverview.tsx` → `useFinancialOverviewQuery(months, includeStockData, excludedDepartments, includeCurrentMonth)` → TanStack Query cache/`queryFn` → (new) `apiClient.financialOverview_GetFinancialOverview(...)` → `this.http.fetch` (the same authenticated fetcher as before) → `GET /api/FinancialOverview?...` → `GetFinancialOverviewResponse.fromJS(...)` → hook's `data`. The only change is that the query-string construction and response parsing/error-throwing move from hand-written code in the hook into the generated method — the wire format and the object shapes flowing back to the component are identical.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Subtle query-string mismatch between old manual construction and generated method (e.g. parameter ordering, boolean stringification) | Low | Already verified by direct source comparison (see Architectural Fit Assessment); FR-2's acceptance criteria give exact expected query strings for both the default case and the `excludedDepartments` case — confirm both against the network tab during manual verification. |
| Error message text changes (`Failed to fetch financial overview: ...` → `SwaggerException` message) could surface literally in the UI and look different to users | Low | Grep `frontend/src/components/pages/FinancialOverview.tsx` and its `financial-overview/*` children for any place `error.message` is rendered verbatim vs. just checked for truthiness/used to show a generic banner; only rewrite the queryFn body — do not chase this into a UI change unless the check reveals the raw message is displayed to users, in which case flag it as a follow-up rather than expanding this task. |
| No unit test currently exists for `useFinancialOverview.ts` (unlike e.g. `useJournal.simple.test.ts`, which mocks `getAuthenticatedApiClient` and its generated method), so a regression in query-string params could pass unnoticed until manual/E2E verification | Low-Medium | Not a blocker for this narrowly-scoped refactor per the spec's Out-of-Scope section, but worth a follow-up: a small test mocking `financialOverview_GetFinancialOverview` and asserting it's called with the right positional args would catch future regressions cheaply. Not required to close this task. |
| Reviewer/future arch-review re-flags the two pre-existing `.http.fetch` bypasses in `ManufacturingStockAnalysis.tsx` / `TransportBoxDetail.tsx` as "still not fixed" after this PR merges | Low | These are explicitly out of scope per spec (§Out of Scope, §Background note). Mention in the PR description that they were noticed but intentionally left untouched, so the next arch-review pass (or a human) can decide whether to file them as separate findings rather than assuming this PR should have caught them. |

## Specification Amendments
None required to the functional requirements — the spec's investigation is accurate and the proposed `queryFn` body is correct as written. One addition worth folding into the PR (not the spec, which is already `Status: COMPLETE`):

- Note in the PR description (not required as a spec change) that `ManufacturingStockAnalysis.tsx:206`, `ManufacturingStockAnalysis.tsx:607`, and `TransportBoxDetail.tsx:221` contain the same `(apiClient as any).http.fetch` / `apiClient.http.fetch` pattern in page components rather than hooks. They were found during this review's exploration but are out of scope here (per spec's Out of Scope section, which explicitly limits the audit to `useFinancialOverview.ts`). Worth a separate arch-review finding or follow-up ticket so they don't get lost.

## Prerequisites
None. The generated client already contains the needed method (confirmed at `frontend/src/api/generated/api-client.ts:3809`); no NSwag regeneration, no OpenAPI/backend change, and no other file needs to change first. This can be implemented directly against the current `main`.
