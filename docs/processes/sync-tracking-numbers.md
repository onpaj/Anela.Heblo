---
process: sync-tracking-numbers
kind: sync
module: packaging
summary: Fills in carrier tracking numbers on Heblo package records that had none at scan time, by reading the order's latest active Shoptet shipment every 10 minutes and when the packing desk shows its done screen.
owns:
  - backend/src/Anela.Heblo.Application/Features/Packaging/Infrastructure/Jobs/FillTrackingNumbersJob.cs
  - backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumber/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumbers/**
verified_at: "5e993f9e2"
related:
  - flow-order-packing
  - calc-packing-statistics
---

# Tracking numbers for packed boxes

## Purpose
When an order is packed, Shoptet generates the carrier label (and with it the tracking number)
asynchronously, often a few seconds after Heblo created the shipment. Heblo therefore often stores
a box record (`Packages`) without a tracking number. This process fills it in later, so the shipment
list (Zásilky, `/baleni/zasilky`) shows the tracking number and the packing statistics can report
tracking coverage.

## Trigger
- Hangfire recurring job **`fill-tracking-numbers`** ("Fill Tracking Numbers", category Warehouse),
  cron `*/10 * * * *` (every 10 minutes), enabled by default; can be disabled in Recurring Jobs.
- On demand from the packing desk done screen:
  - `GET /api/packaging/orders/{orderCode}/tracking-number` (single-box view) — also writes.
  - `GET /api/packaging/orders/{orderCode}/tracking-numbers` (multi-box view) — read only.

## Data flow
**Job** (`FillTrackingNumbersJob`):
1. Skip if the job is disabled (`IRecurringJobStatusChecker`).
2. Read `public."Packages"` rows with `TrackingNumber IS NULL` and `CreatedAt` within the last
   3 days (`GetWithNullTrackingNumberAsync(3)`).
3. Group by order; for each order call Shoptet `GET /api/shipments?orderCode={code}` and take the
   **latest active shipment** (last in Shoptet's oldest-first list, ignoring statuses `canceled`,
   `cancel_requested`, `deleted`, `request_failed`) → its first non-empty package tracking number.
4. If found, write it to **every** null row of that order (`SetTrackingNumberAsync` per row).
   Shoptet errors or no tracking yet → leave the rows, retry next run.

**Single tracking number endpoint** (`GetOrderTrackingNumberHandler`): same Shoptet lookup as step 3;
if found, writes it to every row of the order whose `TrackingNumber` is null
(`SetTrackingNumberByOrderCodeAsync`) and returns it. Errors → returns null (desk falls back to
the box name).

**Per-box endpoint** (`GetOrderTrackingNumbersHandler`): reads the order's active labels, takes the
latest shipment's packages in order and returns their non-empty tracking numbers. No DB write.
Errors → empty list.

## Logic & formulas
- Match is by **order + latest active shipment**, not by box: carrier package names are not
  unique and change once the label is generated.
- Window is 3 days by `CreatedAt`; older rows without a tracking number are never retried.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Recurring job `fill-tracking-numbers` | enabled, `*/10 * * * *` | Toggle / schedule in Recurring Jobs (stored in DB, not appsettings) |

The 3-day window is a code constant (`FillTrackingNumbersJob.DaysBack`).

## Runtime facts
None

## Known quirks
- **Multi-box orders get one tracking number on every box.** Both writers copy the *first* tracking
  number of the latest shipment onto all null rows of the order, so boxes 2..N carry box 1's
  number in `Packages` (the Zásilky list). The done screen of a multi-box order uses the per-box
  endpoint and shows the correct numbers; the stored rows do not.
- Rows that already have a tracking number are never refreshed or verified against Shoptet.
- Rows of a deleted/cancelled shipment are not cleaned up; if the order has no active shipment
  anymore they simply stay null until they fall out of the 3-day window.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Packaging/Infrastructure/Jobs/FillTrackingNumbersJob.cs` — the job
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumber/GetOrderTrackingNumberHandler.cs` — on-demand fill
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetOrderTrackingNumbers/GetOrderTrackingNumbersHandler.cs` — per-box read
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs` — `GetLatestActiveTrackingNumberAsync`, active-status filter
