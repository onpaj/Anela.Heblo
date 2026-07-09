# Architecture Review: Fix onSuccess invalidation key in useEnqueueInvoiceImport

## Skip Design: true

This is a purely internal frontend logic fix — correcting one React Query invalidation key in a data-fetching hook. There are no new or changed visual components, screens, layouts, or design decisions. The existing `InvoiceImportJobTracker` UI is untouched; only the timing/correctness of its cache refresh improves. No design work is required.

## Architectural Fit Assessment

The fix aligns cleanly with existing patterns and requires no architectural change.

Verified against the codebase:
- **Query-key convention.** `QUERY_KEYS` is a centralized const map in `frontend/src/api/client.ts` (`invoices: ["invoices"]` at line 500). The file `useAsyncInvoiceImport.ts` already defines and exports the canonical sub-key factory `invoiceImportQueryKeys` (lines 123–128) with `all()`, `jobs()`, `jobStatus(jobId)`, and `runningJobs()`. The bug is simply that `onSuccess` (line 40) hand-writes a literal `[...QUERY_KEYS.invoices, "jobs"]` that diverges from the factory-produced key the query actually registers under (`[...QUERY_KEYS.invoices, "import", "jobs", "running"]`, line 92). React Query prefix-matching never matches, so the invalidation is a silent no-op.
- **Integration points.** The single integration point is React Query's cache. The mutation (`useEnqueueInvoiceImport`) invalidates; the query (`useRunningInvoiceImportJobs`) consumes. Both live in the same file. No other module reads or writes these keys — `invoiceImportQueryKeys` is exported for external use, but the fix does not depend on external consumers.
- **No test coverage exists.** There is no `useAsyncInvoiceImport.test.ts` (confirmed via glob and directory listing). This is the one gap worth addressing (see Specification Amendments).

The spec's preferred implementation (`invoiceImportQueryKeys.jobs()`) is the correct, idiomatic choice: it produces the factory-derived key and prefix-matches both the running-jobs and per-job-status queries.

## Proposed Architecture

### Component Overview

```
useEnqueueInvoiceImport (mutation)
        │  POST /api/invoices/import/enqueue-async
        │
        └─ onSuccess ──► queryClient.invalidateQueries({
                              queryKey: invoiceImportQueryKeys.jobs()   ← FIX
                          })
                              │  prefix = ["invoices","import","jobs"]
                              ▼
        ┌───────────────────────────────────────────────┐
        │  React Query cache (prefix match)              │
        │   • ["invoices","import","jobs","running"]  ◄──┤ useRunningInvoiceImportJobs
        │   • ["invoices","import","jobs","status",id]◄──┤ useInvoiceImportJobStatus
        └───────────────────────────────────────────────┘
                              │ triggers refetch
                              ▼
                   InvoiceImportJobTracker (UI, unchanged)
```

### Key Design Decisions

#### Decision 1: Use the broad `jobs()` prefix rather than the narrow `runningJobs()` key
**Options considered:**
- (a) `invoiceImportQueryKeys.runningJobs()` — invalidates only the running-jobs list.
- (b) `invoiceImportQueryKeys.jobs()` — invalidates all import-job queries (running + status).

**Chosen approach:** (b) `invoiceImportQueryKeys.jobs()`.

**Rationale:** After enqueue, the intent is to refresh the job view. `jobs()` is the broadest correct choice and matches the original comment's intent ("show the new job"). The extra cost is negligible — the status query already polls at `refetchInterval: 2000` and running-jobs at `5000`; both are lightweight. Consistent with the spec's stated preference. If a reviewer prefers minimal blast radius, `runningJobs()` is an acceptable narrower fallback and satisfies all acceptance criteria.

#### Decision 2: Use the factory, never a literal
**Options considered:** hand-written array literal vs. `invoiceImportQueryKeys` factory.
**Chosen approach:** Factory (`invoiceImportQueryKeys.jobs()`).
**Rationale:** The bug's root cause is a literal drifting out of sync with the query registration. Using the factory makes the invalidation key structurally impossible to diverge from the query key. This is the single most important constraint in the spec (acceptance criterion: key must come from the factory, not a literal).

## Implementation Guidance

### Directory / Module Structure
No new files. The change is confined to:
- `frontend/src/api/hooks/useAsyncInvoiceImport.ts` — lines 38–41, the `onSuccess` body.

Recommended new file (see amendments):
- `frontend/src/api/hooks/useAsyncInvoiceImport.test.ts` — a focused regression test.

### Interfaces and Contracts
No interface changes. Public hook signatures (`useEnqueueInvoiceImport`, `useInvoiceImportJobStatus`, `useRunningInvoiceImportJobs`) and the `invoiceImportQueryKeys` factory are unchanged. The exact edit:

```typescript
onSuccess: () => {
  queryClient.invalidateQueries({ queryKey: invoiceImportQueryKeys.jobs() });
},
```

The stale literal `[...QUERY_KEYS.invoices, "jobs"]` must no longer appear. `invoiceImportQueryKeys` is already defined in the same module (below the hook); because `onSuccess` is a closure invoked at runtime, referencing the const defined later in module scope is safe (it is initialized at module load, long before any mutation resolves). No import changes needed.

### Data Flow
1. User enqueues import → `mutationFn` POSTs to `/api/invoices/import/enqueue-async`.
2. On HTTP success → `onSuccess` calls `invalidateQueries` with prefix `["invoices","import","jobs"]`.
3. React Query marks the running-jobs entry (and any active job-status entry) stale and refetches.
4. `useRunningInvoiceImportJobs` refetches `/api/invoices/import/running-jobs`; the new job renders in `InvoiceImportJobTracker` immediately, independent of the 5s poll.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Bug silently reintroduced by future edits (literal drift) | Low | Fix uses the factory; add a regression test asserting the enqueued-mutation `onSuccess` invalidates a key that prefix-matches `runningJobs()`. |
| Broad `jobs()` prefix causes unexpected extra refetches | Very Low | Only two lightweight, already-polling queries share the prefix; incremental cost is one refetch. Acceptable per NFR-1. |
| Reviewer disagreement on broad vs. narrow key | Very Low | Both are spec-acceptable; default to `jobs()`, note `runningJobs()` as fallback. |

## Specification Amendments

1. **Add a regression test (recommended, not currently in spec's required scope).** No test file exists for this hook. The spec's acceptance criteria are behavioral ("cache entry is invalidated and refetched") but leave verification manual. Add `frontend/src/api/hooks/useAsyncInvoiceImport.test.ts` that renders `useEnqueueInvoiceImport` with a `QueryClient`, spies on `queryClient.invalidateQueries`, resolves the mutation, and asserts it was called with a key equal to `invoiceImportQueryKeys.jobs()` (or that the key prefix-matches `invoiceImportQueryKeys.runningJobs()`). This locks in the fix and guards against literal drift — the exact failure mode this bug represents. This is the only substantive addition; the spec otherwise fully and correctly describes the fix.

2. **No other amendments.** The spec's Out-of-Scope boundaries (no refactor of the other queries to consume the factory, no polling/staleTime changes, no component/backend changes) are architecturally sound and should be honored to keep the change surgical.

## Prerequisites
None. No migrations, config, feature flags, or infrastructure. The `invoiceImportQueryKeys` factory the fix depends on already exists in the target file. The change is a one-line edit plus an optional test, buildable and testable with the existing frontend toolchain (`npm run build`, `npm run lint`, `npm test`).
