# Specification: Fix onSuccess invalidation key in useEnqueueInvoiceImport

## Summary
The `useEnqueueInvoiceImport` mutation in `frontend/src/api/hooks/useAsyncInvoiceImport.ts` invalidates React Query caches on success using the key `[...QUERY_KEYS.invoices, "jobs"]`, which does not prefix-match the running-jobs query registered under `[...QUERY_KEYS.invoices, "import", "jobs", "running"]`. As a result the intended immediate cache invalidation is a silent no-op. This is a small, self-contained bug fix: replace the incorrect key with the exported canonical key factory so the running-jobs query is actually invalidated when a new import is enqueued.

## Background
When a user enqueues an async invoice import, the `useEnqueueInvoiceImport` mutation's `onSuccess` callback is meant to invalidate the running-jobs query so the newly created job appears in the `InvoiceImportJobTracker` UI immediately, without waiting for the next poll.

React Query invalidation uses prefix matching on the query key array. The key currently passed to `invalidateQueries` — `[...QUERY_KEYS.invoices, "jobs"]` — inserts `"jobs"` directly after the invoices prefix, whereas `useRunningInvoiceImportJobs` registers its cache entry under `[...QUERY_KEYS.invoices, "import", "jobs", "running"]` (`"import"` comes before `"jobs"`). Because the segment sequences diverge at the first position after the invoices prefix, the invalidation key never matches the running-jobs entry, so nothing is invalidated.

The functional impact is currently masked: `useRunningInvoiceImportJobs` sets `refetchInterval: 5000`, so the new job becomes visible within ~5 seconds regardless of the broken invalidation. However, the intentional immediate refresh silently does nothing today, and the defect becomes user-visible if the poll interval is lengthened or removed. The file already exports a canonical key factory (`invoiceImportQueryKeys`, lines 123-128) whose `runningJobs()` and `jobs()` helpers return the correct keys, but `onSuccess` does not use it.

## Functional Requirements

### FR-1: Correct the onSuccess invalidation key
The `onSuccess` callback of `useEnqueueInvoiceImport` must invalidate the running-jobs query using a key that prefix-matches `[...QUERY_KEYS.invoices, "import", "jobs", "running"]`. The fix must use the exported canonical key factory rather than a hand-written literal, so the key stays in sync with the query definition.

Preferred implementation — invalidate the broad job prefix so all import-job queries (running jobs and any job-status entries) are refreshed:

```typescript
onSuccess: () => {
  queryClient.invalidateQueries({ queryKey: invoiceImportQueryKeys.jobs() });
},
```

`invoiceImportQueryKeys.jobs()` returns `[...QUERY_KEYS.invoices, "import", "jobs"]`, which prefix-matches both the running-jobs query and the per-job status query — the broadest correct choice and consistent with the original intent of refreshing the job view after enqueue.

Acceptable narrower alternative — invalidate only the running-jobs query:

```typescript
onSuccess: () => {
  queryClient.invalidateQueries({ queryKey: invoiceImportQueryKeys.runningJobs() });
},
```

**Acceptance criteria:**
- After `useEnqueueInvoiceImport` resolves successfully, the cache entry for `useRunningInvoiceImportJobs` (key `[...QUERY_KEYS.invoices, "import", "jobs", "running"]`) is invalidated and refetched.
- The invalidation key is produced by the `invoiceImportQueryKeys` factory (`.jobs()` or `.runningJobs()`), not a hand-written array literal.
- The stale literal `[...QUERY_KEYS.invoices, "jobs"]` no longer appears in `onSuccess`.
- No other behavior of the hook (mutation function, URL, request body, error handling) is changed.

### FR-2: Immediate visibility of the enqueued job
With the corrected key, an enqueued job must appear in the running-jobs list via invalidation-triggered refetch, independent of the `refetchInterval` poll.

**Acceptance criteria:**
- With `refetchInterval` polling hypothetically disabled, enqueuing an import still causes the new job to appear in the running-jobs list within one refetch cycle triggered by the `onSuccess` invalidation.
- The existing `refetchInterval: 5000` poll remains unchanged and continues to function as a fallback.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. The change triggers one additional (already-intended) refetch of the running-jobs endpoint per successful enqueue. If the broad `jobs()` prefix is used, any active job-status query is also refetched; these are lightweight polled queries already refetching on short intervals, so the incremental cost is negligible.

### NFR-2: Security
No security impact. No change to authentication, authorization, request payloads, endpoints, or data exposure. The mutation continues to use `getAuthenticatedApiClient()` and the same `/api/invoices/import/enqueue-async` endpoint.

## Data Model
No data model changes. Relevant runtime cache entities (React Query keys), unchanged by this fix except for correcting which key is invalidated:
- Enqueue mutation → `POST /api/invoices/import/enqueue-async`
- Running jobs query → key `[...QUERY_KEYS.invoices, "import", "jobs", "running"]` → `GET /api/invoices/import/running-jobs`
- Job status query → key `[...QUERY_KEYS.invoices, "import", "jobs", "status", jobId]` → `GET /api/invoices/import/job-status/{jobId}`
- Canonical key factory `invoiceImportQueryKeys`: `all()`, `jobs()`, `jobStatus(jobId)`, `runningJobs()`

## API / Interface Design
No API surface change. This is an internal frontend fix confined to the `onSuccess` callback of `useEnqueueInvoiceImport` in `frontend/src/api/hooks/useAsyncInvoiceImport.ts`. Public hook signatures and their return types are unchanged.

## Dependencies
- `@tanstack/react-query` — `useMutation`, `useQueryClient`, `invalidateQueries` prefix-matching semantics.
- Existing exports within the same file: `QUERY_KEYS.invoices` and the `invoiceImportQueryKeys` factory.
- No new libraries, services, or external dependencies.

## Out of Scope
- Refactoring the other query keys in the file (`useInvoiceImportJobStatus`, `useRunningInvoiceImportJobs`) to consume the `invoiceImportQueryKeys` factory. Desirable for consistency but not required for this fix; may be noted for a follow-up.
- Changing polling intervals, `staleTime`, or `gcTime` on any query.
- Any change to the `InvoiceImportJobTracker` component or backend endpoints.
- Changes to the mutation's request/response handling or error surface.

## Open Questions
None.

## Status: COMPLETE
