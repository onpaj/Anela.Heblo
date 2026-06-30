### task: migrate-invoice-import-statistics-hook

**Goal:** Replace the raw fetch call in `useInvoiceImportStatistics` with `apiClient.analytics_GetInvoiceImportStatistics(...)` and remove the duplicate `DailyInvoiceCount` and `InvoiceImportStatisticsResponse` interfaces, re-exporting the generated equivalents instead.

**Files to change:**
- `frontend/src/api/hooks/useInvoiceImportStatistics.ts` — replace raw fetch with typed client call; remove duplicate interfaces; add re-exports of generated types

**Implementation steps:**
1. Remove the hand-written `DailyInvoiceCount` and `InvoiceImportStatisticsResponse` interface declarations.
2. Add re-export statements for the generated equivalents (e.g. `export type { DailyInvoiceCount, InvoiceImportStatisticsResponse } from '../generated-client'` — adjust path to match the actual generated client barrel).
3. Replace the `queryFn` body: remove `(apiClient as any).http.fetch(...)` and replace with `apiClient.analytics_GetInvoiceImportStatistics(...)`, awaiting and returning the result.
4. Ensure the query key and return type remain unchanged so no callers outside this file break.

**Acceptance criteria:**
- No TypeScript errors in this file (`npm run build` passes).
- The hook still returns data shaped as `InvoiceImportStatisticsResponse`.
- No `(apiClient as any)` usage remains in the file.

**Dependencies:** none
