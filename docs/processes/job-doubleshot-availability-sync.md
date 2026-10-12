---
process: job-doubleshot-availability-sync
kind: job
module: catalog
summary: Every 15 minutes, sets each double-shot variant's (SA…V) Shoptet stock to its mirror product set's (SA…S) computed stock, so the e-shop shows correct availability without manual restocking; dry run unless a feature flag is on.
owns:
  - backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShotCodeRule.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/DoubleShots/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/DoubleShotAvailabilitySyncJob.cs
verified_at: "5990b3e50"
related: [sync-catalog-stock]
---

# Double-shot availability sync

## Purpose
Double-shots are sold as one variant product per serum (`SA0xxNNNV`, 5/30/100 ml). Shoptet cannot show a
product set as a variant, so each variant has a mirror product set (`SA0xxNNNS`) whose stock Shoptet computes
from its components. Nobody maintains the variant stock; this job copies the set's stock onto the variant so
customers see correct availability. Requested by Andy (Teams "Cenotvorba", 8 Oct 2026); issue #4479, epic #4477.

## Trigger
Hangfire `doubleshot-availability-sync`, cron `*/15 * * * *` (Europe/Prague), enabled by default.
Can be disabled or run manually in Recurring Jobs. Writes only when feature flag
`is-doubleshot-availability-sync-enabled` is on; otherwise it is a dry run that only logs what it would change.
Going live = switching that flag on in the feature-flag admin; nothing else.

## Data flow
1. Shoptet stock CSV export (`StockClient:Url`) via `IEshopStockClient.ListAsync`, wrapped in `ICatalogResilienceService`.
2. `DoubleShotStockPlanner.Plan` pairs every `^SA\d+V$` code with the same code ending in `S` (`DoubleShotCodeRule`).
3. Dry run (flag off): log every pair whose CSV variant stock differs from the target. Nothing else is called.
4. Live (flag on), for **every** pair: `GET /api/stocks/{Shoptet:StockId}/supplies?code=<V>` (live free stock),
   then, if target ≠ live, `PATCH /api/stocks/{Shoptet:StockId}/movements` with
   `{ productCode: <V>, amountChange: target − live }` (`IEshopStockClient.UpdateStockAsync`).

No Heblo table is written. Every change is logged (`code old -> new`, with `[DRY RUN]` when the flag is off);
a summary line (pairs, to change, changed, failed, unpaired) ends every run.

## Logic & formulas
- target = max(stock of S, 0). The CSV `stock` is free-for-order stock (already net of open-order `claim`).
- Relative `amountChange`, not `realStock`: `realStock` is gross and Shoptet subtracts `claim` from it;
  a delta keeps the V's free stock exactly equal to the S's even when V has pending order lines.
- The delta is computed from the V's **live** `supplies.amount`, read right before the write — never from the CSV.
  A relative write from a cached CSV would compound (a repeated run would add +213 twice); with the live read,
  re-runs, manual triggers and a duplicated PATCH are all corrected by the next run.
- `[DisableConcurrentExecution]` + `[AutomaticRetry(Attempts = 0)]`: runs never overlap, and the next scheduled
  run is the retry.
- A V without its S row in the export is skipped with a warning — never zeroed.
- `S` codes are never written (Shoptet rejects writes to sets with `stock-change-not-allowed`).
- Codes like `BAL0001V` do not match `^SA\d+V$` and are never touched.
- One failed read/PATCH (or a V missing from `supplies`) logs a warning and the job continues with the next V;
  a failed CSV read writes nothing. The next run corrects both.
- The V↔S rule (`DoubleShotCodeRule`) is shared with the order swap (#4480).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `FeatureManagement:is-doubleshot-availability-sync-enabled` | `false` | Off = dry run (log only). On = writes. Overridable at runtime in the feature-flag admin. |
| `StockClient:Url` | KV `StockClient--Url` | Shoptet stock CSV export URL. |
| `Shoptet:StockId` | `1` | Warehouse id for the movements PATCH. |

## Runtime facts
- 12 pairs exist in prod: SA014–SA017 × 005/030/100 (Motýl, Krása, Teenka, Sladký) — prod stock CSV, read-only — 2026-10-12.
- Before the job, all V variants had stock 0 while their S mirrors carried computed stock (e.g. SA015030S = 213) — prod stock CSV — 2026-10-12.
- CSV `stock` equals `GET /api/stocks/1/supplies` `amount` (free stock, net of `claim`) — compared 141 items with claims — 2026-10-12.
- Until the #4480 swap replaces it, a V order line reserves V stock (`claim` on V), not component stock — #4480 probe, `shoptet-api.md` §3.17 — 2026-10-12.

## Known quirks
- A V order placed between the CSV read and the PATCH drifts the V by that quantity for one cycle; the next run corrects it.
- The CSV export may be cached by Shoptet for some minutes. Only the S target comes from it, so a stale export
  delays the V by up to one cache lifetime but never compounds.
- The Shoptet stock HttpClient's retry policy (built for the CSV GET) also wraps the movements PATCH, so a PATCH
  whose response times out can be applied twice (shared with `feed-stock-up`). Here the next run's live read
  corrects it.
- A manual edit of V stock in Shoptet is overwritten on the next run.

## Code entry points
- `backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShotCodeRule.cs` — V↔S code rule (shared with #4480)
- `backend/src/Anela.Heblo.Application/Features/Catalog/DoubleShots/DoubleShotStockPlanner.cs` — pairing and target
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/DoubleShotAvailabilitySyncJob.cs` — job, dry run, writes
