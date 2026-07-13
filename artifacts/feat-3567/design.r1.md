# Design: Fix onSuccess invalidation key in useEnqueueInvoiceImport

## Component Design

`useEnqueueInvoiceImport` (`frontend/src/api/hooks/useAsyncInvoiceImport.ts`) — a React Query mutation hook that POSTs to `/api/invoices/import/enqueue-async`. Its `onSuccess` callback changes from calling `invalidateQueries` with a hand-written literal key to calling it with the module's existing `invoiceImportQueryKeys.jobs()` factory method:

```typescript
onSuccess: () => {
  queryClient.invalidateQueries({ queryKey: invoiceImportQueryKeys.jobs() });
},
```

No new components, no signature changes. `invoiceImportQueryKeys`, `useRunningInvoiceImportJobs`, and `useInvoiceImportJobStatus` are consumed as-is; only the argument passed to `invalidateQueries` changes.

## Data Schemas

No schema or payload changes. React Query cache keys involved (unchanged in structure, only the invalidation call now targets the correct prefix):

- `invoiceImportQueryKeys.jobs()` → `[...QUERY_KEYS.invoices, "import", "jobs"]` (invalidation target)
- `invoiceImportQueryKeys.runningJobs()` → `[...QUERY_KEYS.invoices, "import", "jobs", "running"]` (query invalidated via prefix match)
- `invoiceImportQueryKeys.jobStatus(jobId)` → `[...QUERY_KEYS.invoices, "import", "jobs", "status", jobId]` (also matched via prefix, incidental)
