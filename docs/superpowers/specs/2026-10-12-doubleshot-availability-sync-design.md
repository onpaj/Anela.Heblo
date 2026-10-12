# Double-shot availability sync (V variant ← S mirror set) — Design

Issue: #4479 (parent #4477, sibling #4480). Deadline: 19 Oct 2026.

## Goal

The 4 double-shots are sold on the e-shop as one variant product per serum (`V` codes, 5/30/100 ml).
Shoptet cannot show a product set as a variant, so every `V` has a mirror product set `S` that
carries the real composition and whose stock Shoptet computes from its components. Nobody maintains
the `V` stock. A recurring job keeps each `V`'s orderable stock equal to its `S` mirror's, so a
double-shot is never shown sold out while its components are in stock (and vice versa).

## Facts verified on production (2026-10-12, read-only)

- 12 pairs exist: `SA014…SA017` × `005/030/100`, e.g. `SA015030V` ↔ `SA015030S`.
  The codes are identical except for the final letter. The V rows share a `pairCode` (81–84).
- All V rows have stock 0 today; S rows carry Shoptet-computed stock (e.g. `SA015030S` = 213).
- Product sets appear in the stock CSV export with their computed stock.
- The CSV `stock` column equals `GET /api/stocks/{id}/supplies` → `amount` (free-for-order,
  already net of `claim`). Writing `realStock` sets `amount = realStock − claim`.
- Other codes ending in `V` exist (`BAL00xxV` packages, hand-set to 1000) and must never be touched.

## Decisions (confirmed with Ondra)

| Question | Decision |
|---|---|
| V↔S mapping | Derived rule: code matches `^SA\d+V$` and the same code ending in `S` exists. No config. |
| Source of truth | Shoptet-computed `S` stock from the same CSV export. |
| Cadence | Every 15 minutes. |
| At zero / negative | Write `max(S, 0)`; visibility and "vyrábíme čerstvé" / selling into minus stay configured in Shoptet. |
| Dry run | Feature flag `is-doubleshot-availability-sync-enabled`, default **off** = dry run (log only). Going live = switching the flag on, nothing else. |

## Components

### 1. `DoubleShotCode` (Domain, shared with #4480)

`backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShots/DoubleShotCode.cs` — pure static helper, no I/O.

- `bool IsVariantCode(string? code)` — `^SA\d+V$` (ordinal, case-sensitive; Shoptet codes are upper-case).
- `string? ToSetCode(string? variantCode)` — `SA015030V` → `SA015030S`; `null` when not a variant code.
- `string? ToVariantCode(string? setCode)` — `SA015030S` → `SA015030V`; `null` when not `^SA\d+S$`.

Callers check that the mirror exists in their own data (this job: the CSV; #4480: its order/catalog data).

### 2. `DoubleShotStockPlanner` (Application, pure)

`Plan(IReadOnlyList<EshopStock> stock) → IReadOnlyList<DoubleShotStockAdjustment>`

- For every row passing `IsVariantCode` whose `ToSetCode` exists in the list: target = `max(S.Stock, 0)`.
- Emits an adjustment (`VariantCode`, `SetCode`, `Current`, `Target`, `Delta`) only when `Target ≠ Current`.
- Reports V codes without an S mirror separately (`UnpairedVariantCodes`) so the job can warn.

### 3. `DoubleShotAvailabilitySyncJob : IRecurringJob`

`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/` — template `CompleteDeliveredOrdersJob`.

- Metadata: `doubleshot-availability-sync`, category Catalog, cron `*/15 * * * *`, `DefaultIsEnabled = true`.
- Skips when disabled via `IRecurringJobStatusChecker`.
- Reads stock once via `IEshopStockClient.ListAsync`, wrapped in `ICatalogResilienceService`.
  On failure: log error, write nothing, end the run.
- Flag off → logs each planned change `[DRY RUN] code old → new` and writes nothing.
- Flag on → `IEshopStockClient.UpdateStockAsync(vCode, delta)` (relative `amountChange`), one call per V.
  A failure on one V logs a warning and continues; the next run self-corrects from fresh state.
- Never writes to an `S` code.
- Summary log: pairs found, changed, failed, unpaired, dry-run marker.

Why relative `amountChange` and not `realStock`: the CSV value is free stock; `realStock` is gross
(Shoptet subtracts `claim`). A delta needs no extra `supplies` read and stays correct with pending V orders.
A V order landing between read and write causes at most a one-run drift, corrected on the next run.

### 4. Feature flag

`FeatureFlagKeys.DoubleShotAvailabilitySync = "is-doubleshot-availability-sync-enabled"`, registry entry
(default `false`), `appsettings.json` default. No frontend mirror — the frontend never reads this flag, and the mirror test only requires frontend keys to exist in the backend registry.

## Testing

- `DoubleShotCode`: V→S, S→V, non-matching (`BAL0001V`, `SA001`, `SA015030`, lower-case, null/empty).
- Planner: pairs only when both exist; negative S → 0; equal → no adjustment; `BAL…V` ignored; unpaired V reported.
- Job (mocked client/flag/status/resilience): disabled → no calls; flag off → no writes; flag on → writes only
  differing V with correct delta; one failure doesn't stop the rest; list failure → no writes; never writes S.
- Adapter: `UpdateStockAsync` request body parsed as JSON (`productCode`, `amountChange`, no `realStock`) —
  added only if not already covered.

## Docs

- New `docs/processes/job-doubleshot-availability-sync.md` (template `_TEMPLATE.md`), module overview updated,
  `python3 scripts/process-docs/check.py index`.
- `docs/integrations/shoptet-api.md`: double-shot V/S convention, CSV `stock` = `supplies.amount`,
  sets present in CSV with computed stock.

## Out of scope

- The V → S order swap (#4480).
- Toggling product visibility.
- Fixing the stale `Shoptet--Token` Key Vault secret (prod uses App Setting `Shoptet__ApiToken`) — flagged in the PR.

## Rollout

PR ships in dry-run. After deploy, review the dry-run log in App Insights, then Ondra switches the flag on.
No live Shoptet write is made during development.
