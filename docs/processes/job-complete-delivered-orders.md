---
process: job-complete-delivered-orders
kind: job
module: shoptet-orders
summary: Hourly job that moves Shoptet orders handed to a carrier (states 70/82) to "Vyřízena" (-3) once Shoptet reports one of their shipments delivered, and appends an audit remark; dry-run unless a feature flag is on.
owns:
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs
  - backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs
verified_at: "5e993f9e2"
related: []
---

# Auto-complete delivered orders

## Purpose
Closes e-shop orders automatically once the parcel has actually reached the customer, so
nobody has to move orders from "handed to carrier" (Předáno přepravci) to "completed"
(Vyřízena) by hand in the Shoptet admin. The result is visible only in Shoptet: the order's
status changes and the internal staff remark (Interní poznámka, `eshopRemark`) gets the line
`Automaticky vyřízeno – zásilka doručena`. Heblo stores nothing.

The real side effect is switched **off by default**: until the feature flag
`is-delivered-order-completion-enabled` is turned on, the job only logs which orders it *would*
complete (dry run).

## Trigger
- Hangfire recurring job `complete-delivered-orders`, cron `0 * * * *` (every hour on the hour,
  Europe/Prague), `DefaultIsEnabled = true`, category Warehouse. Can be disabled or triggered
  manually from the Recurring Jobs admin page (the job checks `IRecurringJobStatusChecker` first
  and exits if disabled).
- Recurring jobs are scheduled only where `Hangfire:SchedulerEnabled` is true — repo config sets
  it only in `appsettings.Production.json`, so it runs on Production only.

## Data flow
1. Read feature flags (DB override → `appsettings.json` `FeatureManagement` → registry default):
   - `is-delivered-order-completion-enabled` — off ⇒ dry run.
   - `is-delivered-order-completion-test-source-enabled` — on ⇒ poll the test states instead.
2. Pick source states: `ShoptetOrders:DeliveredCompletionSourceStateIds` (default `[70, 82]`)
   or, with the test-source flag, `ShoptetOrders:DeliveredCompletionTestSourceStateIds`
   (default `[73]`) — the test list *replaces* the production list.
3. For each source state: `GET /api/orders?statusId={id}&page={n}&itemsPerPage=50`, following
   the paginator until `pageCount` (`ShoptetOrderClient.ListOrdersByStatusAsync`).
4. For each order: `GET /api/shipments?orderCode={code}` (`ShoptetShipmentClient.HasDeliveredShipmentAsync`,
   reached via the ShipmentLabels adapter). Delivered = **any** shipment with
   `status == "delivered"` (case-insensitive).
5. Delivered and flag on:
   - `PATCH /api/orders/{code}/status` `{"data":{"statusId": CompletedStatusId}}` (default `-3`, Vyřízena).
   - Append remark: `GET /api/orders/{code}?include=notes` → read `notes.eshopRemark` →
     `PATCH /api/orders/{code}/notes` `{"data":{"eshopRemark": "<old>\nAutomaticky vyřízeno – zásilka doručena"}}`
     (no leading newline when the remark was empty).
6. Log a summary: scanned / delivered / completed (+ `[DRY RUN]` marker).

Target: Shoptet order status and `eshopRemark` only. No Heblo table is written.

## Logic & formulas
- Order states (anela.cz, from `ShoptetOrdersSettings` comments and `docs/integrations/shoptet-api.md`):
  70 = Předáno přepravci, 82 = SMS chlazené-Předáno dopravci, 73 = Oprava-robot, -3 = Vyřízena.
- "Delivered" means at least one shipment of the order is `delivered`; other shipments of a
  multi-parcel order are not checked.
- Idempotent: a completed order leaves states 70/82 and is not listed again.
- Error isolation: a failing state list is logged (Error) and that state is skipped; a failing
  order is logged (Warning) and retried at the next hourly run. A status change that succeeded
  but whose remark append failed is logged as Warning and **not** retried (the order is already
  out of the source state).
- Dry run: shipments are still queried for every order (full read load on Shoptet), only the
  two PATCH calls are skipped.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ShoptetOrders:DeliveredCompletionSourceStateIds` | `[70, 82]` (code default; not in appsettings) | States polled in normal mode |
| `ShoptetOrders:DeliveredCompletionTestSourceStateIds` | `[73]` (code default) | States polled when the test-source flag is on |
| `ShoptetOrders:CompletedStatusId` | `-3` (code default) | Target state |
| `FeatureManagement:is-delivered-order-completion-enabled` | `false` | Off = dry run (log only) |
| `FeatureManagement:is-delivered-order-completion-test-source-enabled` | `false` | On = poll test states instead of 70/82 |
| `Hangfire:SchedulerEnabled` | `false`; `true` in Production | Whether recurring jobs are scheduled at all |
| `ShoptetApi:BaseUrl`, `ShoptetApi:ApiToken` | secrets | Shoptet REST access (`Shoptet-Private-API-Token` header) |

Feature flags can be overridden at runtime in the admin UI (table `FeatureFlagOverrides`); the
current production value cannot be determined from the repo.

## Runtime facts
None.

## Known quirks
- Off by default: with the repo defaults the job runs every hour on Production but changes
  nothing — it only writes "[DRY RUN]" log lines. Whether the flag was ever switched on in
  production is not recorded in the repo.
- The source-state lists have **non-empty code defaults** and the .NET config binder *appends*
  configured array elements to an existing array instead of replacing it (memory note
  `gotcha_config_binder_appends_arrays`, verified 2026-09-21 on Binder 8.0.0). Configuring
  `DeliveredCompletionSourceStateIds` to `[90]` therefore polls `[70, 82, 90]` — states 70/82
  cannot be removed via configuration, only by changing the code default. Same for the test list.
- Remark append is read-modify-write without concurrency control: a staff edit of the internal
  remark between the GET and the PATCH is overwritten.
- No HTTP retry/resilience handler on the order and shipment Shoptet clients; transient failures
  simply wait for the next hourly run.
- Exception filter `ex is not OperationCanceledException` also lets HTTP timeouts
  (`TaskCanceledException`) escape the per-order catch and abort the whole run
  (memory note `gotcha_taskcanceled_is_operationcanceled`).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs` — the whole job
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs` — state ids and defaults
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — `ListOrdersByStatusAsync`, `UpdateStatusAsync`, `AppendEshopRemarkAsync`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs` — `HasDeliveredShipmentAsync`
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs` — flag descriptions/defaults
