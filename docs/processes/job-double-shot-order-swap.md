---
process: job-double-shot-order-swap
kind: job
module: shoptet-orders
summary: Every 5 minutes (and, once its flag is on, before each picking-list print) replaces double-shot V variant lines in open Shoptet orders with their S mirror product set, so set components are deducted, picked and invoiced.
owns:
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/**
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/SwapDoubleShotOrdersJob.cs
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersDoubleShotPickingPreparerAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderDetail.cs
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/NewEshopOrderItem.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.Items.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/OrderItemModels.cs
  - backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShotCodeRule.cs
verified_at: "470c545cc"
related: [flow-picking-list, feed-issued-invoices]
---

# Double-shot V to S order swap

## Purpose
A double-shot product exists twice in Shoptet. The **V variant** (e.g. `SA015030V`) is a plain
product shown on the e-shop for customers to order. Its mirror **S product set** (`SA015030S`)
carries the composition (the set components). A V line deducts and picks nothing useful, so each
V line in an open order must be replaced by the S set: then Shoptet deducts the component stock,
the warehouse picking list shows the components, and Flexi invoices follow the item edits.
Nobody does this by hand; the visible result is in Shoptet (order items, an internal note) and in
the warehouse picking list.

The real side effect is switched **off by default**: until feature flag
`is-double-shot-order-swap-enabled` is on, the sweep only logs what it would do (dry run).

## Trigger
- Hangfire recurring job `swap-double-shot-orders`, cron `*/5 * * * *` (every 5 minutes,
  Europe/Prague), category Warehouse, `DefaultIsEnabled = true`. Can be disabled or triggered
  from the Recurring Jobs admin page (the job checks `IRecurringJobStatusChecker` first).
- Pre-step of every picking-list print: `ExpeditionListService` calls
  `IPickingListOrderPreparer` (adapter `ShoptetOrdersDoubleShotPickingPreparerAdapter`) before
  building the list. This covers all four entry points of `flow-picking-list`. A failure is
  logged as a warning and printing continues. The pre-step runs only while the flag is on; with
  the flag off it returns at once and the recurring job alone produces the dry-run log.
- Flag `is-double-shot-order-swap-enabled` (default `false`): off = dry run, on = writes.

## Data flow
1. Download the stock CSV (`IEshopStockClient.ListAsync`) and keep distinct codes matching the V
   rule (`DoubleShotCodeRule`, `^SA\d+V$`; S code = same code with trailing `V` -> `S`).
2. For each V code: `GET /api/orders?productCode={V}&creationTimeFrom={now - lookback}`, all
   pages. Lookback = `ShoptetOrders:DoubleShotSwapLookbackDays` (14, clamped to at least 1).
   Union the orders (code + status id from the list response).
3. Drop orders whose list status is not eligible, without a detail call and without a log line
   per order (only counted in the summary). For each remaining order: `GET /api/orders/{code}`
   (404 = skip) and plan the swap; the status re-read from the detail still decides.
4. Per V line, apply (flag on):
   - `POST /api/orders/{code}/item` - `itemType: product-set`, code = S, same amount, unit price
     with VAT, VAT rate and `priceRatio`, `remark` = tag `auto-swap {V} #{vItemId}`;
   - `DELETE /api/orders/{code}/item/{vItemId}`.
5. If at least one swap completed: `PATCH /api/orders/{code}/notes` appends to `eshopRemark`
   `Automaticky prohozeno: SA015030V → SA015030S (2 ks); ...` (Unicode arrow, as the code emits).
6. Log a summary: orders scanned, orders skipped as not eligible, lines swapped / healed /
   failed, failed orders, and a dry-run marker. A busy sweep logs a separate warning instead.

Target: Shoptet order items and `eshopRemark` only. No Heblo table is written.

## Logic & formulas
- Eligible order states: `ShoptetOrders:DoubleShotSwapEligibleStatusIds`; empty (the default)
  means -1 Nova, 3 Ceka se na platbu, 32 Zaplaceno, -2 Vyrizuje se, 35 Poznamka, 73 Oprava-robot.
  Never 26 (Bali se) or later: those are past picking.
- Only lines with `itemType == product` and a V code are swapped. Each V line is planned alone,
  so two V lines with the same code are swapped separately (the tag carries the V item id).
- Swap = two calls, POST S then DELETE V; Shoptet has no atomic swap. Order of calls matters:
  a crash between them leaves both lines, never neither.
- Self-healing: if a `product-set` line whose `remark` equals the tag already exists, the POST
  is skipped and only the V line is deleted.
- A V line with amount <= 0 or unit price with VAT <= 0 is not swapped (logged as Warning), so a
  paid V line is never replaced by a free S line. A heal still proceeds for such a line: its S
  line already exists.
- POST 422 on a line: logged as error, V stays, the sweep continues with the next line. POST 409
  or any other POST error: the rest of that order waits for the next sweep; other orders continue.
- DELETE failure of any kind (after a successful POST, or in a heal): the order now holds both
  the V and the S line. Logged as Error naming both codes; the rest of that order waits, no note
  is written for that line. The next run retries the delete (heal).
- One sweep at a time: static lock, waits max 2 minutes; a second caller logs a warning and
  returns an empty summary (counted as SkippedBusy).
- Dry run: all reads still happen, every planned action is logged, no write call is made.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `FeatureManagement:is-double-shot-order-swap-enabled` | `false` | Off = dry run (log only) |
| `ShoptetOrders:DoubleShotSwapEligibleStatusIds` | `[]` = -1, 3, 32, -2, 35, 73 | Order states in which lines may be rewritten |
| `ShoptetOrders:DoubleShotSwapLookbackDays` | `14` | How far back (creation time) orders are searched |
| `Hangfire:SchedulerEnabled` | `false`; `true` in Production | Whether recurring jobs are scheduled at all |
| `ShoptetApi:BaseUrl`, `ShoptetApi:ApiToken` | secrets | Shoptet REST access |

The status list default is empty on purpose (the config binder merges arrays index-wise); the
service applies `.Distinct()` to a configured override. Feature flags can be overridden at
runtime in the admin UI (table `FeatureFlagOverrides`).

## Runtime facts
- Shoptet has no atomic swap. `PATCH /api/orders/{code}/item/{id}` changing `code`/`itemType`
  only relabels the line (no components, no stock movement) - never use it. - `docs/integrations/shoptet-api.md` 3.17 - 2026-10-12
- `POST .../item` with `product-set` expands components and deducts stock; `DELETE` returns the
  V line's stock. Works in states -1/-2 and after an invoice; invoices follow item edits. - `docs/integrations/shoptet-api.md` 3.17 - 2026-10-12
- Item edits are not written to order history, hence the `eshopRemark` note. - same - 2026-10-12
- `GET /api/orders?productCode=` matches a line code exactly. - `docs/integrations/shoptet-api.md` 3.8 - 2026-10-12
- Order codes of deleted orders are reused by Shoptet. - `docs/integrations/shoptet-api.md` 3.17 - 2026-10-12
- Prod mirror sets are SA014-SA017 x 005/030/100, codes ending in S (e.g. `SA015030S` <-> `SA015030V`). Test store 780175 has no real V/S pair (its set `SA014030` has no suffix). - #4479 on the prod stock CSV, test probe - 2026-10-12

## Known quirks
- The order note can be lost permanently: if appending the note fails, or the sweep is cancelled
  after a successful swap but before the note, the order is swapped without a note. The next run
  finds no V line, so it never writes the note (logged as Warning).
- A failed DELETE after a successful POST leaves V and S both in the order (double charge and
  double stock) until a later run deletes V. The next run retries the delete; if the Error keeps
  repeating for the same order, remove the V line by hand in Shoptet.
- Race with a parallel print: `CreatePickingListAsync` runs outside the sweep lock, so a job
  sweep can edit an order that a parallel synchronous print has just printed with its V line.
- One sweep at a time (static lock, 2-minute wait); a second caller logs a warning and skips.
  Hangfire runs a single worker, so the job and a scheduled print never overlap; contention is
  only possible with the synchronous "Spustit tisk oprav" / "Tisknout zakazku" endpoints.
- With the flag on, each picking-list print runs a sweep first (extra Shoptet calls: one stock-CSV
  download, one list call per V code, one detail call per eligible candidate order).
- A missing S set surfaces as a logged 422 and the V line stays; nothing is written for it.
- The note append is read-modify-write without concurrency control (see `module-shoptet-orders`).

## Rollout
Merged with the flag off: production logs planned swaps (dry run). Once the V variants exist in
Shoptet, check the dry-run log on a real V order for the exact V -> S pairs, then turn
`is-double-shot-order-swap-enabled` on.

Before enabling the flag, check on the test store that the S line remark
(`auto-swap SA015030V #123`) is not printed on the invoice PDF / order e-mail (unverified).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/DoubleShotOrderSwapService.cs` - sweep, lock, list-status pre-filter, POST/DELETE failure handling, note
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/DoubleShotSwap/DoubleShotSwapPlanner.cs` - pure planning, tag and heal rule, zero amount/price guard
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/SwapDoubleShotOrdersJob.cs` - recurring job and flag, `[AutomaticRetry(Attempts = 0)]`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersDoubleShotPickingPreparerAdapter.cs` - picking-list pre-step
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.Items.cs` - item add/delete/read, list by product code
- `backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShotCodeRule.cs` - V/S naming rule
