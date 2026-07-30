# Plan — FlexiBee ERP dependency latency: unchanged after #2987

## Summary

`petra-tesarikova.flexibee.eu` p95/p99 are flat-to-worse five weeks after #2987
was closed `completed` with no merged fix — only an unrelated, pre-existing
60s app-level timeout (added April 2026, before #2987 even existed) caps the
worst-case tail on one code path. #2987 got stuck exactly where its own
grooming comment predicted: bundling investigation with several candidate
fixes and never picking one. This plan closes that loop — it commits to one
concrete, doc-aligned fix (a Polly circuit breaker, the piece grooming already
scoped and never shipped) and turns the remaining "which resource drives the
tail" question into a bounded investigation with a required, checkable
output, instead of an open-ended re-run of the same suggestion.

## Context

- #2987 (filed 2026-06-05, closed `completed` 2026-06-22) reported the same
  standing latency (p95 5.8s, p99 21s, tail to a 300s ceiling). Its grooming
  comment declined to label it `agent`-ready, explicitly because it "bundles
  investigation + multiple options," and recommended splitting off one
  concrete slice: *"Add fail-fast timeout (10–30s) + Polly circuit breaker to
  the FlexiBee HTTP client."* That split-off issue was never created; #2987
  was closed the same day with no linked PR (verified via issue timeline —
  no `closed` event with a `commit_id`, no merged PR referencing #2987).
- The 300s→75s drop in `max` duration is **not** attributable to #2987. Git
  history shows `ManufactureErpOptions.ErpTimeoutSeconds` (default 60s,
  `backend/src/Anela.Heblo.Application/Features/Manufacture/Configuration/ManufactureErpOptions.cs:12`)
  was added in commit `ba2ce63d` (PR #601/#669, 2026-04-18) — a month *before*
  #2987 was filed. It wraps only the `SubmitManufactureAsync` call inside
  `SubmitManufactureHandler` (`.../UseCases/SubmitManufacture/SubmitManufactureHandler.cs:80-86`),
  which backs the `ConfirmProductCompletion` / `ConfirmSemiProductManufacture`
  confirm actions. No circuit breaker exists anywhere in
  `Anela.Heblo.Adapters.Flexi` — confirmed by grep; the only resilience
  pattern present is this single `CancellationTokenSource.CancelAfter`. Why
  `max` observed at the dependency level (all FlexiBee traffic, not just the
  manufacture path) dropped from 300,002ms to 74,980ms between the two
  windows is **not yet explained** by any code change found — this is one of
  the open questions FR-1 must resolve.
- Call-volume math changes the shape of "what to fix": the three named
  confirm operations total ~405 calls/week (232+146+27) against 18,791 total
  — ~2%. The other ~18,400 calls carry no `operation_Name` (background
  jobs/sync — `FlexiAnalyticsSyncJob`, `FlexiStockClient`,
  `FlexiLotsClient`, etc., none of which go through
  `ManufactureErpOptions.ErpTimeoutSeconds`). The dependency-level p95/p99 in
  both issues is a blended figure — it is very likely dominated by the
  background/unattributed 98%, not by the three confirm endpoints (whose own
  p95 — 1.7–2.4s — is already well under the blended 6.7s/23.4s figures).
  Decoupling the confirm endpoints (#2987 suggestion #3) would improve *their*
  latency and blast radius, but would probably **not** move the headline
  dependency p95/p99 the issue is tracking. This must be checked before
  committing to that work, not assumed.
- Per-resource attribution (#2987 suggestion #3's own KQL, `summarize ... by
  name`) was never run in either issue. Without it there is no evidence for
  *which* FlexiBee resource(s) drive the tail, so no fix — timeout, circuit
  breaker, caching, batching, or decoupling — can be targeted correctly.
  This plan makes that query a required, non-optional first step with a
  concrete output artifact, so this issue cannot be closed the same way
  #2987 was (declared "completed" with nothing shipped).

## Functional requirements

**FR-1 — Attribute the p95/p99 tail to specific FlexiBee resources and calling paths.**
Run the by-`name` drill-down (P7D) against `petra-tesarikova.flexibee.eu`,
split by `operation_Name` (confirm endpoints vs. unattributed/background), to
identify which resource(s) drive the tail and whether the confirm-path or the
background-path is the actual contributor to the dependency-level p95/p99.

```kql
dependencies
| where target == "petra-tesarikova.flexibee.eu"
| summarize calls=count(), p50=round(percentile(duration,50),0),
            p95=round(percentile(duration,95),0), p99=round(percentile(duration,99),0),
            maxdur=round(max(duration),0)
  by name, operation_Name
| order by p99 desc
```
Run via `./docs/routines/telemetry-anomaly/appinsights-query.sh --timespan P7D '<query>'`.

*Acceptance criteria:*
- A ranked table (by p99) of FlexiBee resource `name` values with call
  count, p50/p95/p99, and `operation_Name` breakdown is produced and recorded
  (see FR-2).
- The table explicitly states, with numbers, whether the confirm endpoints
  (`ManufactureOrder/ConfirmProductCompletion`,
  `ManufactureOrder/ConfirmSemiProductManufacture`,
  `ManufactureBatch/CalculateBatchPlan`) or the unattributed background share
  is the larger contributor to overall p95/p99.
- The 300s→75s `max` drop is explained (either a specific config/code change
  is identified, or it's confirmed to be a traffic-mix artifact with evidence)
  — do not leave it as an unresolved coincidence.

**FR-2 — Record findings before deciding a fix.**
Write the FR-1 output plus the resulting decision to a new
`docs/integrations/flexibee-api.md`, following the existing convention set by
`docs/integrations/shoptet-api.md` (endpoint findings must be documented
before further reliance, per `CLAUDE.md`) — FlexiBee has no equivalent doc
yet despite being the highest-latency dependency in the app.

*Acceptance criteria:*
- File committed with: the resource ranking table, the confirm-vs-background
  split, and an explicit "what we're fixing and why" decision line pointing
  at FR-3/FR-4.

**FR-3 — Add a Polly circuit breaker around the FlexiBee ERP submit call.**
This is the concrete, doc-aligned, lowest-risk fix that #2987's grooming
already scoped and approved in principle but which never got its own issue
or implementation. Wrap the `_manufactureClient.SubmitManufactureAsync` call
in `SubmitManufactureHandler` (`SubmitManufactureHandler.cs:46-47`) with a
Polly circuit breaker policy (per `development_guidelines.md`: "Polly —
External API calls"), so that once FlexiBee is failing/timing out
repeatedly, subsequent confirm attempts fail fast instead of each queuing
for the full 60s `ErpTimeoutSeconds` window. Follow the existing
`docs/superpowers/plans/2026-05-27-plaud-circuit-breaker-and-monitoring.md`
precedent for structure (typed exception on open circuit, logged/observable
state transitions).

*Acceptance criteria:*
- Circuit breaker opens after a configurable consecutive-failure/timeout
  threshold (default aligned with existing `ManufactureErpOptions` config
  section) and fails fast (no HTTP call, no 60s wait) while open.
- A distinct exception/error surfaces through `IManufactureErrorTransformer`
  so the UI shows a clear "ERP temporarily unavailable" message rather than
  a generic timeout, consistent with the existing `ManualActionRequired`
  degraded-completion path already in `ConfirmProductCompletionWorkflow`.
- Unit tests cover: breaker opens after threshold, fails fast while open,
  closes again after recovery (half-open probe).
- `dotnet build` + `dotnet format` + all touched tests pass.

**FR-4 — Scope (do not implement) async write-back, gated on FR-1 evidence.**
Only if FR-1 shows the confirm endpoints materially contribute to the
dependency-level p95/p99 (not just their own already-acceptable 1.7–2.4s):
produce a short design note (not code) evaluating decoupling
`ConfirmProductCompletion`/`ConfirmSemiProductManufacture` from the
synchronous FlexiBee write in `ConfirmProductCompletionWorkflow.SubmitToErpAsync`
— e.g. a new `PendingErpSync`-style `ManufactureOrderState` plus a background
retry job, reusing the `ManualActionRequired`/note mechanism that already
exists in the workflow for degraded ERP outcomes. File this as its own
scoped GitHub issue (mirroring how #2987's circuit-breaker slice should have
been split off) rather than implementing it inline — it touches the order
state machine and frontend polling and is too large for this fix.

*Acceptance criteria:*
- If FR-1 shows background/unattributed calls dominate instead: FR-4 is
  explicitly marked not-applicable in the FR-2 doc, and a separate note
  recommends #2987 suggestion #2 (batch/cache/rate-limit background sync) as
  the next telemetry follow-up instead — do not silently drop it.

## Non-functional requirements

- **Fail-fast over fail-slow**: the circuit breaker's purpose is bounding
  blast radius (thread/connection pool exhaustion under FlexiBee degradation)
  on the confirm path, not improving average latency.
- **Observability**: circuit state transitions (closed→open→half-open) must
  be logged at a level that shows up in the existing App Insights
  `traces`/`exceptions` query surface used by the telemetry-anomaly routine,
  so a future run can verify the fix instead of re-filing the same signal a
  third time.
- **No behavior change for the 98% background path** — FR-3 only touches the
  manufacture confirm submit path; background sync jobs are explicitly out of
  scope for FR-3 (see FR-4 gating).

## Data model

No new persistent entities for FR-1–FR-3. FR-4 (if triggered) would need a
new `ManufactureOrderState` value and a pending-sync marker on
`ManufactureOrder` (deferred to its own design/issue, not modeled here).

## Interfaces

- No new public API surface for FR-1/FR-2 (investigation + doc only).
- FR-3: internal resilience policy wrapping the existing
  `IManufactureClient.SubmitManufactureAsync` call site; no contract change
  to `ConfirmProductCompletion`/`ConfirmSemiProductManufacture` request/response
  DTOs. The user-visible change is the error message text on circuit-open,
  routed through the existing `IManufactureErrorTransformer` →
  `SubmitManufactureResponse.UserMessage` → workflow note path.
- FR-4, if scoped as a follow-up issue: would eventually change
  `ManufactureOrder` state transitions and require frontend polling/UI for a
  "pending ERP sync" state — explicitly not designed here.

## Dependencies and scope

**In scope:** App Insights KQL drill-down (FR-1), findings doc (FR-2), Polly
circuit breaker on the manufacture ERP submit path (FR-3), and — conditionally
— a design note plus a new follow-up GitHub issue for async write-back (FR-4).

**Out of scope for this task:**
- Implementing async/queued write-back itself (FR-4 produces a design +
  issue, not code).
- Touching the 18k/week background/unattributed FlexiBee calls (batching,
  caching, rate-limiting) — flagged as a possible follow-up per FR-4's
  fallback, not implemented here.
- Changing `Rem.FlexiBeeSDK.Client`'s own internal HTTP timeout default
  (external NuGet package, not in this repo) or upgrading it.
- Any Azure Portal / Key Vault changes.

**Dependencies:** `APPINSIGHTS_APP_ID`/`APPINSIGHTS_API_KEY` env for FR-1
(same as the telemetry-anomaly routine); Polly is already an approved,
in-repo dependency per `development_guidelines.md` (no new package expected
beyond confirming `Polly`/`Microsoft.Extensions.Http.Polly` is referenced
where `SubmitManufactureHandler` lives).

## Rough plan

1. Run the FR-1 KQL drill-down; capture the resource-ranked table and the
   confirm-vs-background split.
2. Write `docs/integrations/flexibee-api.md` (FR-2) with the findings and the
   explicit fix decision.
3. Implement the Polly circuit breaker around `SubmitManufactureAsync` in
   `SubmitManufactureHandler` (FR-3), TDD per the Plaud-circuit-breaker
   precedent: failing test → policy → passing test → `dotnet format` →
   `dotnet build`.
4. Based on FR-1's evidence, either (a) write the FR-4 design note and file a
   new scoped follow-up issue, or (b) record in the FR-2 doc why FR-4 is not
   applicable and point at the background-calls follow-up instead.
5. Full backend build/test/format pass; confirm no regression in existing
   `SubmitManufactureHandler`/manufacture confirm test suites.

## Open questions

- **What actually caused `max` to drop from 300,002ms to 74,980ms between the
  two windows, if not #2987?** Resolved as part of FR-1 — default
  assumption until proven otherwise: an unrelated infra/SDK-version change
  (e.g. the `Rem.FlexiBeeSDK.Client` bump to 0.1.139 in commit `3f5445b0`, or
  a change in traffic mix) rather than a deliberate timeout fix.
- **Is the confirm path or the background path the real p95/p99 driver?**
  Default assumption pending FR-1 evidence: background/unattributed, given it
  is 98% of call volume — FR-4 is written as conditional specifically because
  of this uncertainty rather than assumed to be needed.
- **Circuit breaker failure threshold/duration** — no existing convention in
  this codebase (Plaud used retry-suppression, not a circuit breaker). Default
  to Polly's standard `CircuitBreakerAsync` shape (e.g. break after 5
  consecutive failures, 30s break duration) unless FR-1's data suggests a
  different threshold; make it configurable via `ManufactureErpOptions` rather
  than hardcoded, consistent with `ErpTimeoutSeconds`.
