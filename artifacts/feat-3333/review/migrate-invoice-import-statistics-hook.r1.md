# Code Review: migrate-invoice-import-statistics-hook

## Summary
The implementation correctly replaces the raw `(apiClient as any).http.fetch(...)` call with the typed `apiClient.analytics_GetInvoiceImportStatistics(...)` method from the generated client. The hand-written `DailyInvoiceCount` and `InvoiceImportStatisticsResponse` interfaces have been removed, and the three generated types (`DailyInvoiceCount`, `GetInvoiceImportStatisticsResponse`, `ImportDateType`) are properly re-exported. All acceptance criteria are satisfied.

## Review Result: PASS

### task: migrate-invoice-import-statistics-hook
**Status:** PASS

## Overall Notes
- No `(apiClient as any)` usage remains — the generated client method is called directly and is synchronous (returns a `Promise` directly without `await`), matching the `queryFn` signature correctly.
- `getAuthenticatedApiClient()` is called synchronously inside `queryFn`, consistent with how the client factory works (it is not async in `client.ts`). The old code incorrectly used `await` on it; the new code does not — this is a correct fix as a side-effect of the migration.
- The return type annotation `Promise<GetInvoiceImportStatisticsResponse>` is accurate: the generated method signature is `analytics_GetInvoiceImportStatistics(dateType: ImportDateType | undefined, daysBack: number | null | undefined): Promise<GetInvoiceImportStatisticsResponse>`.
- `daysBack ?? null` correctly satisfies the `number | null | undefined` parameter type.
- `dateType as ImportDateType` cast is safe because `UseInvoiceImportStatisticsParams.dateType` is constrained to `'InvoiceDate' | 'LastSyncTime'`, which exactly matches the `ImportDateType` enum values.
- `staleTime: 5 * 60 * 1000` and `gcTime: 10 * 60 * 1000` are unchanged.
- `queryKey: [...QUERY_KEYS.invoiceImportStatistics, dateType, daysBack]` is unchanged.
- The old `InvoiceImportStatisticsResponse` export name is gone; callers must now use `GetInvoiceImportStatisticsResponse`. Any consumer importing the old name would need updating, but that is out of scope for this task and the re-export of the generated name is the correct approach.
