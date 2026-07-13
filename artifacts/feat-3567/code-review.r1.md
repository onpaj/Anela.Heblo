## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/api/hooks/useAsyncInvoiceImport.ts:50,92` — `useInvoiceImportJobStatus` and `useRunningInvoiceImportJobs` still build their `queryKey` as hand-written array literals (`[...QUERY_KEYS.invoices, "import", "jobs", "status", jobId || ""]` / `[...QUERY_KEYS.invoices, "import", "jobs", "running"]`) instead of using `invoiceImportQueryKeys.jobStatus(jobId)` / `.runningJobs()`, which are already exported a few lines below. Noted as out-of-scope in the spec, but worth a quick follow-up so the query definitions and the invalidation call share one source of truth.
