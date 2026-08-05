# Architecture review: Route Packaging (Baleni) FE hooks through the generated API client

Reviewed `plan-01.md` and `design-01.md` against the live codebase (branch `harness/tsk_1186ed0ff5334248`, commit `c3e49f34`). Method verified: read every generated `packaging_*` method and DTO the design cites directly out of `frontend/src/api/generated/api-client.ts` at the cited line numbers, read all 8 target hook files in full, read the reference implementation (`usePackingMaterials.ts`), read `frontend/src/api/client.ts` in full, and checked the existing `useCompletePackingOrder.test.ts` mock.

## Verdict

**Approved with one required change before implementation starts.** The plan and design correctly identify the violation, correctly cite the reference pattern (`usePackingMaterials.ts`), and every generated method signature / DTO field they cite matches the real `api-client.ts` exactly — no invented endpoints, no wrong line numbers, no wrong field names. The `Date`-vs-`string`, carrier-enum, and errorCode-typing open questions are resolved correctly and match the code they cite.

There is one gap that isn't cosmetic: **the design silently drops the `showErrorToasts=false` argument that 5 of the 8 hooks currently pass to `getAuthenticatedApiClient`, which will turn on global duplicate error toasts in production** — a real behavior regression on the packing-kiosk hot path that the plan's own FR-3 ("error toasts/messages unchanged") explicitly forbids, and that no existing test would catch.

## Alignment with existing patterns

- `usePackingMaterials.ts` is confirmed as the correct reference: it imports/re-exports generated types, calls `getAuthenticatedApiClient().packingMaterials_*(...)` directly, casts request bodies with `as` rather than `new X(...)`. The design's per-hook prescriptions follow this shape faithfully.
- `docs/development/api-client-generation.md` codifies exactly the rule this ticket enforces: `apiClient.http.fetch` / `(apiClient as any).baseUrl` are explicitly called out as forbidden ("breaks silently on NSwag regeneration"), and `getAuthenticatedApiClient()` / `getAuthenticatedFetch()` are the only sanctioned transports. The design's direction is the documented, not just inferred, correct pattern.
- All 10 generated method signatures the design/plan cite (`packaging_ScanOrder` api-client.ts:8798, `packaging_ResetShipment`:8843, `packaging_GetOrderTrackingNumber`:8928, `packaging_GetOrderTrackingNumbers`:8965, `packaging_GetDashboard`:9002, `packaging_GetStatistics`:9036, `packaging_GetPackages`:9074, `packaging_DeletePackage`:9170, `packaging_CompletePacking`:9207) match verbatim, including parameter order/types (`carrier: Carriers | null | undefined`, `fromDate/toDate: Date | null | undefined`, etc.).
- `ScanPackingOrderResponse`/`ScanOrderData`/`ShippingAddress` field shapes at api-client.ts:33604+ match the design's schema table exactly (no renamed/missing fields; the only structural delta is `Date` vs `string`, as the design says).

## Required change: preserve `showErrorToasts` suppression

`frontend/src/api/client.ts:276-360` (`getAuthenticatedApiClient(showErrorToasts = true)`) wraps **every** generated method's HTTP call — including `packaging_*` — in a shared `authenticatedHttp.fetch` closure. When `showErrorToasts` is true (the default) and a response comes back with `success === false && errorCode` set (HTTP 200 — exactly the envelope these hooks hand-parse today), `extractErrorMessage` (client.ts:218-224) classifies it as a structured business error and **fires the global toast** (`globalToastHandler("Upozornění", getErrorMessage(errorCode, params))`) in addition to whatever the hook's own caller does with the thrown error. The same default also fires a toast on any non-2xx HTTP response.

Verified in the current tree that **5 of the 8 hooks in scope deliberately opt out of this** by passing `false`:
- `useScanPackingOrder.ts:82` — `getAuthenticatedApiClient(false)`
- `useResetOrderShipment.ts:30` — `getAuthenticatedApiClient(false)`
- `useCompletePackingOrder.ts:16` — `getAuthenticatedApiClient(false)`
- `useOrderTrackingNumber.ts:11` / `useOrderTrackingNumbers.ts:11` — `getAuthenticatedApiClient(false)`

`usePackingDashboard.ts` / `usePackingStatistics.ts` use `getAuthenticatedFetch()` instead, which client.ts:417-419 documents as explicitly **not** triggering global toasts or the 401 redirect — the same suppression, via the other sanctioned escape hatch.

Neither `plan-01.md` nor `design-01.md` mentions this parameter anywhere. Every code sample in `design-01.md` (e.g. "`getAuthenticatedApiClient().packaging_ScanOrder(orderCode, numberOfPackages, body)`", section 1) calls the client with the default `true`. If implemented as written, this introduces a generic global toast ("Upozornění" + `getErrorMessage(...)`'s message — a *different* message than the hook's own curated `SCAN_ERROR_MESSAGES`/`RESET_ERROR_MESSAGES`/`COMPLETE_ERROR_MESSAGES` Czech copy) firing **alongside** whatever the mutation's own error handling already surfaces in the kiosk UI, on every scan/reset/complete/tracking-number business failure and HTTP error. This is not hypothetical: `packaging_ScanOrder`'s generated body (api-client.ts:8819) calls `this.http.fetch(url_, options_)` — the exact same instrumented closure `getAuthenticatedApiClient` constructs — so the toast behavior applies uniformly regardless of whether the call goes through the hand-rolled fetch or the generated method.

This is a direct conflict with the plan's own **FR-3** acceptance criterion ("error toasts/messages unchanged") and **FR-6** ("no regression in kiosk scan/reset/complete flows"). It also would not be caught by the acceptance checks already specified: `useCompletePackingOrder.test.ts`'s mock (verified: `jest.mock('../../client', () => ({ getAuthenticatedApiClient: jest.fn() }))`, `mockReturnValue` ignores call arguments) returns the same stub regardless of what argument is passed, so a test rewritten per FR-3/the design's own guidance ("mock `getAuthenticatedApiClient` returning `{ packaging_CompletePacking: mockFn }`") would pass even with the toast-suppression argument silently dropped — `tsc`/`npm run build`/unit tests give no signal here; only a manual reproduction of the kiosk flow with a business error and the toast provider mounted would surface it.

There is a **direct, already-established precedent for exactly this call shape** elsewhere in the codebase: `frontend/src/api/hooks/useDashboard.ts:83,102` — `const apiClient = getAuthenticatedApiClient(false); ... apiClient.dashboard_GetUserSettings()` / `apiClient.dashboard_GetTileData(undefined)`. This is the pattern the downstream fix should copy, not a novel one.

**Fix for the design:** every rewritten call site that today passes `getAuthenticatedApiClient(false)` or uses `getAuthenticatedFetch()` must call `getAuthenticatedApiClient(false).packaging_*(...)` in the rewrite — i.e. add `FR-7` (or fold into FR-3) explicitly: *"Preserve the `showErrorToasts=false` argument (or its `getAuthenticatedFetch()` equivalent) on every hook that currently suppresses global toasts; only `usePackages.ts`'s two mutations already use the default `true` and should keep doing so."* The acceptance check for this needs to be behavioral, not just `tsc`-clean: manually trigger a scan/reset/complete business error (e.g. via a mocked 200 response with `success:false`) against a running app with the toast provider mounted, and confirm no global "Upozornění" toast appears — only the hook's existing curated-message UI path fires.

## Other invariant checks (all pass)

- **DTO ownership / classes-not-records rule** (`docs/architecture/development_guidelines.md`, `docs/development/api-client-generation.md`) — not implicated; this is a pure FE consumption change, no new backend DTOs.
- **Absolute-URL rule** (root `CLAUDE.md`, api-client-generation.md) — the rework moves every call onto `getAuthenticatedApiClient()`/`getAuthenticatedFetch()`, both of which already resolve `baseUrl` correctly; no hook in the plan introduces a new raw relative-URL fetch.
- **`ScanOrderBody`/request casting convention** — design's `body as ScanOrderBody` for `packaging_ScanOrder` matches the established `as CreatePackingMaterialRequest` / `as UpdateQuantityRequestGenerated` casting convention in `usePackingMaterials.ts:80,103`; no `new X(...)` construction needed, consistent with how the generated client only calls `JSON.stringify` on it.
- **Test-mock migration pattern** (FR-3's acceptance criterion) — the shift from asserting "raw fetch URL/args" to "generated method called with args" is the correct, minimal adjustment; no other hidden assertions in `useCompletePackingOrder.test.ts` depend on the old transport shape.
- **`usePackages.ts` toast default** — this file already calls `getAuthenticatedApiClient()` (default `true`, no suppression) for both the query and the delete mutation, so it has no pre-existing suppression behavior to lose; the design's plain rewrite for this file is fine as written and should **not** get the `false` argument, avoiding an over-correction if FR-7 is applied module-wide instead of per-hook.
- **No backend changes** — confirmed; all 9 generated methods and their response DTOs already exist in `api-client.ts`, nothing to regenerate.

## Implementation guidance (delta from design-01.md)

1. Add the `showErrorToasts` preservation requirement above as an explicit FR before implementation starts — this is the one substantive correction needed to the design.
2. Per-hook toast-argument matrix for the implementer (derived directly from the current tree, not from the design doc):

   | Hook | Current transport | Toast suppressed today? | Required call in rewrite |
   |---|---|---|---|
   | `useScanPackingOrder` | `getAuthenticatedApiClient(false)` | yes | `getAuthenticatedApiClient(false).packaging_ScanOrder(...)` |
   | `useResetOrderShipment` | `getAuthenticatedApiClient(false)` | yes | `getAuthenticatedApiClient(false).packaging_ResetShipment(...)` |
   | `useCompletePackingOrder` | `getAuthenticatedApiClient(false)` | yes | `getAuthenticatedApiClient(false).packaging_CompletePacking(...)` |
   | `useOrderTrackingNumber` | `getAuthenticatedApiClient(false)` | yes | `getAuthenticatedApiClient(false).packaging_GetOrderTrackingNumber(...)` |
   | `useOrderTrackingNumbers` | `getAuthenticatedApiClient(false)` | yes | `getAuthenticatedApiClient(false).packaging_GetOrderTrackingNumbers(...)` |
   | `usePackagesQuery` | `getAuthenticatedApiClient()` | no | `getAuthenticatedApiClient().packaging_GetPackages(...)` (unchanged default) |
   | `useDeletePackageMutation` | `getAuthenticatedApiClient()` | no | `getAuthenticatedApiClient().packaging_DeletePackage(...)` (unchanged default) |
   | `usePackingDashboard` | `getAuthenticatedFetch()` | yes | `getAuthenticatedApiClient(false).packaging_GetDashboard()` |
   | `usePackingStatistics` | `getAuthenticatedFetch()` | yes | `getAuthenticatedApiClient(false).packaging_GetStatistics(...)` |

3. Everything else in `design-01.md` — the type-deletion list, the re-export/alias strategy for `PackingOrder`/`ScanShipment`, the carrier-enum resolution, the `Date` conversion-at-call-boundary strategy, and the consumer-impact call sites (`BaleniHome.tsx`, `BaleniStatistics.tsx`, `PackingCharts.tsx`, `ZasilkyTable.tsx`) — is accurate against the current tree and needs no further correction. Proceed with implementation using `design-01.md` plus the toast-argument matrix above.

## Risks / prerequisites

- **Prerequisite:** the implementer must read `client.ts:276-360` and `:417-430` before touching any hook, since neither upstream artifact surfaces this mechanism — it is the one piece of hidden cross-cutting state this refactor touches.
- **Risk if skipped:** silent production regression (duplicate/unwanted toasts on the packing-kiosk hot path) that passes `tsc`, `npm run build`, and the existing/updated unit tests, surfacing only in manual or E2E use — exactly the class of drift this arch-review ticket was filed to eliminate, just relocated from "stale hand-written DTO" to "stale hand-written transport-config assumption."
- No other risks identified; scope, non-functional requirements, and out-of-scope boundaries in `plan-01.md` are otherwise sound.

```json
{"outcome": "done", "summary": "Verified plan-01.md/design-01.md against the live api-client.ts, client.ts, and all 8 hook files: generated method signatures/DTOs match exactly and the usePackingMaterials.ts reference pattern is followed correctly. Found one real gap — the design drops the showErrorToasts=false argument that 5 of 8 hooks currently pass to getAuthenticatedApiClient (2 more use the equivalent getAuthenticatedFetch() suppression), which would turn on duplicate global toasts on the kiosk hot path, contradicting the plan's own FR-3/FR-6 and unguarded by any existing test. Wrote architecture-01.md with the required fix, a per-hook toast-argument matrix, and confirmation that the rest of the design needs no changes."}
```
