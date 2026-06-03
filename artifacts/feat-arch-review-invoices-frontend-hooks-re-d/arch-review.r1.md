I've explored the spec, the two hook files, the generated client, and the key consumers. The spec aligns with reality on most points, but I uncovered two material gaps it underweights: (1) the sync-error shape mismatch in the detail modal, and (2) a real TypeScript type-narrowing problem with `BackgroundJobInfo.id` due to optionality. The review below codifies both.

```markdown
# Architecture Review: Replace hand-written Invoices hook types with generated client imports

## Skip Design: true

This is a pure type-only refactor. No new UI/UX components, screens, or visual decisions. The only user-visible effects are *removals* (customer email / phone / address / items sections in the detail modal, plus parts of the per-sync error panel) — driven by alignment with the canonical generated contract, not by a design choice.

## Architectural Fit Assessment

**The proposed direction is correct and well-precedented.** `frontend/src/api/hooks/usePurchaseOrders.ts` is already the in-repo blueprint: it imports response/request types directly from `../generated/api-client`, keeps a local hook-input interface for query-string parameter shaping, and otherwise uses raw `fetch` against `${baseUrl}{relativeUrl}`. Replicating that pattern in `useIssuedInvoices.ts` and `useAsyncInvoiceImport.ts` closes a real source-of-truth divergence (NSwag rewrites `api-client.ts` on every build; hand-written interfaces silently drift).

**Integration points** (verified by reading source):
- `frontend/src/api/generated/api-client.ts` — already exposes every needed `I*` interface (lines 19541–20167 for the invoice surface). No backend or codegen change required.
- `frontend/src/api/hooks/usePurchaseOrders.ts` — establishes the import pattern, query-key collocation, and `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch` convention.
- Five consumer files import types from the two hooks (verified by `grep`). All are in scope per FR-4.

**Caveats the spec underweights** — addressed in *Risks* below:
1. The generated `IIssuedInvoiceErrorDto` (sync history `error` field) has only `{ errorType?: string; message?: string }`. The hand-written hook type had `{ message, code?, field?, errorType: number }` and the modal reads all four. The spec mentions `items` / `customerEmail` / etc. as contract gaps but does not explicitly enumerate the `sync.error.code` / `sync.error.field` / numeric `errorType` reads in `IssuedInvoiceDetailModal.tsx`.
2. `IBackgroundJobInfo.id` is `string | undefined`. `IssuedInvoicesPage.tsx:199` does `runningJobs.map(job => job.id).filter(Boolean)` and assigns to `string[]`. After the swap the compiler will reject the assignment without an explicit narrowing predicate.
3. `IIssuedInvoiceDto.errorType` is the enum `IssuedInvoiceErrorType | undefined` (`'General' | 'InvoicePaired' | 'ProductNotFound'`). `IssuedInvoiceDetailModal.tsx:439–442` compares against `'0' | '1' | '2'` string literals — these comparisons will compile-error (`Type '"0"' has no overlap with 'IssuedInvoiceErrorType | undefined'`).

These are not blockers but **they widen the consumer-fix scope beyond what FR-4 lists by example.** The implementation MUST address them under the same surgical-diff rules.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────┐
│  Source of truth: NSwag-generated api-client.ts                  │
│                                                                  │
│   IIssuedInvoiceDto · IIssuedInvoiceDetailDto                    │
│   IIssuedInvoiceSyncDataDto · IIssuedInvoiceErrorDto             │
│   IGetIssuedInvoicesListResponse · IGetIssuedInvoiceDetailResponse│
│   IEnqueueImportInvoicesRequest · IEnqueueImportInvoicesResponse │
│   IBackgroundJobInfo · IIssuedInvoiceSourceQuery                 │
│   IssuedInvoiceErrorType (enum)                                  │
└──────────────────────────────────────────────────────────────────┘
            ▲ import type                       ▲ import type
            │                                   │
┌───────────┴─────────────────┐  ┌──────────────┴───────────────────┐
│ useIssuedInvoices.ts        │  │ useAsyncInvoiceImport.ts          │
│  · local: IssuedInvoicesFilters│  · local: (none)                  │
│  · raw fetch + response.json│  │  · raw fetch + response.json     │
└─────────────────────────────┘  └──────────────────────────────────┘
            ▲                                   ▲
            │                                   │
   ┌────────┴─────────┐                ┌────────┴──────────┐
   │ IssuedInvoicesPage│                │InvoiceImportJob*  │
   │ IssuedInvoice…Modal│                │InvoiceImport…Indicator│
   └───────────────────┘                └───────────────────┘
            ▲                                   ▲
            └──── tests (mocks updated) ────────┘
```

### Key Design Decisions

#### Decision 1: Use `import type { I* }` (data-only interface variants), not class variants

**Options considered:**
- Import class types (e.g. `IssuedInvoiceDto`) — implies runtime instances with `fromJS` / `toJSON` methods.
- Import `I*` interface variants (e.g. `IIssuedInvoiceDto`) — describe pure JSON shape.

**Chosen approach:** `I*` interfaces, via `import type`.

**Rationale:** The hooks call `fetch` directly and return `response.json()` unmodified. At runtime, values are plain objects, not class instances. Importing the class type would falsely advertise methods that the runtime values do not have, and would mislead future maintainers into calling `value.toJSON()` or expecting `Date` instance methods. `import type` also keeps the build output free of phantom value imports. (`usePurchaseOrders.ts` imports class names, but it does so as named imports of *response* types it returns from the hook — the actual runtime values are still plain objects from `response.json()`. The `I*` form is strictly more honest.)

#### Decision 2: Retain `IssuedInvoicesFilters` as a local hook-input type, do not rename

**Options considered:**
- Replace with positional generated parameters (kill the local type entirely).
- Keep as local interface under the current name.
- Rename to `*Request` to align with `GetPurchaseOrdersRequest`.

**Chosen approach:** Keep local, keep the current name.

**Rationale:** The hook constructs a query string from these fields; there is no generated request DTO with this shape (the generated client uses positional arguments on `issuedInvoices_GetList()`). Renaming would touch every consumer call site and violates NFR-4 (surgical diff). Naming consistency with `GetPurchaseOrdersRequest` is a separate concern that can be addressed when patterns are unified holistically.

#### Decision 3: Delete contract-orphan reads from consumers; do not silently `as any` them

**Options considered:**
- `as any` the unknown fields and leave the UI as-is to minimize visible change.
- Delete reads (and surrounding UI) for fields not in the generated contract.

**Chosen approach:** Delete the reads and the surrounding UI; flag every user-visible removal in the PR description.

**Rationale:** `as any` would preserve the silent type-drift the refactor is meant to eliminate. The generated client is the contract; if the backend really does emit `customerEmail` / `items`, the fix is to update the OpenAPI document (out of scope per spec) — not to launder the gap through `any`. The cost is real (entire sections of the detail modal disappear) but it is the right cost.

#### Decision 4: Cast `BackgroundJobInfo.id` reads at the single narrowing site, do not change the hook signature

**Options considered:**
- Wrap the hook to re-narrow `id` to `string`.
- Cast / filter at the single consumer that needs it (`IssuedInvoicesPage.tsx:199`).

**Chosen approach:** Narrow at the consumer using an explicit predicate, e.g. `runningJobs.map(j => j.id).filter((id): id is string => Boolean(id))`.

**Rationale:** The hook should return the contract shape verbatim. The unique consumer that needs a `string[]` already has the right boundary to enforce it.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits are confined to:

```
frontend/src/api/hooks/
  useIssuedInvoices.ts                 (delete interfaces 4–74 except IssuedInvoicesFilters; add `import type`)
  useAsyncInvoiceImport.ts             (delete interfaces 5–31; add `import type`)
  __tests__/useAsyncInvoiceImport.test.ts (mocks: cast or wrap createdAt as Date)

frontend/src/pages/customer/
  IssuedInvoicesPage.tsx               (narrow `runningJobs.map(j => j.id)` to string[])

frontend/src/components/customer/
  IssuedInvoiceDetailModal.tsx         (remove customerEmail/Phone/Address/items UI;
                                        fix errorType comparisons; trim sync.error.code/field reads;
                                        adapt numeric error-type table to string enum)
  __tests__/IssuedInvoiceDetailModal.test.tsx (align mock shape with IIssuedInvoiceDetailDto)

frontend/src/components/invoices/
  InvoiceImportJobTracker.tsx          (no change expected; verify build)
  InvoiceImportRunningIndicator.tsx    (no change expected; verify build)
  __tests__/InvoiceImportJobTracker.test.tsx (verify mock shape vs IBackgroundJobInfo)
```

### Interfaces and Contracts

**`useIssuedInvoices.ts` — imports header:**
```ts
import type {
  IIssuedInvoiceDto,                  // (unused directly; kept for export-compat if any test imports it)
  IGetIssuedInvoicesListResponse,
  IGetIssuedInvoiceDetailResponse,
  IIssuedInvoiceSyncDataDto,          // re-exported only if a consumer imports it
} from "../generated/api-client";
```
Only re-export what consumers actually import — verify via `grep` and drop unused re-exports.

**`useAsyncInvoiceImport.ts` — imports header:**
```ts
import type {
  IEnqueueImportInvoicesRequest,
  IEnqueueImportInvoicesResponse,
  IBackgroundJobInfo,
} from "../generated/api-client";
```

**Public hook signatures (post-refactor):**
- `useIssuedInvoicesList(filters: IssuedInvoicesFilters): UseQueryResult<IGetIssuedInvoicesListResponse>`
- `useIssuedInvoiceDetail(invoiceId: string): UseQueryResult<IGetIssuedInvoiceDetailResponse>`
- `useEnqueueInvoiceImport(): UseMutationResult<IEnqueueImportInvoicesResponse, Error, IEnqueueImportInvoicesRequest>`
- `useInvoiceImportJobStatus(jobId?: string): UseQueryResult<IBackgroundJobInfo | null>`
- `useRunningInvoiceImportJobs(): UseQueryResult<IBackgroundJobInfo[]>`

**Local-only retained:**
- `IssuedInvoicesFilters` (current shape, current name, current location)

**Deleted:**
- `IssuedInvoiceDto`, `IssuedInvoicesListResponse`, `IssuedInvoiceDetailDto`, `IssuedInvoiceItemDto`, `IssuedInvoiceSyncHistoryDto`, `IssuedInvoiceDetailResponse`
- `EnqueueImportInvoicesRequest`, `EnqueueImportInvoicesResponse`, `BackgroundJobInfo`, `ImportResultDto`

### Data Flow

```
Component                     Hook                          Network
─────────                     ────                          ───────
IssuedInvoicesPage
  └─ filters: IssuedInvoicesFilters ──► useIssuedInvoicesList
                                          └─ fetch GET /api/IssuedInvoices?…
                                          ◄─ response.json() : plain object ≈ IGetIssuedInvoicesListResponse
                                            (Date-typed fields are ISO strings at runtime)
  ◄─ data.items[]
  ◄─ data.totalCount

IssuedInvoiceDetailModal
  └─ invoiceId ────────────────────────► useIssuedInvoiceDetail
                                          └─ fetch GET /api/IssuedInvoices/{id}
                                          ◄─ response.json() : plain object ≈ IGetIssuedInvoiceDetailResponse
  ◄─ data.invoice: IIssuedInvoiceDetailDto
       (no customerEmail / customerPhone / customerAddress / items — UI sections removed)

  └─ handleReimport ───────────────────► useEnqueueInvoiceImport (mutation)
                                          └─ fetch POST /api/invoices/import/enqueue-async
                                          ◄─ { jobId? }

IssuedInvoicesPage / InvoiceImport*
  └─                                  ──► useRunningInvoiceImportJobs / useInvoiceImportJobStatus
                                          └─ fetch GET .../running-jobs | .../job-status/{id}
                                          ◄─ IBackgroundJobInfo[] | IBackgroundJobInfo | null
                                            (id?: string — narrow at consumer)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Runtime values are ISO strings but typed as `Date` (NSwag generates `Date` for date fields; raw `fetch` doesn't deserialize). Consumers passing them to APIs that strictly require `Date` could compile but fail at runtime. | Medium | Add the one-line comment per FR-6 above each `return response.json()`. Where the type checker complains, parse explicitly with `new Date(value as unknown as string)` at the call site. Do not introduce a project-wide deserializer in this PR. |
| **Detail modal contract gap is broader than the spec lists.** `IIssuedInvoiceErrorDto` lacks `code` and `field`, and types `errorType` as `string` not number — but `IssuedInvoiceDetailModal.tsx:400–418` reads `sync.error.code`, `sync.error.field`, and `sync.error.errorType === 0/1/2`. | High | Treat the same way as `items` / `customerEmail`: delete the reads, trim the per-sync error-detail panel to what the generated contract guarantees (`message`, `errorType` as string union). Document the UI shrink in the PR description. File a follow-up if backend genuinely returns the missing fields. |
| `invoice.errorType` comparisons against `'0' | '1' | '2'` in the modal will not compile against `IssuedInvoiceErrorType` enum. | High | Replace with `=== 'General' / 'InvoicePaired' / 'ProductNotFound'`. This is a real bug being uncovered (numeric vs string-enum mismatch); fixing it is in scope as a consumer repair under FR-4. |
| `IBackgroundJobInfo.id` is optional, but `setActiveJobIds` expects `string[]`. | Medium | Replace `.filter(Boolean)` with a typed predicate: `.filter((id): id is string => Boolean(id))`. Single-site fix. |
| Test fixtures assign string literals to fields now typed `Date`. E.g. `useAsyncInvoiceImport.test.ts:36` `createdAt: '2024-01-01T10:00:00Z'`. | Low | Wrap as `new Date('2024-01-01T10:00:00Z')` or cast the whole mock with `as unknown as IBackgroundJobInfo`. Prefer the former — it matches the static type honestly. |
| Silent regression of UI sections (customer email/phone/address rows, items table, sync error code/field detail). Real users may rely on these. | Medium | Loud PR description. Screenshot before/after. Solo-developer review can decide whether to keep the UI behind an `(invoice as any).customerEmail` cast as a short-term holdover or accept the removal. Do not split the difference silently. |
| Re-exports break test imports. The test file at `useAsyncInvoiceImport.test.ts:5` imports `BackgroundJobInfo` (the deleted local type). | Low | Update the test to `import type { IBackgroundJobInfo } from '../../generated/api-client'`. |
| Two generated list methods exist (`invoices_GetInvoicesList` and `issuedInvoices_GetList`), both returning `GetIssuedInvoicesListResponse`. Easy to confuse during code review. | Low | Note in PR: the hook calls `/api/IssuedInvoices`, which is the `issuedInvoices_GetList` endpoint. No method change in this PR. |

## Specification Amendments

The spec is high-quality. The following clarifications keep implementation surgical and complete:

1. **FR-4: expand the enumerated contract gaps.** Beyond `customerEmail` / `customerPhone` / `customerAddress` / `items`, the implementation must also remove or repair:
   - Reads of `sync.error.code` and `sync.error.field` in `IssuedInvoiceDetailModal.tsx` (lines ~400, ~406).
   - Numeric comparisons on `sync.error.errorType` (lines ~415–418) — adapt to the string-typed shape of `IIssuedInvoiceErrorDto.errorType`.
   - Numeric comparisons on `data.invoice.errorType === '0' / '1' / '2'` (lines ~439–442) — replace with `IssuedInvoiceErrorType` enum string comparisons.

2. **FR-4: add `IssuedInvoicesPage.tsx` `runningJobs` narrowing as a known repair site.** Replace `.filter(Boolean)` with `.filter((id): id is string => Boolean(id))` to keep `setActiveJobIds(string[])` type-safe.

3. **FR-2: explicitly state `IIssuedInvoiceDto` need not be re-exported** unless a consumer file imports it from the hook. The spec lists it in the mapping but the consumers I read all reach for the response shape; reduce re-exports to what `grep` proves is needed.

4. **FR-6: clarify the comment placement.** "Immediately above the `fetch`/`response.json()` return statement" is fine for `useIssuedInvoices.ts` (two locations) and `useAsyncInvoiceImport.ts` (three locations) — be explicit that the comment is repeated per hook, not per file.

5. **NFR-4: explicitly permit the four enum-string repairs in the detail modal as part of "minimum set of consumer files whose imports or field reads break."** Without this, a reviewer could read NFR-4 as forbidding the comparison-string fix as a "while I'm here" change.

## Prerequisites

None — no migrations, no infrastructure, no config. Verify before starting:

- `frontend/src/api/generated/api-client.ts` is current (`npm run generate-api` if in doubt). The interfaces listed in this review are visible at lines 19541–20167.
- `npm run build` and `npm run lint` currently pass on the branch base. (Baseline so failures attributable to this PR are obvious.)
- Existing tests in `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts`, `frontend/src/components/customer/__tests__/IssuedInvoiceDetailModal.test.tsx`, and `frontend/src/components/invoices/__tests__/InvoiceImportJobTracker.test.tsx` currently pass.
- Confirm with stakeholder (or note in PR for solo-developer review) that removing the customer email / phone / address / items UI sections — and the per-sync error-code/field detail — is the right call given the OpenAPI contract, or whether the upstream contract gap should be filed and held first.
```