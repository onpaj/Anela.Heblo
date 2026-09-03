# FlexiBee (ABRA) API — Integration Findings

> **Living document.** Every new finding about the FlexiBee API MUST be added here before it is used elsewhere (mirrors the convention already established for `docs/integrations/shoptet-api.md`).

---

## 1. Overview

FlexiBee (ABRA) is the company's ERP. The project integrates with it via the adapter
`Anela.Heblo.Adapters.Flexi`, built on the external NuGet client
`Rem.FlexiBeeSDK.Client` (currently `0.1.139`). Target host:
`petra-tesarikova.flexibee.eu`.

It is, by a wide margin, the slowest and highest-volume outbound dependency in
production (see §2).

---

## 2. Latency findings (2026-07-28)

### 2.1 Headline numbers (blended, all FlexiBee traffic — from the telemetry-anomaly signal)

| Metric (P7D) | #2987 (2026-06-05→12) | This issue (2026-07-20→27) | Δ |
|---|---|---|---|
| Calls | 21,527 | 18,791 | -13% |
| p50 | 531 ms | 548 ms | +3% |
| p95 | 5,821 ms | 6,705 ms | **+15%** |
| p99 | 21,400 ms | 23,449 ms | **+10%** |
| max | 300,002 ms (5 min ceiling) | 74,980 ms | -75% |
| Failed | 18 | 13 | — |

p95/p99 are flat-to-worse five weeks after #2987 was closed `completed`. Only the
worst-case tail (`max`) dropped.

### 2.2 Confirm-path call volume and latency (from the signal; code-verified call sites)

| Calling operation | calls (P7D) | p95 | Code path |
|---|---|---|---|
| `POST ManufactureOrder/ConfirmProductCompletion [id]` | 232 | 1,694 ms | `SubmitManufactureHandler` via `ConfirmProductCompletionWorkflow` |
| `POST ManufactureOrder/ConfirmSemiProductManufacture [id]` | 146 | 2,411 ms | `SubmitManufactureHandler` via `ConfirmSemiProductManufactureWorkflow` |
| `POST ManufactureBatch/CalculateBatchPlan` | 27 | 1,687 ms | `BatchPlanningService` (does not call `SubmitManufactureAsync`) |

These three operations total ~405 calls/week (~2% of the 18,791 blended total) — the
remaining ~18,400 calls/week carry no `operation_Name` (background jobs/sync:
`FlexiAnalyticsSyncJob`, `FlexiStockClient`, `FlexiLotsClient`, and similar adapter
clients under `Anela.Heblo.Adapters.Flexi`). Each named confirm endpoint's own p95
(1.7–2.4s) is already well under the blended dependency-level p95/p99 (6.7s/23.4s),
which strongly suggests the background/unattributed 98% dominates the headline
figures, not the confirm path.

### 2.3 Per-resource attribution — NOT completed in this environment

Plan FR-1 called for running:

```kql
dependencies
| where target == "petra-tesarikova.flexibee.eu"
| summarize calls=count(), p50=round(percentile(duration,50),0),
            p95=round(percentile(duration,95),0), p99=round(percentile(duration,99),0),
            maxdur=round(max(duration),0)
  by name, operation_Name
| order by p99 desc
```

via `./docs/routines/telemetry-anomaly/appinsights-query.sh --timespan P7D '<query>'`.

**This could not be run in the development environment used to implement this
change** — `APPINSIGHTS_APP_ID`/`APPINSIGHTS_API_KEY` are not available here (the
script's own precondition check confirms this: `Error: APPINSIGHTS_APP_ID is not
set.`). This is a real gap, not a formality: without it, §2.2's "background
dominates" conclusion is inferred from call-volume math and each named endpoint's
own p95, not measured directly per FlexiBee resource. **Next telemetry-anomaly run
should execute this query and update this section before this gap is considered
closed.**

### 2.4 The 300s → 75s `max` drop — not explained by any FlexiBee-adapter code change

No circuit breaker existed anywhere in `Anela.Heblo.Adapters.Flexi` before this
change (confirmed by grep across the adapter). The only pre-existing timeout on the
confirm path is `ManufactureErpOptions.ErpTimeoutSeconds` (default 60s), added in
commit `ba2ce63d` (PR #601/#669, 2026-04-18) — a **month before #2987 was even
filed**, so it cannot explain a change observed *after* #2987 was closed on
2026-06-22. It also only wraps the confirm path's `SubmitManufactureAsync` call
(~2% of blended volume), not the 98% background/unattributed share where the `max`
figure is most likely to originate.

No other config or code change affecting FlexiBee call timeouts was found in this
repo's history in the relevant window. Two remaining candidate explanations —
neither confirmed, both requiring the App Insights access noted in §2.3 to settle:

1. A change in FlexiBee-side behavior or traffic mix outside this repo's control.
2. The `Rem.FlexiBeeSDK.Client` version currently pinned (`0.1.139`) changed its own
   internal HTTP timeout default between the two measurement windows — this repo
   does not control that package's source, only its version pin.

This is recorded as an **open, unresolved question** rather than assumed away.

---

## 3. Decision

**What is being fixed:** a Polly circuit breaker around the manufacture-order ERP
submit call (`SubmitManufactureHandler` → `IManufactureClient.SubmitManufactureAsync`),
mirroring the existing `CatalogResilienceService` precedent
(`Features/Catalog/Infrastructure/CatalogResilienceService.cs`). See
`ManufactureErpResilienceService`
(`Features/Manufacture/Infrastructure/ManufactureErpResilienceService.cs`). This
bounds blast radius on the ~2% of FlexiBee traffic that sits on the user-facing
manufacturing confirm path: once FlexiBee is failing/timing out repeatedly, further
confirm attempts fail fast (typed `ManufactureErpUnavailableException`, surfaced as
a Czech "FlexiBee je aktuálně nedostupný..." message) instead of each queuing for
the full `ErpTimeoutSeconds` window.

This is **not** a fix for the blended p95/p99 latency this issue was filed
against — per §2.2, the confirm path is not believed to be the dominant
contributor to that figure, and a circuit breaker changes failure-mode blast
radius, not steady-state latency. It fixes the concrete, doc-aligned, lowest-risk
piece of #2987's original suggestion list (grooming's "Add fail-fast timeout +
Polly circuit breaker" slice) that was scoped but never shipped.

**FR-4 (async/queued write-back for the confirm endpoints): marked not
applicable**, per the plan's own gating condition ("only if FR-1 shows the confirm
endpoints materially contribute to the dependency-level p95/p99"). §2.2's call-volume
math and each endpoint's already-acceptable own p95 (1.7–2.4s, well under the
blended 6.7s/23.4s) argue against the confirm path being the dominant contributor —
but this is inferred, not measured (§2.3), so treat this as a provisional decision to
revisit once the per-resource query above has actually been run.

**Recommended next telemetry follow-up** (in place of FR-4, per the plan's
fallback): once §2.3's per-resource query is run, if it confirms the
background/unattributed share (`FlexiAnalyticsSyncJob`, `FlexiStockClient`,
`FlexiLotsClient`, etc.) drives the blended p95/p99, the next step is
batching/caching/rate-limiting those background sync clients — not further tuning
the confirm-path timeout ceiling, which has already been tuned twice (#2987, this
issue) without moving the headline number.

## Ceník price writes

`PUT /c/{firma}/cenik/{idcenik}.json` with body
`{"winstrom":{"cenik":{"cenaZakl":"157.02"}}}` updates an item's base selling price.
`cenaZakl` is **excluding VAT**; `cenanakup` is the purchase price and is computed from
the BoM — never written by Heblo.

**Addressing by `code:` is dangerous.** Flexi makes no distinction between create and
update: it decides from the identifier. `PUT /c/{firma}/cenik/code:XXX.json` with an
unknown code **creates a new price list item** rather than failing. Always address writes
by the internal numeric `idcenik` (read as `ProductPriceFlexiDto.ProductId` from user
query 41). A product with no known `idcenik` must be reported as a failure, never created.
