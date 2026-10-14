---
process: job-create-missing-invoices
kind: job
module: shoptet-orders
summary: Daily job that issues a Shoptet invoice for every completed ("Vyřízena", -3) order created since 2026-01-01 whose total is non-zero and that has no invoice yet; dry-run unless a feature flag is on.
owns:
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CreateMissingInvoicesJob.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.Invoices.cs
verified_at: "98ab89141"
related:
  - job-complete-delivered-orders
  - feed-issued-invoices
---

# Create missing invoices

## Purpose
Every e-shop order with a real price must end up with an invoice. Orders sometimes reach
"Vyřízena" without one; staff used to find them with the Shoptet admin filter *Přehled objednávek →
Vyřízena → Doklady: bez faktury* and click "create invoice" by hand. This job does that once a day.
Orders with a zero total (gifts, replacements, internal orders) are ignored — they need no invoice.

The result is visible only in Shoptet: the order gets an invoice, dated the day the job runs
(date of issue and DUZP = today — the same as a manual click; agreed with the owner 2026-10-14).
The new invoice then reaches Flexi through the normal issued-invoice import (`feed-issued-invoices`).
Heblo stores nothing.

The real side effect is switched **off by default**: until the feature flag
`is-missing-invoice-creation-enabled` is on, the job only logs which orders it *would* invoice.

## Trigger
- Hangfire recurring job `create-missing-invoices`, cron `0 3 * * *` (03:00 Europe/Prague, outside
  warehouse hours so it does not compete with packing for the shared Shoptet rate budget),
  `DefaultIsEnabled = true`, category Warehouse, `[AutomaticRetry(Attempts = 0)]` — the next night's
  run is the retry — and `[DisableConcurrentExecution]` so a manual trigger cannot overlap the nightly run. Can be disabled or triggered manually from the Recurring Jobs admin page.
- Recurring jobs are scheduled only where `Hangfire:SchedulerEnabled` is true (Production only).

## Data flow
1. Read flag `is-missing-invoice-creation-enabled` — off ⇒ dry run.
2. `GET /api/orders?statusId=-3&creationTimeFrom=<MissingInvoiceOrdersCreatedFrom>&page=n&itemsPerPage=50`,
   all pages; keep code + header `price.withVat`.
3. `GET /api/invoices?creationTimeFrom=<start − 31 days>&page=n&itemsPerPage=1000`, all pages;
   collect every non-null `orderCode` (valid **and** invalid/cancelled invoices). The number of
   invoices read must equal page 1's `paginator.totalCount`, else the run aborts.
4. Work list = orders with `price.withVat > 0` whose code is not in the invoiced set.
5. Log one summary line: scanned / zero total / already invoiced / missing + the missing codes.
6. Flag on: take at most `MissingInvoiceMaxPerRun` (300) missing orders (the rest wait for the next
   night) and for each, sequentially and 500 ms apart: `GET /api/invoices?orderCode={code}` — any
   hit ⇒ skip (invoiced since the bulk listing) — else `POST /api/orders/{code}/invoice` with body
   `{"data":{}}`. Log each creation and a final created/skipped/failed count.

Target: Shoptet invoices only. No Heblo table is written.

## Logic & formulas
- "Needs an invoice" = state -3 **and** created ≥ start date **and** `price.withVat > 0` **and** no
  invoice (any validity) references the order. A null price counts as zero.
- Idempotent: an invoiced order appears in the invoiced set next run and is skipped.
- The invoice list starts 31 days before the order start date as a margin against a hand-backdated
  invoice — an invoice cannot predate its order otherwise.
- Duplicate guards (invoices cannot be deleted via the API): an **empty invoiced set while priced
  orders exist aborts the run** (a 200 with an empty/malformed body would otherwise look like
  "nothing is invoiced"); a null listing body throws; a listing that returns fewer invoices than its
  paginator announced throws (a partial set would make older orders look uninvoiced); every order
  is re-checked with `GET /api/invoices?orderCode=` right before its POST; at most 300 invoices per
  run. If the `orderCode` filter were ever ignored the re-check answers "invoiced" for all orders,
  so the run creates nothing rather than duplicates (visible as all-skipped in the summary).
- Error isolation: a failing invoice listing **aborts the run** (otherwise every order would look
  uninvoiced and be invoiced twice). A failing order listing aborts too. A failing re-check or `POST`
  is logged (Warning, with Shoptet's error text) and retried the next night; the loop goes on, but
  **5 consecutive failures abort the run** (401/403/429 or an outage is systemic), and any failure
  makes the Hangfire run end as Failed after the summary line. HTTP timeouts are per-order
  failures; only cancellation of the job's own token stops the loop.
  `MissingInvoiceMaxPerRun ≤ 0` fails the run instead of silently creating nothing.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ShoptetOrders:MissingInvoiceOrdersCreatedFrom` | `2026-01-01T00:00:00+01:00` (code default) | Oldest order creation time considered |
| `ShoptetOrders:MissingInvoiceCreationDelayMs` | `500` (code default) | Pause between two orders, each a re-check GET + POST (~4 req/s budget, `shoptet-api.md` §3.14) |
| `ShoptetOrders:MissingInvoiceMaxPerRun` | `300` (code default) | Max invoices created per run |
| `ShoptetOrders:CompletedStatusId` | `-3` (code default) | Source state (shared with `job-complete-delivered-orders`) |
| `FeatureManagement:is-missing-invoice-creation-enabled` | `false` | Off = dry run (log only) |
| `Hangfire:SchedulerEnabled` | `false`; `true` in Production | Whether recurring jobs are scheduled at all |

## Runtime facts
None yet. Rollout plan: merge with the flag off, compare the first night's dry-run list with the
Shoptet admin filter — **broken down by order source**: in-store/market orders (`in_store`, store
ids 10/25/1000, "Trhy", `shoptet-api.md`) may deliberately have no invoice and are not excluded —
then switch the flag on in the admin UI, ideally with `MissingInvoiceMaxPerRun` set low (e.g. 5)
for the first live night.

## Known quirks
- `POST /api/orders/{code}/invoice` was verified on the test store for an order in -2 only, never
  on production or on a -3 order. The first live run is the real verification.
- The response of a POST for an order that already has an invoice is unknown; it is logged as a
  failure (Warning) and the order drops out of the work list once the invoice listing sees it.
- A POST that times out may still have created the invoice on Shoptet's side. It is counted as
  failed; the next night's invoice listing sees the invoice and skips the order.
- Volume: the order listing walks every -3 order since the start date (50 per page), so the run
  grows through the year — ~300 requests per 15,000 orders.
- Invoices are dated the run day, not the order date: an order completed in April and invoiced in
  October gets an October DUZP.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CreateMissingInvoicesJob.cs` — the job
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.Invoices.cs` — order listing with price, invoiced order codes, invoice creation
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersSettings.cs` — start date, delay
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs` — flag
