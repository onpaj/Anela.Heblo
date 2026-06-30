# Implementation: migrate-invoice-import-statistics-hook

## What was implemented

Replaced the raw `(apiClient as any).http.fetch(...)` call in `useInvoiceImportStatistics` with a typed `apiClient.analytics_GetInvoiceImportStatistics(...)` call. Removed the hand-written `DailyInvoiceCount` and `InvoiceImportStatisticsResponse` interface declarations and added re-exports of the generated equivalents (`DailyInvoiceCount`, `GetInvoiceImportStatisticsResponse`, `ImportDateType`) from `api-client.ts`.

## Files created/modified

- `frontend/src/api/hooks/useInvoiceImportStatistics.ts` — removed duplicate interfaces, replaced raw fetch with generated client call, added re-exports of generated types

## How to verify

1. Run `cd frontend && npx tsc --noEmit` — only pre-existing deprecation warnings, no new errors.
2. Confirm no `(apiClient as any)` remains in the file.
3. Confirm the file exports `DailyInvoiceCount`, `GetInvoiceImportStatisticsResponse`, and `ImportDateType`.
4. Confirm `queryFn` calls `apiClient.analytics_GetInvoiceImportStatistics(dateType as ImportDateType, daysBack ?? null)`.

## Notes

- `getAuthenticatedApiClient()` is synchronous; the `async` keyword and `await` on the client call were removed accordingly.
- The `queryFn` is now a plain function returning a `Promise` (no `async/await` wrapper needed since the method itself returns a `Promise`).
- `staleTime` and `gcTime` values are unchanged (5 min / 10 min).
- The `UseInvoiceImportStatisticsParams` interface (with its string-union `dateType`) is retained as the public API for callers; the cast `as ImportDateType` bridges it to the generated enum.

## Status
DONE
