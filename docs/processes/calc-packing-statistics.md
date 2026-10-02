---
process: calc-packing-statistics
kind: calculation
module: packaging
summary: Derives packing-desk numbers — orders waiting in Shoptet, orders packed today per packer, and 30-day throughput, peak hours, carrier mix and tracking coverage — from Heblo's package records and live Shoptet order counts.
owns:
  - backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackingOrderCountSource.cs
  - backend/src/Anela.Heblo.Application/Features/Packaging/DashboardTiles/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackingDashboard/**
  - backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackingStatistics/**
  - backend/src/Anela.Heblo.Domain/Features/Packaging/PackingStatistics.cs
  - backend/src/Anela.Heblo.Domain/Features/Packaging/PackerPackingSummary.cs
verified_at: "5e993f9e2"
related:
  - flow-order-packing
  - sync-tracking-numbers
---

# Packing statistics and dashboard

## Purpose
Answers "how much is waiting to be packed, how much did we pack today and who packed it", and over
a longer window "how many orders/boxes per day, when are the peaks, which carriers, how many boxes
per order". Shown in three places:
- Balení home (`/baleni`) — today's counters (`GET /api/packaging/dashboard`).
- Main dashboard tile **"Stav balení"** (tile id `packingstats`, category Orders, shown by default,
  no extra permission) — same numbers.
- Statistics page (Statistiky, `/baleni/statistiky`) — `GET /api/packaging/statistics?fromDate=&toDate=`.

## Trigger
On demand. The Balení dashboard query refreshes every 60 s in the browser; statistics are cached in
the browser for 5 minutes. No background job, no stored result.

## Data flow
**Today's counters** (`GetPackingDashboardHandler`, `PackingStatsTile`):
1. "Today" = local calendar day of the server (container `TZ=Europe/Prague`).
2. `public."Packages"` rows with `PackedAt` in today → distinct (packer id, packer name, order code)
   → per packer: distinct orders; total: distinct orders.
3. Live Shoptet counts: `GET /api/orders?statusId={id}&page=1&itemsPerPage=50` → `paginator.totalCount`
   for `ShoptetOrders:PackingStateId` (26 "Balí se" → *being packed*) and `ProcessingStateId`
   (-2 "Vyřizuje se" → *incoming*). A Shoptet failure leaves both null; the rest still shows.

**Statistics** (`GetPackingStatisticsHandler` → `PackageRepository.GetPackingStatisticsAsync`):
1. Window: local days `fromDate..toDate` inclusive; defaults to the last 30 days ending today.
   `fromDate > toDate` → `InvalidDateRange`.
2. Load the window's `Packages` rows and aggregate in memory, all day/hour buckets in local time.

## Logic & formulas
Units: one `Packages` row = one box; an order = distinct `OrderCode`.

| Figure | Definition |
|---|---|
| Total packages / orders | row count / distinct order codes in window |
| Average packages per order | packages ÷ orders, 2 decimals |
| Tracking coverage % | rows with `TrackingNumber` ÷ all rows × 100, 1 decimal |
| Daily throughput | per local day: distinct orders, rows |
| Hour heatmap | rows per (ISO weekday 1=Mon..7=Sun, local hour) |
| By packer | only rows with `PackedByUserId`; distinct orders and rows per packer |
| By carrier | rows per `ShippingProviderCode` (name = first non-null stored name) |
| Packages per order | orders by distinct `PackageNumber` count, bucket 3 = "3 or more" |
| Busiest day / hour | max of daily throughput / heatmap by package count |
| Packer attribution since | earliest local day with an attributed row in the window |

Today's per-packer counts group by stored (id, name), so unattributed rows appear as their e-mail
or "Neznámý"; the statistics' *by packer* list omits them entirely.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ShoptetOrders:PackingStateId` | 26 | Shoptet state counted as "being packed" |
| `ShoptetOrders:ProcessingStateId` | -2 | Shoptet state counted as "being processed" (incoming) |

Default window (30 days) is a code constant in `GetPackingStatisticsHandler`.

## Runtime facts
- Package history starts 2026-06-09 (first `PackedAt` in prod), so no year-on-year comparison is
  possible before June 2027; `public."Packages"` is readable by `metabase_ro` through the legacy
  blanket grant but has no curated reporting view — prod Heblo_V3 check — 2026-09-24.

## Known quirks
- Numbers count **boxes recorded in Heblo**, not Shoptet orders moved to Zabaleno. Orders packed
  outside the desk are missing; Reset rewrites `PackedAt` to the reset time and drops packer
  attribution; duplicate rows from the re-scan backfill inflate box counts (see `flow-order-packing`).
- `PackedAt` is the moment the row was written (shipment created), not the moment the order was
  marked packed in Shoptet.
- Tracking coverage is understated for the last few minutes (tracking numbers arrive via
  `sync-tracking-numbers`) and permanently for rows older than its 3-day window.
- Rows written before packer selection existed have no `PackedByUserId`; "Packer attribution since"
  tells the page from when per-packer numbers are complete.

## Code entry points
- `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs` — `GetPackedTodayByPackerAsync`, `GetPackingStatisticsAsync` (all formulas)
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackingStatistics/GetPackingStatisticsHandler.cs` — window, summary
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackingDashboard/GetPackingDashboardHandler.cs` — today's counters
- `backend/src/Anela.Heblo.Application/Features/Packaging/DashboardTiles/PackingStatsTile.cs` — dashboard tile
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` — Shoptet order counts
