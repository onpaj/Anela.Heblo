# Architecture review — Replace raw `http.fetch` bypass in `useManufactureOutput` & `useSemiproductRecipePdf`

## Verdict

**Approved as designed.** I re-derived every factual claim in `plan-01.md` and
`design-01.md` directly against the current source tree (not just trusted the
prior steps) and found no drift, no invariant violation, and no missing
consumer. This is a mechanical, low-risk refactor; proceed to implementation
as specified in `design-01.md`.

## What I verified against the live codebase

- **Generated methods exist exactly as claimed:**
  `manufactureOutput_GetManufactureOutput(monthsBack)` at
  `frontend/src/api/generated/api-client.ts:7418` and
  `manufactureBatch_GetRecipePdf(productCode, batchSize)` at
  `api-client.ts:6706`, both public, both typed.
- **Generated types match the design's data-schema section verbatim:**
  `GetManufactureOutputResponse` (30239), `ManufactureOutputMonth` (30280),
  `ProductContribution` (30344), `ProductionDetail` (30396) — all fields
  optional, `ProductionDetail.date` is genuinely `Date` (NSwag's `init()` does
  `new Date(_data["date"].toString())`), confirming the string→Date fix is real
  and necessary, not speculative.
- **Current hook files match the plan's line-level description exactly** —
  `useManufactureOutput.ts:37-55` and `useSemiproductRecipePdf.ts:12-22` both
  do the `(apiClient as any).baseUrl` / `(apiClient as any).http.fetch`
  bypass described in the evidence, confirming the finding is current, not
  stale.
- **`getAuthenticatedApiClient()` is synchronous** (`client.ts:276`, returns
  `ApiClient` not `Promise<ApiClient>`) — the plan's aside about dropping the
  stray `await` is correct and harmless to fix opportunistically since it's
  the same line being touched.
- **Consumer read-sites match what the design enumerates.** I read both
  `ManufactureOutput.tsx` and `ManufactureOutputModal.tsx` in full:
  `data.months`, `month.products`, `product.weightedValue/.quantity/.difficulty`,
  `monthData.productionDetails`, `record.amount/.pricePerPiece/.priceTotal`,
  `formatDate(record.date)` are all real, unconditional accesses that will
  break under the now-optional generated types exactly as the design predicts.
- **Established re-export convention confirmed.** `useManufacturedProductInventory.ts`
  does re-export/alias generated types for its consumer pages
  (`export type ManufacturedProductInventoryItem = IManufacturedProductInventoryItemDto`,
  `export { InventoryChangeType }`). The design's choice to keep
  `ManufactureOutputMonth`/`ProductContribution`/`ProductionDetail` importable
  from the hook module (rather than repointing every consumer import to
  `../generated/api-client`) matches this precedent, minimizing the diff.
- **`docs/development/api-client-generation.md` enforcement rules and escape
  hatch (`getApiBaseUrl()` + `getAuthenticatedFetch()`) read as quoted** —
  lines 212-219 and 274 match, and the "for endpoints whose business outcomes
  are HTTP status codes" carve-out doesn't apply here (see below).
- **Scope is complete and self-contained** — grepped the whole `frontend/src`
  tree for every reference to `useManufactureOutput`, `useSemiproductRecipePdf`,
  and `ManufactureOutputResponse`; the only hits are the 2 hooks + 3 consumer
  components already listed in scope. No test files reference these hooks
  (confirms the plan's "no existing tests" note) and no other page imports the
  hand-declared interfaces being deleted.

## One risk the plan/design didn't surface — checked, and it's a non-issue

Both generated methods special-case HTTP 204: `processManufactureOutput_GetManufactureOutput`
and `processManufactureBatch_GetRecipePdf` resolve to `null as any` on a 204
response instead of throwing (only `status !== 200 && status !== 204` throws).
If either backend endpoint could return 204, `apiClient.manufactureBatch_GetRecipePdf(...).data`
would throw a *different*, unhandled `TypeError` (reading `.data` off `null`)
instead of landing in the hook's existing `catch` — a behavior change from
"controlled error state" to "uncaught exception."

I checked both controllers:
- `ManufactureOutputController.GetManufactureOutput` returns
  `HandleResponse(response)` off a plain `ActionResult<T>` — no 204 path.
- `ManufactureBatchController.GetRecipePdf` returns either `BadRequest(response)`
  or `File(...)` (200) — no 204 path.

Neither endpoint can produce 204 today, so this is not a live bug — but it's
an implicit invariant ("this endpoint never returns 204") that isn't written
down anywhere. **Recommendation:** no design change needed; just don't let a
future PR add a 204/"no content" branch to either handler without also
updating these hooks to guard `response`/`response?.data` for null. Not
worth blocking on — flagging for awareness only.

## Alignment with project conventions

- Matches `docs/development/api-client-generation.md`'s sanctioned pattern
  (`getAuthenticatedApiClient()` + generated method) with no need for the
  `getApiBaseUrl()`/`getAuthenticatedFetch()` escape hatch, since neither
  endpoint needs raw status-code branching — the design correctly identifies
  this and doesn't over-engineer with the escape hatch.
- DTOs-as-classes rule (CLAUDE.md) is a non-issue here: the generated types are
  already NSwag-emitted classes; no new DTOs are introduced.
- No backend change, no new endpoint, no persistence/module-boundary impact —
  out of scope for the "no architectural changes without consulting docs
  first" gate; this is caller-side plumbing only.

## Implementation guidance (unchanged from design, reaffirmed)

Follow `design-01.md` section-by-section as written:
1. `useManufactureOutput.ts` — delete hand-declared interfaces, import +
   re-export the four generated types, call the generated method directly.
2. `useSemiproductRecipePdf.ts` — call `manufactureBatch_GetRecipePdf`, use
   `.data` directly, drop the manual `response.ok` check.
3. `ManufactureOutput.tsx` / `ManufactureOutputModal.tsx` — no import path
   change (types still resolve via the hook module's re-export); add `?? []`
   / `?? 0` guards at each now-optional access, fix `formatDate` to accept
   `Date | undefined` instead of `string`.
4. Let `npm run build` be the authority on which access sites still need a
   guard — don't try to hand-enumerate every site up front (the design
   correctly defers to this instead of guessing an exhaustive list, which I
   confirmed is necessary: e.g. `formatMonthDisplay(maxMonth.month)` and
   `getMonthShortName(m.month)` in `ManufactureOutput.tsx` also need
   `?? ""` guards once `month` becomes optional — the design's illustrative
   list doesn't call these out by name, but its verification plan already
   accounts for exactly this gap via the build-error loop).

## Prerequisites before implementation begins

None outstanding. No open questions block starting; the one open question
in the plan (`ProductionDetail.date` string→Date) is resolved with hard
evidence from the generated source, not assumption.
