# Specification: Replace hand-written Invoices hook types with generated client imports

## Summary
The Invoices module has two frontend hooks (`useIssuedInvoices.ts`, `useAsyncInvoiceImport.ts`) that hand-declare TypeScript interfaces for response shapes already produced by NSwag in `frontend/src/api/generated/api-client.ts`. This work eliminates that duplication by importing the generated types instead, matching the established pattern from `usePurchaseOrders.ts`. The refactor is type-only and must preserve runtime behavior of all hook consumers, including the use of raw `fetch` (which returns JSON-shaped objects, not class instances).

## Background
NSwag regenerates `api-client.ts` on every build from the backend's OpenAPI document. When the backend evolves (new fields, renames, type changes), the generated client is updated automatically; hand-written interfaces inside hooks are not. Today, both `useIssuedInvoices.ts` and `useAsyncInvoiceImport.ts` declare local TypeScript interfaces that overlap (and already diverge) from the generated equivalents:

- `useIssuedInvoices.ts` declares `IssuedInvoicesFilters`, `IssuedInvoiceDto`, `IssuedInvoicesListResponse`, `IssuedInvoiceDetailDto`, `IssuedInvoiceItemDto`, `IssuedInvoiceSyncHistoryDto`, `IssuedInvoiceDetailResponse`.
- `useAsyncInvoiceImport.ts` declares `EnqueueImportInvoicesRequest`, `EnqueueImportInvoicesResponse`, `BackgroundJobInfo`, `ImportResultDto`.

The generated client (`api-client.ts`) already exposes the canonical shapes:
- `IssuedInvoiceDto` / `IIssuedInvoiceDto`
- `IssuedInvoiceDetailDto` / `IIssuedInvoiceDetailDto`
- `IssuedInvoiceSyncDataDto` / `IIssuedInvoiceSyncDataDto`
- `IssuedInvoiceErrorDto` / `IIssuedInvoiceErrorDto`
- `IssuedInvoiceErrorType` (enum)
- `GetIssuedInvoicesListResponse` / `IGetIssuedInvoicesListResponse`
- `GetIssuedInvoiceDetailResponse` / `IGetIssuedInvoiceDetailResponse`
- `EnqueueImportInvoicesRequest` / `IEnqueueImportInvoicesRequest`
- `IssuedInvoiceSourceQuery` / `IIssuedInvoiceSourceQuery`
- `EnqueueImportInvoicesResponse` / `IEnqueueImportInvoicesResponse`
- `BackgroundJobInfo` / `IBackgroundJobInfo`

The existing reference implementation, `frontend/src/api/hooks/usePurchaseOrders.ts`, demonstrates the convention: import named types from `../generated/api-client` and never re-declare them locally.

The hooks in scope call `fetch` directly and return the parsed JSON response. Generated client interfaces (the `I*` variants) describe property names and types but make no claim about runtime parsing — most notably, `Date` properties are received as ISO strings over the wire and stay as strings unless explicitly parsed. This nuance affects which generated types are appropriate (see FR-2) and is the main behavioral risk during migration (see FR-6).

**Decisions carried into this revision** (previously open questions, now resolved):
- The generated client is treated as authoritative for the issued-invoice detail shape. Any consumer code that reads fields not exposed by `IIssuedInvoiceDetailDto` (e.g., `items`, `customerEmail`, `customerPhone`, `customerAddress`) is currently relying on a non-contract and will be cleaned up under FR-4. If the backend genuinely returns those fields at runtime, that is an upstream OpenAPI gap to be filed separately — it does not block this refactor.
- `IssuedInvoicesFilters` is **not** renamed. Keeping the existing name preserves a surgical diff; reviewers can revisit naming as a follow-up if it causes confusion at consumer sites.

## Functional Requirements

### FR-1: Remove duplicate interface declarations
All interface declarations in `useIssuedInvoices.ts` lines 4–74 and `useAsyncInvoiceImport.ts` lines 5–31 that have a semantic counterpart in the generated client must be deleted from the hook files.

**Acceptance criteria:**
- After the change, `useIssuedInvoices.ts` contains no `export interface` declarations whose name matches a type exported by `frontend/src/api/generated/api-client.ts`.
- After the change, `useAsyncInvoiceImport.ts` contains no `export interface` declarations whose name matches a type exported by `frontend/src/api/generated/api-client.ts`.
- `grep -n "export interface" frontend/src/api/hooks/useIssuedInvoices.ts frontend/src/api/hooks/useAsyncInvoiceImport.ts` returns only entries (if any) that have been explicitly retained per FR-3.

### FR-2: Import generated types from `../generated/api-client`
Each removed local type must be replaced by an import of its generated counterpart. Use `import type` for type-only imports (the hooks do not consume the runtime class methods like `fromJS`).

**Type mapping (generated client name on the right):**
| Hand-written name | Replacement |
|---|---|
| `IssuedInvoiceDto` | `IIssuedInvoiceDto` |
| `IssuedInvoicesListResponse` | `IGetIssuedInvoicesListResponse` |
| `IssuedInvoiceDetailDto` | `IIssuedInvoiceDetailDto` |
| `IssuedInvoiceSyncHistoryDto` | `IIssuedInvoiceSyncDataDto` |
| `IssuedInvoiceDetailResponse` | `IGetIssuedInvoiceDetailResponse` |
| `EnqueueImportInvoicesRequest` | `IEnqueueImportInvoicesRequest` |
| `EnqueueImportInvoicesResponse` | `IEnqueueImportInvoicesResponse` |
| `BackgroundJobInfo` | `IBackgroundJobInfo` |

Rationale for the `I*` interface variants: the hooks return the raw `response.json()` result, so the value is a plain object — not an instance of the generated class. The `I*` interfaces describe the same shape without implying class methods.

**Acceptance criteria:**
- Each hook file has an `import type { … } from "../generated/api-client";` line that includes every type listed in the mapping above that the file actually uses.
- All hook return types (`Promise<…>`) reference the generated names, not the removed local names.
- TypeScript compiles (`npm run build`) with no errors related to these hooks.

### FR-3: Decide on local-only types
Three hand-written types have no direct generated counterpart and must be addressed explicitly, not silently kept:

- **`IssuedInvoicesFilters`** — represents the hook's input parameters (query string fields). The generated client exposes these only as positional parameters on `issuedInvoices_GetList()`. **Decision:** retain `IssuedInvoicesFilters` as a local type inside `useIssuedInvoices.ts` under its current name. Renaming is out of scope.
- **`IssuedInvoiceItemDto`** — declared inside the hand-written `IssuedInvoiceDetailDto` but has no counterpart in `IIssuedInvoiceDetailDto`. The generated `IIssuedInvoiceDetailDto` exposes `syncHistory` only — no `items`, `customerEmail`, `customerPhone`, or `customerAddress`. **Decision:** delete `IssuedInvoiceItemDto` from the hook. Consumers reading those fields are updated under FR-4. The generated client is treated as authoritative for the detail shape; any contract gap is followed up separately and does not block this refactor.
- **`ImportResultDto`** — declared in `useAsyncInvoiceImport.ts` but is not used by any of the exported hooks in that file and has no generated counterpart. **Decision:** delete it.

**Acceptance criteria:**
- `IssuedInvoicesFilters` remains exported from `useIssuedInvoices.ts` under its current name.
- `IssuedInvoiceItemDto` and `ImportResultDto` are removed from their respective files.
- No other file in `frontend/src` imports `IssuedInvoiceItemDto` or `ImportResultDto` after the change (verified by `grep`).

### FR-4: Update consumers whose imports or field reads break
Removing or shrinking the hook-exported types may break (a) imports in consumer files and (b) consumer code that reads fields not present on the generated types. Every breakage must be repaired:

- **Broken imports** → import the equivalent generated type from `../generated/api-client` (or the retained local input type, e.g., `IssuedInvoicesFilters`).
- **Broken field reads** (e.g., `invoice.items`, `invoice.customerEmail`, `invoice.customerPhone`, `invoice.customerAddress`) → remove the read and the surrounding UI that depended on it. The generated client is authoritative; these fields are not part of the documented contract. If a consumer's user-visible behavior changes (e.g., a section of the modal disappears), call it out in the PR description so reviewers can confirm.

Known direct consumers (from `grep`):
- `frontend/src/pages/customer/IssuedInvoicesPage.tsx`
- `frontend/src/components/customer/IssuedInvoiceDetailModal.tsx`
- `frontend/src/components/invoices/InvoiceImportJobTracker.tsx`
- `frontend/src/components/invoices/InvoiceImportRunningIndicator.tsx`
- `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts`
- `frontend/src/components/customer/__tests__/IssuedInvoiceDetailModal.test.tsx`
- `frontend/src/components/invoices/__tests__/InvoiceImportJobTracker.test.tsx`

**Acceptance criteria:**
- Every consumer that imported a removed type from the hook file is updated to import the generated equivalent (or the retained local input type).
- Every consumer that read a field not present on the generated `I*` interfaces is updated (field read removed; surrounding UI cleaned up).
- `npm run build` and `npm run lint` pass.
- Existing unit tests for these hooks/components pass with mock objects updated to match the new (generated) type signatures.
- Any user-visible UI removals (e.g., a customer-email row in the detail modal) are explicitly noted in the PR description.

### FR-5: Preserve runtime behavior of `fetch` + `response.json()`
The hooks must continue to:
- Call `fetch` directly against `${baseUrl}{path}` (no migration to generated client methods is in scope).
- Return `response.json()` unchanged.
- Keep `useQuery` / `useMutation` configurations (query keys, `staleTime`, `gcTime`, `refetchInterval`, `enabled`, `onSuccess`) byte-identical.

**Acceptance criteria:**
- A line-level diff of each hook shows changes only inside the `import` block and inside type annotations. No changes to URL construction, fetch options, error handling, query key construction, or `useQuery` / `useMutation` options.
- Existing tests under `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts` pass.

### FR-6: Reconcile `Date`-vs-`string` mismatches
The generated `I*` interfaces type date-like fields as `Date | undefined` (e.g., `IIssuedInvoiceDto.invoiceDate`, `IBackgroundJobInfo.createdAt`). At runtime, however, `response.json()` deserializes these as ISO strings. Hand-written types previously typed them as `string`, so consumers may currently call `String.prototype` methods or pass them to `new Date(value)` directly.

After importing the generated interfaces, the static type will say `Date` but the runtime value will be a string. This will not produce compile errors in most cases (the values are already used via `.toLocaleDateString()`-style calls that exist on `Date`, or via constructs that accept either type), but it can mask real bugs.

**Acceptance criteria:**
- A one-line comment is added inside each of the two hook files, immediately above the `fetch`/`response.json()` return statement, stating that response payloads use the `I*` (data-only) interface variants and that date fields arrive as ISO strings even though typed as `Date`.
- Any consumer that currently casts `lastSyncTime` / `invoiceDate` / `createdAt` to `Date` via `new Date(value)` continues to work. No new runtime conversion is introduced unless an existing call site is already broken.
- If the type checker complains about an existing call site (e.g., assigning the string-typed value to a `Date`-typed prop), the call site is updated to explicitly parse with `new Date(value)` and a short comment notes the wire-format reality.

## Non-Functional Requirements

### NFR-1: No runtime regressions
The change is type-only and must not alter network calls, error handling, polling intervals, query keys, cache behavior, or React Query semantics. All existing unit tests and Playwright E2E coverage for the Invoices module must pass.

### NFR-2: Build and lint cleanliness
- `cd frontend && npm run build` succeeds with no new TypeScript errors.
- `cd frontend && npm run lint` succeeds with no new lint warnings introduced by this change.

### NFR-3: Consistency with established pattern
The end state of `useIssuedInvoices.ts` and `useAsyncInvoiceImport.ts` matches the convention demonstrated by `frontend/src/api/hooks/usePurchaseOrders.ts`: types come from `../generated/api-client`; only hook-local input/parameter types remain as local declarations.

### NFR-4: Surgical diff
The PR diff is limited to:
- The two hook files (`useIssuedInvoices.ts`, `useAsyncInvoiceImport.ts`).
- The minimum set of consumer files whose imports or field reads break as a direct result of FR-1, FR-3, and FR-4.
- Test files whose mock objects no longer satisfy the new (generated) type signatures.

No unrelated formatting, comments, refactors, or "while I'm here" changes are included.

## Data Model
No data model changes. The backend contracts are untouched. The generated client already reflects the current backend shape; this work merely consumes those shapes on the frontend.

Key generated types involved (defined in `frontend/src/api/generated/api-client.ts`):
- `IIssuedInvoiceDto` — list-row shape for issued invoices.
- `IIssuedInvoiceDetailDto extends IIssuedInvoiceDto` — adds `syncHistory`, `concurrencyStamp`, `lastModificationTime`, `creatorId`, `lastModifierId`.
- `IIssuedInvoiceSyncDataDto` — sync history entries (replaces hand-written `IssuedInvoiceSyncHistoryDto`).
- `IIssuedInvoiceErrorDto` — sync error payload.
- `IssuedInvoiceErrorType` (enum) — error categories (`General`, `InvoicePaired`, `ProductNotFound`).
- `IGetIssuedInvoicesListResponse extends IBaseResponse` — paginated list envelope.
- `IGetIssuedInvoiceDetailResponse extends IBaseResponse` — detail envelope.
- `IEnqueueImportInvoicesRequest` — async import enqueue payload (wraps `IssuedInvoiceSourceQuery`).
- `IEnqueueImportInvoicesResponse extends IBaseResponse` — async import job acknowledgment.
- `IBackgroundJobInfo` — Hangfire job status.

## API / Interface Design
No new API. Hook public signatures keep their names; their return types change to the generated equivalents:

- `useIssuedInvoicesList(filters): UseQueryResult<IGetIssuedInvoicesListResponse>`
- `useIssuedInvoiceDetail(invoiceId): UseQueryResult<IGetIssuedInvoiceDetailResponse>`
- `useEnqueueInvoiceImport(): UseMutationResult<IEnqueueImportInvoicesResponse, Error, IEnqueueImportInvoicesRequest>`
- `useInvoiceImportJobStatus(jobId?): UseQueryResult<IBackgroundJobInfo | null>`
- `useRunningInvoiceImportJobs(): UseQueryResult<IBackgroundJobInfo[]>`

`IssuedInvoicesFilters` remains a hook-input type, exported from `useIssuedInvoices.ts` under its current name.

## Dependencies
- `frontend/src/api/generated/api-client.ts` — already present, generated at build time by NSwag. No changes required here.
- `@tanstack/react-query` — already used. No version change.
- No new packages, no backend changes, no API contract changes.

## Out of Scope
- Migrating the hooks to call generated client methods (`apiClient.issuedInvoices_GetList(...)`, etc.) instead of raw `fetch`. That is a larger refactor and not required to address the type-drift finding.
- Investigating whether the backend actually returns `customerEmail` / `customerPhone` / `customerAddress` / `items` for invoice details at runtime, and (if so) updating the OpenAPI document / NSwag output. Track separately.
- Filing or fixing other OpenAPI/NSwag bugs uncovered during the migration.
- Renaming `IssuedInvoicesFilters`.
- Touching any module other than Invoices.
- Updating Playwright E2E test fixtures.

## Open Questions
None.

## Status: COMPLETE