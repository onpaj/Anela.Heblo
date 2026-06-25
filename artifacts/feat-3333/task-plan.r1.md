# Task Plan: Migrate Analytics Hooks to Generated API Client

## Overview

Replace raw `(apiClient as any).http.fetch(...)` calls in two React Query hooks with typed generated-client method invocations, remove the duplicate hand-written interfaces from the hook files, and update the chart components to consume the generated types (including the `date: Date` change and the `DailyBankStatementStatistics` rename).

Four files change; no new files are created. Tasks are ordered so each chart component task follows its hook task, since the chart imports depend on what the hook re-exports.

---

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

---

### task: update-invoice-import-chart

**Goal:** Update `InvoiceImportChart.tsx` to use `item.date` as a `Date` directly rather than calling `parseISO` on a string.

**Files to change:**
- `frontend/src/components/charts/InvoiceImportChart.tsx` — remove `parseISO` call; use `item.date!` (non-null assertion) where the date is accessed

**Implementation steps:**
1. Locate every usage of `parseISO(item.date)` (or equivalent string-to-Date conversion) in the component.
2. Replace each with `item.date!` since the backend always populates the field and the type is now `Date`.
3. Remove any now-unused `parseISO` import from `date-fns` if it is no longer referenced elsewhere in the file.

**Acceptance criteria:**
- No TypeScript errors in this file.
- No `parseISO` call remains for `item.date` in this component.
- `npm run build` passes.

**Dependencies:** migrate-invoice-import-statistics-hook

---

### task: migrate-bank-statement-import-statistics-hook

**Goal:** Replace the raw fetch call in `useBankStatements.ts` with `apiClient.analytics_GetBankStatementImportStatistics(...)`, remove duplicate interfaces, re-export generated equivalents, and retain `GetBankStatementImportStatisticsRequest` with string dates (converting to `Date` inside `queryFn`).

**Files to change:**
- `frontend/src/api/hooks/useBankStatements.ts` — replace raw fetch with typed client call; remove duplicate interfaces (except `GetBankStatementImportStatisticsRequest`); add re-exports; convert string dates to `Date` before passing to client method; add `?? []` fallback for `statistics` field

**Implementation steps:**
1. Remove the hand-written duplicate interfaces (all except `GetBankStatementImportStatisticsRequest`).
2. Add re-export statements for the generated equivalents from the generated client barrel.
3. Keep `GetBankStatementImportStatisticsRequest` with `string` date fields as-is (it is part of the public API of this hook file per FR-7).
4. In the `queryFn`, convert the string date fields from `GetBankStatementImportStatisticsRequest` to `Date` objects before calling `apiClient.analytics_GetBankStatementImportStatistics(...)`.
5. Add `?? []` fallback when accessing the `statistics` field of the response (since the generated type declares it `DailyBankStatementStatistics[] | undefined`).
6. Ensure no `(apiClient as any)` usage remains.

**Acceptance criteria:**
- No TypeScript errors in this file.
- `GetBankStatementImportStatisticsRequest` interface is still exported with string date fields.
- No `(apiClient as any)` usage remains.
- `npm run build` passes.

**Dependencies:** none

---

### task: update-bank-statement-import-chart

**Goal:** Update `BankStatementImportChart.tsx` to use `item.date` as a `Date` directly (removing string parsing), rename `BankStatementImportStatisticsDto` references to `DailyBankStatementStatistics`, and add the `?? []` fallback where needed.

**Files to change:**
- `frontend/src/components/charts/BankStatementImportChart.tsx` — remove date string parsing; use `item.date!`; update type references from `BankStatementImportStatisticsDto` to `DailyBankStatementStatistics`; add `?? []` on `statistics` access if present in this file

**Implementation steps:**
1. Replace every `parseISO(item.date)` (or equivalent) with `item.date!`.
2. Rename all references to `BankStatementImportStatisticsDto` → `DailyBankStatementStatistics` (import and usage sites).
3. Remove any now-unused imports (e.g. `parseISO`, old DTO type) if they are no longer referenced elsewhere in the file.
4. If the component accesses `data.statistics` directly, add `?? []` to guard against `undefined`.

**Acceptance criteria:**
- No TypeScript errors in this file.
- No reference to `BankStatementImportStatisticsDto` remains.
- No `parseISO` call remains for `item.date` in this component.
- `npm run build` and `npm run lint` pass.

**Dependencies:** migrate-bank-statement-import-statistics-hook
