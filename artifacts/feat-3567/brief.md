## Module
Invoices

## Finding
In `frontend/src/api/hooks/useAsyncInvoiceImport.ts`, the `useEnqueueInvoiceImport` mutation's `onSuccess` callback attempts to invalidate the running-jobs query after an import is enqueued:

```typescript
// line 40 — useAsyncInvoiceImport.ts
onSuccess: () => {
  // Invalidate running jobs queries to show the new job
  queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.invoices, "jobs"] });
},
```

However, `useRunningInvoiceImportJobs` (line 92 of the same file) registers its cache entry under a different key:

```typescript
queryKey: [...QUERY_KEYS.invoices, "import", "jobs", "running"]
```

React Query's prefix-matching means `[...QUERY_KEYS.invoices, "jobs"]` will never match `[...QUERY_KEYS.invoices, "import", "jobs", "running"]`. The running-jobs cache entry is therefore **not invalidated** on success, and the new job doesn't appear immediately in the UI.

The file already exports a canonical key factory at the bottom (`invoiceImportQueryKeys.runningJobs()`) that returns the correct key, but it isn't used in `onSuccess`.

## Why it matters
The `InvoiceImportJobTracker` relies on an immediate cache refresh to show the new job entry. The `refetchInterval: 5000` poll on `useRunningInvoiceImportJobs` means the job becomes visible within 5 seconds regardless, but the intentional immediate invalidation silently does nothing — a latent source of confusion when the polling interval changes or is removed.

## Suggested fix
Replace the incorrect key with the exported canonical factory:

```typescript
onSuccess: () => {
  queryClient.invalidateQueries({ queryKey: invoiceImportQueryKeys.runningJobs() });
},
```

Or use the broader prefix that covers all import job queries:

```typescript
queryClient.invalidateQueries({ queryKey: invoiceImportQueryKeys.jobs() });
```

---
_Filed by daily arch-review routine on 2026-07-08._
