# Specification: Suppress label-readiness poll 404 noise in browser telemetry and bound orphaned poll loops

## Summary
The App Insights browser SDK records every 404 returned to the packing desk's label-readiness poll (`GET /api/packaging/orders/{orderCode}/packages/{n}/label.pdf`) as a failed `Fetch` dependency. Server-side request telemetry for these polls is already dropped (`LabelPollNotFoundTelemetryFilter`), but the client-side dependency telemetry is not, so telemetry scans see a "404 storm" (256 in 7 days, 124 on order 126019126 package 1). This change removes the expected-404 noise from client telemetry while keeping genuine stuck-label failures visible, and makes sure poll loops cannot outlive the screen that started them.

## Background
- `printLabelWithReadiness` (`frontend/src/components/baleni/printLabelPdf.ts`) polls the label proxy with backoff (1,2,3,5,10 s) for up to 30 s. Every in-loop request carries header `X-Label-Poll: 1`; the final post-timeout confirmation request omits it so a truly stuck label stays visible. One print of a not-yet-ready label therefore yields roughly 8-11 expected 404s.
- Backend: `PackagingController.GetPackageLabelPdf` returns 404 `PackageLabelNotFound` when the live Shoptet label list has no entry (or no `LabelUrl`) for the package. Handler logs poll 404s at Debug, non-poll at Warning. `LabelPollNotFoundTelemetryFilter` drops header-tagged 404 `RequestTelemetry` server-side.
- Gap 1: the JS SDK (`frontend/src/telemetry/appInsights.ts`, `disableFetchTracking: false`, `enableRequestHeaderTracking: true`) still emits a failed `RemoteDependencyData` (type Fetch) per poll. No telemetry initializer filters it.
- Gap 2: `printLabelPdf()` (used by `PackingLabelPrinter` and `ZasilkyPage` reprint) starts `printLabelWithReadiness` with no `AbortSignal`. If the packer scans another order, navigates away, or the component unmounts mid-poll, the loop keeps running to the 30 s deadline. Each retry-button press ("Zkusit štítek znovu") and each reprint starts another full loop. A single order that is genuinely not ready (package never generated, or Zasilky reprint of an expired/removed label) can therefore produce ~11 404s per click and, with repeated clicks, a concentrated storm like the 124 on one order.
- Root cause for order 126019126 pkg 1 (was a label ever generated) cannot be determined from the repo; it requires App Insights / Shoptet access (see Open Questions).

## Functional Requirements

### FR-1: Drop expected poll 404s from browser dependency telemetry
Add a telemetry initializer (or equivalent filter) to the browser App Insights instance that discards a `RemoteDependencyData` item when ALL hold: dependency target/name/URL matches `/api/packaging/orders/*/packages/*/label.pdf`, result code is 404, and the request was a readiness poll (carries `X-Label-Poll`).
**Acceptance criteria:**
- A failed Fetch dependency for a label.pdf URL with status 404 and the poll marker is not sent.
- A 404 for the same URL without the poll marker (final post-timeout confirmation) is still sent.
- Non-404 results (503, 401, 200) for label.pdf are still sent regardless of marker.
- Dependencies for other URLs with 404 are unaffected.
- Existing user-identity initializer behaviour is unchanged.

### FR-2: Poll loops are cancelled when their owner goes away
`printLabelPdf` accepts an optional `AbortSignal` (passed through to `printLabelWithReadiness`). `PackingLabelPrinter` aborts the in-flight loop when the order changes, the printer unmounts, a reprint is started, or the retry button starts a new attempt. `ZasilkyPage` reprint aborts its loop on unmount and before starting a new reprint.
**Acceptance criteria:**
- Unmounting `PackingLabelPrinter` mid-poll stops further label requests (no fetch after the abort).
- Starting a retry or reprint cancels the previous loop for that component, so at most one loop per component is active.
- An aborted loop does not call `onError` or `onAfterPrint`.
- Existing behaviour (backoff schedule, 30 s timeout, final non-poll confirmation fetch) unchanged when not aborted.

### FR-3: Process documentation
`docs/processes/flow-order-packing.md` documents that poll 404s are suppressed in both server request telemetry and browser dependency telemetry, how the poll marker works, and that loops are cancelled on unmount; `verified_at` bumped and `python3 scripts/process-docs/check.py index` run.

## Non-Functional Requirements

### NFR-1: Performance
No additional network calls. Initializer is O(1) per telemetry item.

### NFR-2: Security
The poll marker is a non-sensitive constant header already in use. No new data is logged.

### NFR-3: Observability integrity
A label that genuinely never becomes ready must still surface at least one failed signal (final non-poll 404 server-side and client-side).

## Data Model
None.

## API / Interface Design
No backend contract change. Frontend: `printLabelPdf(orderCode, label, onAfterPrint?, onError?, signal?)` gains an optional trailing `signal`. `appInsights.ts` registers one additional initializer.

## Dependencies
`@microsoft/applicationinsights-web` initializer API (`addTelemetryInitializer`, returning `false` drops an item); `enableRequestHeaderTracking` populates request headers on dependency properties.

## Out of Scope
- Changing the backend 404 semantics or filter (already shipped).
- Disabling Fetch tracking globally.
- Root-causing the specific order 126019126 in Shoptet (operational follow-up).
- Changing the retry/backoff schedule.

## Open Questions
None. Assumptions: (1) the SDK exposes the request header `X-Label-Poll` on the dependency item's properties when `enableRequestHeaderTracking` is true; if implementation proves it does not, fall back to correlating via a module-level in-flight poll URL set maintained by `fetchLabelPdf` (tagged just before fetch, removed after). (2) Investigation of order 126019126 is handled outside this change.

## Status: COMPLETE
