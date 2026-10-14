---
process: calc-packing-material-consumption
kind: calculation
module: packing-materials
summary: Every morning estimates how much of each packing material (boxes, filler, tape…) yesterday's invoiced orders used, subtracts it from the tracked stock and records one consumption row per material and invoice.
owns:
  - backend/src/Anela.Heblo.Application/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJob.cs
  - backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/**
  - backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/**
  - backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/IInvoiceConsumptionSource.cs
  - backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/InvoiceConsumptionHeader.cs
  - backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapter.cs
  - backend/src/Anela.Heblo.Domain/Features/PackingMaterials/PackingMaterialConsumption.cs
  - backend/src/Anela.Heblo.Domain/Features/PackingMaterials/PackingMaterialDailyRun.cs
  - backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialConsumptionConfiguration.cs
  - backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialDailyRunConfiguration.cs
verified_at: "5e993f9e2"
related: []
---

# Daily packing-material consumption (Odečíst spotřebu)

## Purpose
Anela does not count boxes, filler, tape and other packing consumables every day. Instead
Heblo **estimates** what was used from how many orders were invoiced, deducts it from the
stock kept on page **Sledování materiálů** (`/logistics/packing-materials`) and shows how many
days the remaining stock will last. Staff correct the estimate by entering a real count
("Upravit množství") whenever they stock-take or receive goods.

Results are read on the same page: the materials grid (current quantity, forecast days),
the material detail (60-day log + chart) and tab **Historie spotřeby** (per-invoice consumption
rows and quantity changes). Nothing is sent to Flexi, Shoptet or any other system — this stock
exists only in Heblo.

## Trigger
- Hangfire recurring job **`daily-consumption-calculation`**, cron `0 6 * * *`
  (06:00 Europe/Prague), enabled by default, category Catalog in Recurring Jobs. It processes
  **yesterday** (`DateTime.Today − 1`; the container runs with `TZ=Europe/Prague`). It runs at
  06:00 so that the nightly invoice imports `daily-invoice-import-eur` (04:00) and
  `daily-invoice-import-czk` (04:15) have already loaded yesterday's invoices.
  If the job is disabled in Recurring Jobs it logs and does nothing.
- Manually: button **Odečíst spotřebu** → modal with a date picker (default yesterday) →
  `POST /api/packing-materials/process-daily-consumption` `{ "processingDate": "yyyy-MM-dd" }`
  (needs `Warehouse_Logistics` Write). Any past or future date can be chosen.

Each date can be processed **once**; later attempts (job or manual) return
"Daily consumption for {date} was already processed" and change nothing.

## Data flow
1. **Already done?** `PackingMaterialDailyRuns` has a row for the date → stop.
2. **Read materials**: all rows of `public."PackingMaterials"` (with allocations, tracked).
3. **Read invoices** through `IInvoiceConsumptionSource` (implemented by the Invoices module,
   `InvoiceConsumptionSourceAdapter`): every row of `public."IssuedInvoices"` whose
   `InvoiceDate` is within the date (00:00:00 – 23:59:59.9999999), projected to
   `(Id, ItemsCount)`. `InvoiceDate` is the Shoptet invoice creation time; `ItemsCount` is the
   number of item **rows** on the Shoptet invoice (AutoMapper flattens `Items.Count`), set by
   the invoice import (Invoices module, `feed-issued-invoices`). All currencies, synced to
   Flexi or not.
4. **Build fact rows** per material (see formulas) → keep only materials whose total > 0.
5. **Decrement stock** for each such material:
   `CurrentQuantity = max(0, CurrentQuantity − total)` and add a `PackingMaterialLogs` row
   (`LogType = AutomaticConsumption`, `Date` = processed date, no user).
6. **Save #1** — insert the `PackingMaterialDailyRuns` row (`Date`, `ProcessedAt` UTC,
   `MaterialsProcessed`). This `SaveChanges` also commits the quantity changes and logs from
   step 5. A unique-index violation on `IX_PackingMaterialDailyRuns_Date` (a parallel run won)
   is caught → skip, nothing written.
7. **Save #2** — insert all fact rows into `public."PackingMaterialConsumptions"`.

## Logic & formulas
Each material has a `ConsumptionType` and a `ConsumptionRate` (decimal(18,6), any unit the
user chose — pieces, metres…):

| Type (UI) | Rows written for the day | Amount per row |
|---|---|---|
| `PerOrder` (za zakázku) | one per invoice, `InvoiceId` set | `ConsumptionRate` |
| `PerProduct` (za produkt) | one per invoice with `ItemsCount > 0`, `InvoiceId` set | `ConsumptionRate × ItemsCount` |
| `PerDay` (za den) | one, no `InvoiceId` | `ConsumptionRate` |

- Day total per material = sum of its rows. A material with total 0 (rate 0, or no invoices
  for invoice-based types) gets no rows, no log and no stock change.
- `PerDay` materials are deducted every calendar day that is processed, weekends and holidays
  included, even when there were no invoices.
- "Order" means **invoice**: one Shoptet invoice = one order. "Product" means **invoice row**,
  not pieces: 3 × the same product is 1 row; shipping and payment rows count as rows too.
- `ProductCode` / `ProductQuantity` on consumption rows are never filled, and the
  per-product allocations (`PackingMaterialAllocations`) are **not used** by the calculation.
- No rounding; stock never goes below 0 (the clamped part is lost, the consumption rows still
  show the full amount).
- Response: `Success=true, MaterialsProcessed=n`; when n = 0 the message is
  "No invoices found for {date} — no materials were updated" (also shown when invoices exist
  but every rate is 0).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Recurring job `daily-consumption-calculation` | `0 6 * * *`, Europe/Prague, enabled | Schedule and on/off switch, editable in Recurring Jobs (DB `RecurringJobConfigurations`) |
| Material `ConsumptionType` / `ConsumptionRate` | per row, set on the page | The only tuning; edited via "Editovat materiál" |

No appsettings keys.

## Runtime facts
None.

## Known quirks
- **A processed day is locked even if it was processed too early.** If yesterday's invoices
  were not imported by 06:00 (import failed, Hangfire retry, Shoptet outage) the job still
  records the day with only `PerDay` consumption; re-running later returns "already
  processed". The only fix is deleting that `PackingMaterialDailyRuns` row (and any rows
  written for it) in the DB, then re-running from the modal.
- **Missed days are not caught up.** The job processes only yesterday; days when it was
  disabled or failed for good must be run one by one from the modal.
- **Partial-success window.** Quantities, logs and the daily run commit in save #1; the
  consumption rows in save #2. If save #2 fails the stock is already decremented and the day
  locked, but tab Historie spotřeby shows no consumption rows for it (comment in
  `ConsumptionCalculationService`).
- **Allocations are dead weight.** `PackingMaterialAllocations` (material × product code ×
  amount per unit, API `/{id}/allocations`) were built for per-product attribution in #1029,
  which settled for invoice headers only ("Option B": invoice lines are not stored in Heblo).
  No UI exposes them and the calculation ignores them; the breakdown
  `GET /api/packing-materials/consumption?groupBy=Product` therefore always returns no groups
  and the product-code filter in Historie spotřeby never matches.
- **`ItemsCount` is rows, not pieces** (see formulas), so `PerProduct` under-counts
  multi-piece rows and over-counts shipping/payment rows.
- **Manual run default date is UTC-based.** The modal's "yesterday" comes from
  `toISOString()`, so between midnight and 01:00/02:00 Prague time it pre-selects the day
  before yesterday.
- **Hangfire retries a failed run** (default retry policy, no `[AutomaticRetry]` on the job);
  safe because of the once-per-date guard, except in the partial-success case above.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJob.cs` — job id, cron, "yesterday"
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs` — formulas, decrement, save order
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs` — response messages
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapter.cs` — invoice source
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — `GetHeadersByDateAsync` date window
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs` — `AddDailyRunAsync` duplicate handling
- `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/PackingMaterial.cs` — `UpdateQuantity` (writes the log)
- `frontend/src/components/packing-materials/modals/ProcessDailyConsumptionModal.tsx` — manual trigger
