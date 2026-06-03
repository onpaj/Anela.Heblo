# Invoices Frontend Hooks — Replace Hand-Written Types with Generated Imports

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace hand-written TypeScript interfaces in `useIssuedInvoices.ts` and `useAsyncInvoiceImport.ts` with the equivalent generated client types from `frontend/src/api/generated/api-client.ts`, repairing every consumer site whose imports or field reads break as a result, without changing runtime behavior.

**Architecture:** Type-only refactor. Both hooks continue to call `fetch` directly and return `response.json()` unchanged. Hand-written interface declarations are deleted; consumers and the hooks `import type` the canonical `I*` (data-only) variants from `../generated/api-client`. The only locally-retained type is `IssuedInvoicesFilters` (a hook-input query-string shape with no generated counterpart). Consumer files lose any references to fields not present on the generated contract (notably `customerEmail`, `customerPhone`, `customerAddress`, `items`, `sync.error.code`, `sync.error.field`), and the modal's numeric `errorType` comparisons are repaired to the string-enum `IssuedInvoiceErrorType` shape. `IBackgroundJobInfo.id` is `string | undefined`, so the one consumer that derives a `string[]` is updated with a typed predicate.

**Tech Stack:** React, TypeScript, `@tanstack/react-query`, NSwag-generated `api-client.ts`, Jest + React Testing Library.

---

## Scope & File Map

### Files modified
- `frontend/src/api/hooks/useIssuedInvoices.ts` — delete local interfaces (keep `IssuedInvoicesFilters`); add generated `import type`; annotate return types; add FR-6 comment above each `response.json()`.
- `frontend/src/api/hooks/useAsyncInvoiceImport.ts` — delete all local interfaces; add generated `import type`; annotate return types; add FR-6 comment above each `response.json()`.
- `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts` — change the deleted `BackgroundJobInfo` import to `IBackgroundJobInfo` from generated client; convert string `createdAt` mock values to `Date`.
- `frontend/src/components/customer/IssuedInvoiceDetailModal.tsx` — remove UI/reads for fields not on `IIssuedInvoiceDetailDto` (`customerEmail`, `customerPhone`, `customerAddress`, `items`); repair `sync.error.errorType` numeric branch to the string `IIssuedInvoiceErrorDto.errorType` shape; remove `sync.error.code` and `sync.error.field` reads; repair `data.invoice.errorType === '0' | '1' | '2'` comparisons to `IssuedInvoiceErrorType` enum strings.
- `frontend/src/pages/customer/IssuedInvoicesPage.tsx` — narrow `runningJobs.map(j => j.id).filter(Boolean)` with a typed predicate so the result satisfies `setActiveJobIds(string[])`.

### Files verified-only (no edit expected, must still build/lint/test)
- `frontend/src/components/invoices/InvoiceImportJobTracker.tsx`
- `frontend/src/components/invoices/InvoiceImportRunningIndicator.tsx`
- `frontend/src/components/invoices/__tests__/InvoiceImportJobTracker.test.tsx`
- `frontend/src/components/customer/__tests__/IssuedInvoiceDetailModal.test.tsx`

### Generated types referenced (already present in `frontend/src/api/generated/api-client.ts`)
- `IIssuedInvoiceDto` (line 19687)
- `IIssuedInvoiceDetailDto` (line 19815) — adds only `syncHistory`, `concurrencyStamp`, `lastModificationTime`, `creatorId`, `lastModifierId`
- `IIssuedInvoiceSyncDataDto` (line 19870)
- `IIssuedInvoiceErrorDto` (line 19914) — only `errorType?: string` and `message?: string`
- `IssuedInvoiceErrorType` enum (line 19727) — `'General' | 'InvoicePaired' | 'ProductNotFound'`
- `IGetIssuedInvoicesListResponse` (line 19590)
- `IGetIssuedInvoiceDetailResponse` (line 19762)
- `IEnqueueImportInvoicesRequest` (line 20041)
- `IEnqueueImportInvoicesResponse` (line 20005)
- `IBackgroundJobInfo` (line 20160) — `id?: string`, `createdAt?: Date | undefined`, `startedAt?: Date | undefined`

---

## Test Strategy

This is a type-only refactor. There is no new behavior; the existing test suite is the regression net. Each task follows the pattern:

1. Establish baseline (existing tests pass on this branch).
2. Make the type/code change.
3. Run impacted tests; they must still pass.
4. Run `npm run build` and `npm run lint` to catch compile/lint regressions.
5. Commit.

Run targeted tests with `--testPathPattern` to keep iteration fast. The whole-suite run is done in the final task.

---

## Task 0: Baseline verification

**Files:** none — read-only verification.

- [ ] **Step 1: Confirm working directory and clean tree**

Run:
```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-invoices-frontend-hooks-re-d
git status
```

Expected: branch `feat-arch-review-invoices-frontend-hooks-re-d`, clean working tree (no uncommitted changes that could mask regressions).

- [ ] **Step 2: Baseline frontend build**

Run:
```bash
cd frontend && npm run build
```

Expected: build succeeds. If it does not, stop and fix the baseline first — any later failure attribution is meaningless on a broken baseline.

- [ ] **Step 3: Baseline lint**

Run:
```bash
cd frontend && npm run lint
```

Expected: lint succeeds with no errors. Note any pre-existing warnings; the refactor must not add new ones.

- [ ] **Step 4: Baseline targeted tests**

Run:
```bash
cd frontend && npx jest --testPathPattern="(useAsyncInvoiceImport|IssuedInvoiceDetailModal|InvoiceImportJobTracker)"
```

Expected: all three test files pass. If any fail on the baseline, fix or document that first — do not assume your refactor caused the failure later.

- [ ] **Step 5: Confirm generated client has the expected types**

Run:
```bash
grep -n "^export interface IIssuedInvoiceDto \|^export interface IIssuedInvoiceDetailDto \|^export interface IIssuedInvoiceSyncDataDto \|^export interface IIssuedInvoiceErrorDto \|^export enum IssuedInvoiceErrorType \|^export interface IGetIssuedInvoicesListResponse \|^export interface IGetIssuedInvoiceDetailResponse \|^export interface IEnqueueImportInvoicesRequest \|^export interface IEnqueueImportInvoicesResponse \|^export interface IBackgroundJobInfo " frontend/src/api/generated/api-client.ts
```

Expected: 10 lines, one per interface/enum listed in the *Generated types referenced* section above. If the count is off, run `npm run generate-api` (or note the discrepancy and stop).

---

## Task 1: Refactor `useAsyncInvoiceImport.ts`

**Files:**
- Modify: `frontend/src/api/hooks/useAsyncInvoiceImport.ts`

**Why this hook first:** it has the simplest local types (all four removable), and unblocks the test-file repair in Task 2 without yet touching the modal/page consumer code.

- [ ] **Step 1: Replace the import header**

Open `frontend/src/api/hooks/useAsyncInvoiceImport.ts`. Replace lines 1–31 (the existing imports plus the four local interface declarations) with:

```ts
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import type {
  IBackgroundJobInfo,
  IEnqueueImportInvoicesRequest,
  IEnqueueImportInvoicesResponse,
} from "../generated/api-client";
```

Note: `ImportResultDto` is deleted entirely (it was never used by any exported hook in this file, per the spec FR-3).

- [ ] **Step 2: Update the three hook return-type annotations**

Replace each `Promise<…>` annotation with the generated interface variant:

- In `useEnqueueInvoiceImport` (was line ~40): change
  ```ts
  mutationFn: async (request: EnqueueImportInvoicesRequest): Promise<EnqueueImportInvoicesResponse> => {
  ```
  to
  ```ts
  mutationFn: async (request: IEnqueueImportInvoicesRequest): Promise<IEnqueueImportInvoicesResponse> => {
  ```

- In `useInvoiceImportJobStatus` (was line ~73): change
  ```ts
  queryFn: async (): Promise<BackgroundJobInfo | null> => {
  ```
  to
  ```ts
  queryFn: async (): Promise<IBackgroundJobInfo | null> => {
  ```

- In `useRunningInvoiceImportJobs` (was line ~113): change
  ```ts
  queryFn: async (): Promise<BackgroundJobInfo[]> => {
  ```
  to
  ```ts
  queryFn: async (): Promise<IBackgroundJobInfo[]> => {
  ```

- [ ] **Step 3: Add the FR-6 wire-format comments**

Above each `return response.json();` statement in this file (three locations — one per hook), insert exactly:

```ts
      // Wire format: response is the I* interface variant (plain JSON). Date-typed fields
      // (createdAt, startedAt) arrive as ISO strings; parse with `new Date(...)` if needed.
      return response.json();
```

- [ ] **Step 4: Verify no orphan references remain**

Run:
```bash
grep -nE "EnqueueImportInvoicesRequest|EnqueueImportInvoicesResponse|BackgroundJobInfo|ImportResultDto" frontend/src/api/hooks/useAsyncInvoiceImport.ts
```

Expected output: only the lines from the `import type` block and the four annotation sites updated in Step 2 — *no* `export interface` declarations and no naked `BackgroundJobInfo`, `EnqueueImportInvoicesRequest`, `EnqueueImportInvoicesResponse`, or `ImportResultDto` (the bare names, without the leading `I`). If any remain, fix before proceeding.

- [ ] **Step 5: Type-check this file in isolation**

Run:
```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -E "useAsyncInvoiceImport\.ts|useAsyncInvoiceImport\.test\.ts" || echo "no errors in scope"
```

Expected: at this point the *test* file `useAsyncInvoiceImport.test.ts` will fail to compile (it still imports the deleted `BackgroundJobInfo`). The *hook* file itself should compile cleanly. We fix the test in Task 2.

- [ ] **Step 6: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-invoices-frontend-hooks-re-d
git add frontend/src/api/hooks/useAsyncInvoiceImport.ts
git commit -m "refactor(invoices): import async-import hook types from generated client"
```

---

## Task 2: Repair `useAsyncInvoiceImport.test.ts`

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts`

- [ ] **Step 1: Replace the imports**

Open `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts`. Replace lines 1–8 with:

```ts
import { renderHook, waitFor } from '@testing-library/react';
import {
    useInvoiceImportJobStatus,
    useRunningInvoiceImportJobs
} from '../useAsyncInvoiceImport';
import type { IBackgroundJobInfo } from '../../generated/api-client';
import { getAuthenticatedApiClient } from '../../client';
import { createMockApiClient, mockAuthenticatedApiClient, createQueryClientWrapper, setupFakeTimers } from '../../testUtils';
```

Note: `getAuthenticatedApiClient` is left in the imports even if unused locally — it matches the existing file shape (the `jest.mock('../../client')` below targets that module). Do not remove it unless lint says it is unused.

- [ ] **Step 2: Rename the three type annotations**

Find each `BackgroundJobInfo` annotation in the test bodies (currently lines ~32, ~92, ~155) and rename to `IBackgroundJobInfo`. Use `replace_all` on the file via Edit:

- `mockJobStatus: BackgroundJobInfo` → `mockJobStatus: IBackgroundJobInfo`
- `mockJobs: BackgroundJobInfo[]` → `mockJobs: IBackgroundJobInfo[]`

- [ ] **Step 3: Convert string `createdAt` to a `Date` value**

`IBackgroundJobInfo.createdAt` is typed `Date | undefined`. The mock at line ~36 currently has:

```ts
createdAt: '2024-01-01T10:00:00Z',
```

Replace with:

```ts
createdAt: new Date('2024-01-01T10:00:00Z'),
```

This matches the static type honestly (per arch-review Risks row 5).

- [ ] **Step 4: Run this test file**

Run:
```bash
cd frontend && npx jest src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts
```

Expected: all 7 tests pass. If TypeScript compile fails inside the test, the most common cause is a remaining `BackgroundJobInfo` literal — re-run the grep:
```bash
grep -n "BackgroundJobInfo" frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts
```
Every match should now be `IBackgroundJobInfo`.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts
git commit -m "refactor(invoices): align async-import hook tests with generated types"
```

---

## Task 3: Refactor `useIssuedInvoices.ts`

**Files:**
- Modify: `frontend/src/api/hooks/useIssuedInvoices.ts`

**Key decisions carried into the change:**
- `IssuedInvoicesFilters` is **kept** as a local hook-input type under its current name (per spec FR-3 and arch-review Decision 2).
- `IssuedInvoiceItemDto`, `IssuedInvoiceSyncHistoryDto`, `IssuedInvoiceDto`, `IssuedInvoiceDetailDto`, `IssuedInvoicesListResponse`, `IssuedInvoiceDetailResponse` are all **deleted**.
- Only types the file itself uses are imported. `IIssuedInvoiceDto` is **not** re-imported here — the hook returns `IGetIssuedInvoicesListResponse` and `IGetIssuedInvoiceDetailResponse`, which already reference the row/detail shapes by composition.

- [ ] **Step 1: Replace the local interfaces with a generated `import type`**

Open `frontend/src/api/hooks/useIssuedInvoices.ts`. Replace lines 1–74 (the existing imports plus the seven local interface declarations) with:

```ts
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import type {
  IGetIssuedInvoicesListResponse,
  IGetIssuedInvoiceDetailResponse,
} from '../generated/api-client';

export interface IssuedInvoicesFilters {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  invoiceId?: string;
  customerName?: string;
  invoiceDateFrom?: string;
  invoiceDateTo?: string;
  isSynced?: boolean;
  showOnlyUnsynced?: boolean;
  showOnlyWithErrors?: boolean;
}
```

- [ ] **Step 2: Update return-type annotations**

In `useIssuedInvoicesList` (the `queryFn` declared right after the `IssuedInvoicesFilters` block above), change:
```ts
queryFn: async (): Promise<IssuedInvoicesListResponse> => {
```
to:
```ts
queryFn: async (): Promise<IGetIssuedInvoicesListResponse> => {
```

In `useIssuedInvoiceDetail`, change:
```ts
queryFn: async (): Promise<IssuedInvoiceDetailResponse> => {
```
to:
```ts
queryFn: async (): Promise<IGetIssuedInvoiceDetailResponse> => {
```

- [ ] **Step 3: Add the FR-6 wire-format comments**

Above each of the two `return response.json();` statements in this file, insert:

```ts
      // Wire format: response is the I* interface variant (plain JSON). Date-typed fields
      // (invoiceDate, lastSyncTime, syncTime, …) arrive as ISO strings; parse with
      // `new Date(...)` if needed.
      return response.json();
```

- [ ] **Step 4: Verify no orphan declarations remain**

Run:
```bash
grep -nE "^export interface (IssuedInvoiceDto|IssuedInvoiceDetailDto|IssuedInvoiceItemDto|IssuedInvoiceSyncHistoryDto|IssuedInvoicesListResponse|IssuedInvoiceDetailResponse)\b" frontend/src/api/hooks/useIssuedInvoices.ts
```
Expected: no matches.

Run:
```bash
grep -nE "\\b(IssuedInvoiceDto|IssuedInvoiceDetailDto|IssuedInvoiceItemDto|IssuedInvoiceSyncHistoryDto|IssuedInvoicesListResponse|IssuedInvoiceDetailResponse)\\b" frontend/src/api/hooks/useIssuedInvoices.ts
```
Expected: no matches anywhere in the file (all references should have been replaced or removed).

Run:
```bash
grep -n "export interface IssuedInvoicesFilters" frontend/src/api/hooks/useIssuedInvoices.ts
```
Expected: exactly one match — `IssuedInvoicesFilters` is the only retained local interface.

- [ ] **Step 5: Type-check the hooks themselves (consumers will still fail)**

Run:
```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep "useIssuedInvoices.ts" || echo "no errors in useIssuedInvoices.ts itself"
```
Expected: the hook file itself compiles. Errors will move to consumer files (modal, page) — those are repaired in Tasks 4 and 5.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useIssuedInvoices.ts
git commit -m "refactor(invoices): import issued-invoice hook types from generated client"
```

---

## Task 4: Repair `IssuedInvoiceDetailModal.tsx`

**Files:**
- Modify: `frontend/src/components/customer/IssuedInvoiceDetailModal.tsx`

**Scope of repairs (all driven by the generated contract):**
1. Remove the customer email / phone / address rows (the generated `IIssuedInvoiceDetailDto` does not expose these fields).
2. Remove the "Položky faktury" (items) table — generated contract has no `items`.
3. Remove `sync.error.code` and `sync.error.field` render blocks — generated `IIssuedInvoiceErrorDto` only has `errorType?: string` and `message?: string`.
4. Convert the `sync.error.errorType === 0 | 1 | 2` numeric branch to the string-enum branch (`'General' | 'InvoicePaired' | 'ProductNotFound'`).
5. Convert the `data.invoice.errorType === '0' | '1' | '2'` numeric-string branch to the string-enum branch.
6. Remove the unused `Mail`, `Phone`, `MapPin` lucide-react icon imports.

- [ ] **Step 1: Trim the icon imports**

In `frontend/src/components/customer/IssuedInvoiceDetailModal.tsx`, change line 2 from:
```tsx
import { X, AlertCircle, CheckCircle, Clock, FileText, User, Mail, Phone, MapPin, Download, Loader2, ChevronDown, ChevronUp } from "lucide-react";
```
to:
```tsx
import { X, AlertCircle, CheckCircle, Clock, FileText, User, Download, Loader2, ChevronDown, ChevronUp } from "lucide-react";
```

- [ ] **Step 2: Remove the customer email / phone / address rows**

Delete lines 223–251 (the three `{data.invoice.customerEmail && (...)}`, `{data.invoice.customerPhone && (...)}`, `{data.invoice.customerAddress && (...)}` blocks — they begin with `{data.invoice.customerEmail && (` and end with the matching `)}` of the address block). The customer-information `<div className="space-y-3">` should retain only the `customerName` block above it.

After this deletion, the file should still have, in the customer-info section:

```tsx
              {/* Customer information */}
              <div className="space-y-4">
                <h3 className="text-lg font-medium text-gray-900 border-b border-gray-200 pb-2">
                  Informace o zákazníkovi
                </h3>

                <div className="space-y-3">
                  {data.invoice.customerName && (
                    <div className="flex items-center">
                      <User className="h-4 w-4 text-gray-400 mr-2 flex-shrink-0" />
                      <div>
                        <label className="text-sm font-medium text-gray-500">Jméno</label>
                        <p className="text-sm text-gray-900">{data.invoice.customerName}</p>
                      </div>
                    </div>
                  )}
                </div>
              </div>
```

- [ ] **Step 3: Remove the items table**

Delete the entire items block (was lines 255–298), starting with the comment `{/* Items */}` and ending with the closing `)}` of `{data.invoice.items && data.invoice.items.length > 0 && (...)}`. After deletion, the `Error details` block (`{data.invoice.errorType && data.invoice.errorMessage && (...)}`) should immediately follow the customer-information closing `</div>`.

- [ ] **Step 4: Remove `sync.error.code` and `sync.error.field` blocks**

Inside the expandable per-sync error panel, delete the two blocks at (previously) lines 400–411:

```tsx
                                        {sync.error.code && (
                                          <div>
                                            <span className="font-medium text-red-700">Kód chyby:</span>
                                            <p className="text-red-600 font-mono text-xs">{sync.error.code}</p>
                                          </div>
                                        )}
                                        {sync.error.field && (
                                          <div>
                                            <span className="font-medium text-red-700">Problematické pole:</span>
                                            <p className="text-red-600 font-mono text-xs">{sync.error.field}</p>
                                          </div>
                                        )}
```

The `Zpráva` (message) block before them and the `Typ chyby` block after them are retained — but the `Typ chyby` block needs the rewrite in Step 5.

- [ ] **Step 5: Repair the `sync.error.errorType` numeric branch**

`IIssuedInvoiceErrorDto.errorType` is `string | undefined`. The current code (was lines 412–420) compares to numeric literals `0`, `1`, `2`. Replace that block with the string-enum branch. The block currently reads:

```tsx
                                        <div>
                                          <span className="font-medium text-red-700">Typ chyby:</span>
                                          <p className="text-red-600 text-xs">
                                            {sync.error.errorType === 0 ? 'Obecná chyba' :
                                             sync.error.errorType === 1 ? 'Faktura již spárována' :
                                             sync.error.errorType === 2 ? 'Produkt nenalezen' :
                                             `Neznámý typ (${sync.error.errorType})`}
                                          </p>
                                        </div>
```

Replace with:

```tsx
                                        <div>
                                          <span className="font-medium text-red-700">Typ chyby:</span>
                                          <p className="text-red-600 text-xs">
                                            {sync.error.errorType === 'General' ? 'Obecná chyba' :
                                             sync.error.errorType === 'InvoicePaired' ? 'Faktura již spárována' :
                                             sync.error.errorType === 'ProductNotFound' ? 'Produkt nenalezen' :
                                             sync.error.errorType ? `Neznámý typ (${sync.error.errorType})` : 'Neznámý typ'}
                                          </p>
                                        </div>
```

The extra `sync.error.errorType ? … : 'Neznámý typ'` ternary keeps the visible string sane when `errorType` is `undefined` (the generated type allows that; the prior code's `${undefined}` would have rendered `"Neznámý typ (undefined)"`).

- [ ] **Step 6: Repair the `data.invoice.errorType` numeric-string branch**

In the "Aktuální stav synchronizace" panel (was lines 437–443), change:

```tsx
                                          <p className="text-orange-600 text-xs">
                                            {data.invoice.errorType === '0' ? 'Obecná chyba' :
                                             data.invoice.errorType === '1' ? 'Faktura již spárována' :
                                             data.invoice.errorType === '2' ? 'Produkt nenalezen' :
                                             `Neznámý typ (${data.invoice.errorType})`}
                                          </p>
```

to:

```tsx
                                          <p className="text-orange-600 text-xs">
                                            {data.invoice.errorType === 'General' ? 'Obecná chyba' :
                                             data.invoice.errorType === 'InvoicePaired' ? 'Faktura již spárována' :
                                             data.invoice.errorType === 'ProductNotFound' ? 'Produkt nenalezen' :
                                             data.invoice.errorType ? `Neznámý typ (${data.invoice.errorType})` : 'Neznámý typ'}
                                          </p>
```

- [ ] **Step 7: Verify all targeted reads are gone**

Run:
```bash
grep -nE "\\b(customerEmail|customerPhone|customerAddress)\\b|invoice\\.items|sync\\.error\\.(code|field)" frontend/src/components/customer/IssuedInvoiceDetailModal.tsx
```

Expected: no matches.

Run:
```bash
grep -nE "errorType === ['\"]?[012]['\"]?" frontend/src/components/customer/IssuedInvoiceDetailModal.tsx
```

Expected: no matches.

- [ ] **Step 8: Type-check and lint just this file**

Run:
```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep "IssuedInvoiceDetailModal.tsx" || echo "no errors in IssuedInvoiceDetailModal.tsx"
```
Expected: no errors.

Run:
```bash
cd frontend && npx eslint src/components/customer/IssuedInvoiceDetailModal.tsx
```
Expected: no errors.

- [ ] **Step 9: Run the modal tests**

Run:
```bash
cd frontend && npx jest src/components/customer/__tests__/IssuedInvoiceDetailModal.test.tsx
```

Expected: all 7 tests pass. The existing tests mock `data.invoice` via `(useIssuedInvoicesHooks as any).useIssuedInvoiceDetail = …` and the mock objects do not include `items`/`customerEmail`/`customerPhone`/`customerAddress`, so removal does not break them. If a test fails, do *not* re-add the deleted UI — fix the test mock instead and note the change in the commit body.

- [ ] **Step 10: Commit**

```bash
git add frontend/src/components/customer/IssuedInvoiceDetailModal.tsx
git commit -m "refactor(invoices): align detail modal with generated contract" -m "Removes UI for customerEmail, customerPhone, customerAddress, items, sync.error.code, sync.error.field — fields not present on generated IIssuedInvoiceDetailDto / IIssuedInvoiceErrorDto. Repairs errorType comparisons to IssuedInvoiceErrorType string enum. Any backend OpenAPI gap is followed up separately."
```

---

## Task 5: Repair `IssuedInvoicesPage.tsx`

**Files:**
- Modify: `frontend/src/pages/customer/IssuedInvoicesPage.tsx`

**Scope:** the page only breaks at one place — the `runningJobs.map(job => job.id).filter(Boolean)` expression. `IBackgroundJobInfo.id` is `string | undefined`, so `.filter(Boolean)` leaves the inferred type as `(string | undefined)[]`, which is rejected by `setActiveJobIds: Dispatch<SetStateAction<string[]>>`.

- [ ] **Step 1: Add the typed predicate**

Open `frontend/src/pages/customer/IssuedInvoicesPage.tsx`. Line 199 currently reads:

```tsx
      const jobIds = runningJobs.map(job => job.id).filter(Boolean);
```

Replace with:

```tsx
      const jobIds = runningJobs
        .map(job => job.id)
        .filter((id): id is string => Boolean(id));
```

That single-site narrowing satisfies `setActiveJobIds(string[])` without touching the hook return type.

- [ ] **Step 2: Confirm no other consumer breaks**

Run:
```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep "IssuedInvoicesPage.tsx" || echo "no errors in IssuedInvoicesPage.tsx"
```
Expected: no errors.

Run:
```bash
cd frontend && npx eslint src/pages/customer/IssuedInvoicesPage.tsx
```
Expected: no new errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/customer/IssuedInvoicesPage.tsx
git commit -m "fix(invoices): narrow runningJobs id list to string[] for setActiveJobIds"
```

---

## Task 6: Verify the two read-only consumer files compile

**Files:**
- Verify (no edit expected): `frontend/src/components/invoices/InvoiceImportJobTracker.tsx`
- Verify (no edit expected): `frontend/src/components/invoices/InvoiceImportRunningIndicator.tsx`

These files only destructure `jobStatus?.state`, `jobStatus?.jobName`, `jobStatus?.startedAt`, and `runningJobs.length`. The `startedAt` value is fed to `new Date(jobStatus.startedAt).toLocaleTimeString()`, which accepts `Date` input as well as the runtime ISO-string reality (`new Date(dateInstance)` returns a copy). No code change should be required.

- [ ] **Step 1: Type-check the indicator and tracker**

Run:
```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -E "InvoiceImportJobTracker\.tsx|InvoiceImportRunningIndicator\.tsx" || echo "no errors in either tracker/indicator"
```

Expected: no errors. If there are errors, investigate before assuming this task is a no-op; the most likely cause would be a passthrough type that has changed shape. Repair surgically and add a step to the commit list.

- [ ] **Step 2: Run their tests**

Run:
```bash
cd frontend && npx jest src/components/invoices/__tests__/InvoiceImportJobTracker.test.tsx
```

Expected: all 7 tests pass. Mocks in this test use plain JS objects (`{ id, state, jobName }`) and don't statically type against `IBackgroundJobInfo`, so they don't drift.

- [ ] **Step 3: Smoke-check the `IssuedInvoicesPage` consumer regression-style**

There is no dedicated unit test for `IssuedInvoicesPage.tsx`; the build + lint + manual confirmation that `setActiveJobIds(jobIds)` still compiles is the validation. No commit yet — this task is a checkpoint, not a code change.

---

## Task 7: Full validation

**Files:** none — verification only.

- [ ] **Step 1: Full frontend build**

Run:
```bash
cd frontend && npm run build
```

Expected: build succeeds with zero TypeScript errors. If any error appears in a file outside the modified set, stop and investigate — the diff may have widened beyond the surgical scope.

- [ ] **Step 2: Full lint**

Run:
```bash
cd frontend && npm run lint
```

Expected: no new errors. Count warnings against the Task 0 baseline if needed.

- [ ] **Step 3: Targeted test suite — all impacted files**

Run:
```bash
cd frontend && npx jest --testPathPattern="(useAsyncInvoiceImport|IssuedInvoiceDetailModal|InvoiceImportJobTracker|useIssuedInvoices)"
```

Expected: every file passes.

- [ ] **Step 4: Full frontend test suite (regression net)**

Run:
```bash
cd frontend && npm test -- --watchAll=false
```

Expected: full Jest suite passes. The refactor is type-only, so unrelated tests must remain green; any failure indicates an unintended consumer was missed.

- [ ] **Step 5: Confirm grep-level acceptance criteria from the spec**

Run:
```bash
grep -nE "^export interface " frontend/src/api/hooks/useIssuedInvoices.ts frontend/src/api/hooks/useAsyncInvoiceImport.ts
```

Expected: exactly one line — `frontend/src/api/hooks/useIssuedInvoices.ts:…:export interface IssuedInvoicesFilters {` and nothing else.

Run:
```bash
grep -rn "IssuedInvoiceItemDto\|ImportResultDto\|IssuedInvoiceSyncHistoryDto" frontend/src
```

Expected: no matches anywhere under `frontend/src`. (Both names are deleted; if anything still references them, fix it now.)

- [ ] **Step 6: Confirm no `as any` field laundering crept in**

Run:
```bash
grep -nE "\\(.*as any\\)\\.(customerEmail|customerPhone|customerAddress|items)" frontend/src/components/customer/IssuedInvoiceDetailModal.tsx
```

Expected: no matches. (The intent is to remove these reads, not launder them; arch-review Decision 3 explicitly forbids the `as any` fallback.)

---

## Task 8: PR write-up

**Files:** none — communication only.

- [ ] **Step 1: Draft PR description**

The PR body must explicitly call out the user-visible UI shrinkage in the detail modal so a reviewer is not surprised:

```
Replace hand-written types in invoices hooks with generated client imports.

User-visible changes
- IssuedInvoiceDetailModal: removes the customer e-mail / phone / address rows
  and the invoice-items table. These fields are not part of the generated
  IIssuedInvoiceDetailDto contract; the previous reads were silent
  type-drift through hand-written interfaces.
- IssuedInvoiceDetailModal: per-sync error panel no longer renders "Kód chyby"
  or "Problematické pole" — the generated IIssuedInvoiceErrorDto only exposes
  `errorType` and `message`.
- IssuedInvoiceDetailModal: "Typ chyby" comparison repaired from numeric
  literals to the IssuedInvoiceErrorType string enum
  ('General' | 'InvoicePaired' | 'ProductNotFound').

No-op for users
- All polling intervals, query keys, cache settings, fetch URLs, mutation
  behavior, error handling, and modal callback ordering are byte-identical
  to before.

Follow-up
- If the backend genuinely returns customerEmail / customerPhone /
  customerAddress / items / sync.error.code / sync.error.field at runtime,
  that is an OpenAPI gap to file against the issued-invoices endpoint and
  resolve in NSwag regeneration. Out of scope for this PR.
```

- [ ] **Step 2: Push and open the PR per the standard workflow**

Use the `finishfeature` skill (or `git push -u origin <branch>` + `gh pr create --title … --body "$(cat <<'EOF' ... EOF)"`) — covered by global git-workflow rules; no inline command listing here.

---

## Self-Review Notes

- **Spec FR-1** (delete duplicate declarations): Tasks 1, 3 delete the entire set; Task 7 Step 5 verifies via grep.
- **Spec FR-2** (import generated types): Tasks 1, 3 add `import type` blocks. Note: `IIssuedInvoiceDto` is intentionally *not* re-imported in `useIssuedInvoices.ts` per arch-review amendment 3 — no consumer in scope imports it from the hook (confirmed earlier in the planning phase via grep of the five consumer files).
- **Spec FR-3** (local-only types): Task 3 keeps `IssuedInvoicesFilters`; Tasks 1 and 3 explicitly delete `ImportResultDto` and `IssuedInvoiceItemDto`.
- **Spec FR-4** (update consumers): Tasks 2 (test), 4 (modal), 5 (page); Task 6 verifies the no-op consumers.
- **Spec FR-5** (preserve runtime): Every task explicitly limits the diff to imports, type annotations, and the FR-6 comment. No fetch URL, header, query key, polling interval, or `useQuery` / `useMutation` option is touched.
- **Spec FR-6** (wire-format comment): Task 1 Step 3 adds three comments in `useAsyncInvoiceImport.ts`; Task 3 Step 3 adds two in `useIssuedInvoices.ts`. Five comments total (per arch-review amendment 4).
- **Spec NFR-1/2** (no regressions, build/lint clean): Task 7 covers full validation.
- **Spec NFR-3** (consistency with `usePurchaseOrders.ts`): Both refactored hooks end up with `import type { ... } from "../generated/api-client"` + (in the issued-invoices case) one local input type, matching the established pattern.
- **Spec NFR-4** (surgical diff): The Files-modified list is fixed. Task 7 Step 6 catches any `as any` laundering, Task 7 Step 5 catches any orphan declaration, and the verify-only files in Task 6 are explicitly *not* edited.
- **Arch-review Decision 4** (`BackgroundJobInfo.id` narrowing): Task 5 implements the predicate narrowing at the single consumer.
- **Arch-review High-severity risks** (modal error reads, numeric vs string errorType): Task 4 covers all five enumerated sites.
- **Arch-review Spec amendments**: All five amendments are reflected — FR-4 enumerated sites (Task 4), FR-4 narrowing site (Task 5), FR-2 selective re-export (Task 3 Step 1), FR-6 per-hook comment count (Tasks 1/3 Step 3), NFR-4 permitting the enum-string repair (Task 4 Steps 5–6).

No placeholders. Every step lists the exact change, exact command, and exact expected output.
