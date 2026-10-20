# Architecture Review: Suppress label-poll 404 noise in browser telemetry

## Skip Design: true
No new or changed visual components. Frontend-only plumbing in telemetry and an optional abort signal; no layout or UX change.

## Architectural Fit Assessment
Fits existing patterns. Server side already has the `ITelemetryProcessor` pattern (`LabelPollNotFoundTelemetryFilter`, registered in `ApplicationInsightsExtensions.cs`). The browser counterpart is the existing `addTelemetryInitializer` hook in `frontend/src/telemetry/appInsights.ts`. Poll loops already support `AbortSignal` in `printLabelWithReadiness` (used by `PackingLabelPrintModal`); `printLabelPdf` simply does not forward one. No backend change is required. No DB, no API/OpenAPI change (so no client regeneration).

## Proposed Architecture

### Component Overview
```
printLabelPdf.ts  --fetchLabelPdf(isPoll)-->  apiClient.http.fetch (+ X-Label-Poll)
        |                                              |
        | signal (new)                                 v
PackingLabelPrinter / ZasilkyPage              AI JS SDK Fetch tracking
                                                       |
                                  addTelemetryInitializer (new filter) --drop--> (poll 404 label.pdf)
```
Filter logic lives in a small pure function (`shouldDropTelemetryItem`-style) in a new file `frontend/src/telemetry/labelPollFilter.ts`, registered from `initAppInsights`. Keeping it pure makes it unit-testable without the SDK.

### Key Design Decisions

#### Decision 1: Where to detect "is poll"
**Options considered:** (a) read `requestHeaders` from dependency properties; (b) add a query param to the URL; (c) module-level registry of in-flight poll URLs in `printLabelPdf.ts`.
**Chosen approach:** (a) primary, with the match on lower-cased header name; if verification during implementation shows the SDK does not surface request headers for this dependency type, switch to (c). Do not use (b): it changes the cached/loggable URL and the backend contract.
**Rationale:** (a) reuses the marker the backend already keys on, zero extra state. (c) is a safe fallback because `fetchLabelPdf` is the single chokepoint for label fetches.

#### Decision 2: Drop vs. downgrade
**Chosen approach:** Drop (return `false`) only 404 + poll-marked + label.pdf URL. Everything else passes. This mirrors the server filter semantics exactly, so the final unmarked confirmation 404 remains the one failure signal.

#### Decision 3: Cancellation
**Chosen approach:** Add optional trailing `signal?: AbortSignal` to `printLabelPdf` and forward it. Components own an `AbortController` ref, abort on unmount / order change / new attempt. Aborted loops return `{printed:false}` without a status, so the `.then` handler must check `signal?.aborted` before calling `onError` (otherwise a cancelled loop would flash an error).
**Rationale:** Smallest change that bounds concurrent loops; no global state.

## Implementation Guidance

### Directory / Module Structure
- New: `frontend/src/telemetry/labelPollFilter.ts` (+ `__tests__/labelPollFilter.test.ts`)
- Edit: `frontend/src/telemetry/appInsights.ts` (register initializer; keep identity initializer intact)
- Edit: `frontend/src/components/baleni/printLabelPdf.ts` (`printLabelPdf` signal param; guard `onError` on abort; export the header constant for reuse)
- Edit: `frontend/src/components/baleni/PackingLabelPrinter.tsx`, `frontend/src/components/baleni/zasilky/ZasilkyPage.tsx` (abort controllers)
- Edit tests: `printLabelPdf.test.ts`, `PackingLabelPrinter.test.tsx`, `telemetry/__tests__/appInsights.test.ts`
- Edit doc: `docs/processes/flow-order-packing.md`

### Interfaces and Contracts
- `export const LABEL_POLL_HEADER = 'X-Label-Poll'` exported from `printLabelPdf.ts` (currently module-private).
- `isExpectedLabelPollNotFound(item: ITelemetryItem): boolean` in `labelPollFilter.ts`; initializer returns `false` when it is true, otherwise `true`/undefined.
- `printLabelPdf(orderCode, label, onAfterPrint?, onError?, signal?)`.

### Data Flow
Poll fetch -> 404 -> SDK builds dependency item -> initializer matches (URL regex `/\/api\/packaging\/orders\/[^/]+\/packages\/\d+\/label\.pdf/`, resultCode 404, poll header) -> dropped. Final unmarked fetch -> 404 -> passes -> sent.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| SDK does not expose request headers on the dependency item, filter never matches | Medium | Unit test with realistic item shape plus manual check; fall back to in-flight URL registry (Decision 1c) |
| Aborting leaves print iframe / blob state inconsistent | Low | Abort only affects the wait loop; an in-progress successful print is unaffected |
| Aborted loop wrongly surfaces `onError` | Medium | Check `signal.aborted` in `.then` before `onError`; covered by test |
| Filter hides a real stuck-label signal | Low | Final unmarked request still reported (NFR-3); tested |
| Remaining storm is a different cause (label never generated for that order) | Medium | Out of scope operational follow-up; after this change only unmarked finals will remain, making real stuck labels much easier to spot |

## Specification Amendments
None. Spec assumption 1 is recorded as Decision 1 with a defined fallback.

## Prerequisites
None (no migrations, config, or infrastructure). Process-doc CI gate requires the `flow-order-packing.md` update in the same PR.
