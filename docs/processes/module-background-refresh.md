---
process: module-background-refresh
kind: module
module: background-refresh
summary: Keeps Heblo's in-memory caches (catalog stock, sales, prices, costs, margins, financial overview) loaded at startup and refreshed every few minutes, and runs two minute-by-minute warehouse tasks, with an admin page to watch and force them.
owns: []
verified_at: "5e993f9e2"
related: [job-background-refresh-hydration, calc-margins, feed-stock-up, calc-bundle-sales-expansion]
---

# Background refresh (cache hydration)

## Purpose
Most numbers Heblo shows — catalogue stock, sales and purchase history, e-shop and ERP prices,
cost levels and margins, the financial overview — are **not** read live from Flexi or Shoptet on
each page view. They live in memory and are reloaded by **background refresh tasks** that run
in-process on fixed intervals (every 1 minute to every 4 hours). This module:

- loads every cache once at application start, in dependency order ("hydration tiers"), and tells
  Azure when the app is ready to take traffic (`/health/ready`);
- then reloads each cache on its own interval;
- gives administrators the **Background Tasky** page to see each task's last runs and force a reload.

So "how fresh is this number?" is usually answered by the task interval below, and "why is it
empty right after a deploy?" by the tier order. Unlike Hangfire jobs (`module-background-jobs`),
these tasks have no cron, no database row and no history across restarts.

## Users & screens
- **Background Tasky** — route `/automation/background-tasks`, sidebar *Administrace → Background
  Tasky*. Lists every registered task (id, interval, initial delay, tier, enabled), recent execution
  history (in memory, newest first), per-task **force refresh** and **run tier N**. Reading needs
  `Admin_Administration` read; force refresh / run tier need `Admin_Administration` write.
- **Dashboard tile "Stav background tasků"** (tile id `backgroundtaskstatus`, System, auto-shown) —
  "completed/total" = tasks whose latest run is Completed / all registered tasks.
- `/health/ready` — includes check `background-services-ready` (tag `ready`); used by the
  staging deploy and nightly E2E workflow to wait for the app.
- No MCP tool.

## Processes
- `job-background-refresh-hydration` — startup hydration by tier, readiness, the periodic loop,
  force refresh and run-tier; in-memory history. Runs at every start and continuously.
- The tasks themselves belong to their modules. Full list as of `5e993f9e2` (from
  `BackgroundRefresh` in `backend/src/Anela.Heblo.API/appsettings.json`; no environment overrides):

| Task id | Interval | Initial delay | Tier | Owner | What it refreshes |
|---|---|---|---|---|---|
| `ICatalogRepository.RefreshTransportData` | 5 min | 0 | 1 | Catalog | Stock in transport boxes |
| `ICatalogRepository.RefreshManufacturedData` | 5 min | 0 | 1 | Catalog | Manufacture warehouse (sklad výroby) stock |
| `ICatalogRepository.RefreshReserveData` | 5 min | 0 | 1 | Catalog | Reserved stock |
| `ICatalogRepository.RefreshOrderedData` | 5 min | 0 | 1 | Catalog | Ordered (purchase) stock |
| `ICatalogRepository.RefreshPlannedData` | 5 min | 0 | 1 | Catalog | Planned production quantities |
| `ICatalogRepository.RefreshEshopStockData` | 5 min | 0 | 1 | Catalog | Shoptet e-shop stock |
| `ICatalogRepository.RefreshStockTakingData` | 5 min | 0 | 1 | Catalog | Stock-taking records |
| `ICatalogRepository.RefreshErpStockData` | 10 min | 0 | 1 | Catalog | Flexi (ERP) stock levels |
| `ICatalogRepository.RefreshEshopPricesData` | 30 min | 0 | 1 | Catalog | Shoptet prices |
| `ICatalogRepository.RefreshSalesData` | 1 h | 0 | 1 | Catalog | Sales history |
| `ICatalogRepository.RefreshAttributesData` | 1 h | 0 | 1 | Catalog | Product attributes |
| `ICatalogRepository.RefreshPurchaseHistoryData` | 1 h | 0 | 1 | Catalog | Purchase history |
| `ICatalogRepository.RefreshManufactureHistoryData` | 1 h | 0 | 1 | Catalog | Manufacture history |
| `ICatalogRepository.RefreshConsumedHistoryData` | 1 h | 0 | 1 | Catalog | Material consumption history |
| `ICatalogRepository.RefreshLotsData` | 1 h | 0 | 1 | Catalog | Lots / expirations |
| `ICatalogRepository.RefreshErpPricesData` | 1 h | 0 | 1 | Catalog | Flexi prices |
| `ICatalogRepository.RefreshEshopUrlData` | 1 h | 0 | 1 | Catalog | Product URLs (Heureka XML feed) |
| `ICatalogRepository.RefreshManufactureDifficultySettingsData` | 1 h | 0 | 1 | Catalog | Manufacture difficulty settings |
| `ICostPoolService.RefreshCache` | 4 h | 0 | 1 | Shared/CostPools | Cost-pool spend totals (M1 VYROBA, M2 SKLAD+MARKETING, M3 rest) |
| `ICatalogRepository.RefreshSetPartsData` | 1 h | 0 | 2 | Catalog | Set / gift-package composition (needs tier-1 ERP stock) |
| `IMaterialCostProvider.RefreshCache` | 1 h | 0 | 2 | Catalog | Material cost cache (M0) |
| `IFlatManufactureCostProvider.RefreshCache` | 1 h | 0 | 2 | Catalog | Manufacturing cost cache (M1) |
| `ISalesCostProvider.RefreshCache` | 1 h | 0 | 2 | Catalog | Sales/marketing cost cache (M2) |
| `IOverheadCostProvider.RefreshCache` | 1 h | 0 | 2 | Catalog | Overhead cost cache (M3) |
| `IStockUpProcessingService.ProcessPendingOperationsAsync` | 1 min | 1 min | 2 | Catalog | **Writes**: submits pending stock-up operations to Shoptet (see `feed-stock-up`) |
| `ITransportBoxCompletionService.CompleteReceivedBoxesAsync` | 1 min | 1 min 30 s | 2 | Logistics | **Writes**: completes received transport boxes once their stock-ups finished |
| `ICatalogRepository.RefreshMarginData` | 2 h | 0 | 3 | Catalog | Product margins M0–M3 (see `calc-margins`) |
| `IFinancialAnalysisService.RefreshFinancialDataAsync` | 4 h | 5 min | 3 | FinancialOverview | Company financial overview |

  28 tasks: 19 in tier 1, 7 in tier 2, 2 in tier 3. All enabled.
- No plain CRUD.

## Data owned
- Nothing persisted. The registry (`BackgroundRefreshTaskRegistry`, singleton) holds the task
  definitions and an in-memory execution history capped at 1 000 entries, lost on restart.
- The caches themselves are owned by the modules in the table (mostly `CatalogCacheStore` /
  `IMemoryCache`).

## External systems
- None directly. Individual tasks read Flexi, Shoptet, the Heureka feed and the Heblo database, and
  the two tier-2 warehouse tasks write to Shoptet / Heblo — see their owners' docs.

## Dependencies
- Hosts tasks registered by Catalog, Logistics, FinancialOverview and Shared/CostPools via
  `RegisterRefreshTask<TOwner>(...)`.
- Read by: health checks (`BackgroundServicesReadyHealthCheck`), Dashboard (tile), the admin page.

## Known quirks
- **Two tasks are not caches.** `ProcessPendingOperationsAsync` (pushes stock to Shoptet) and
  `CompleteReceivedBoxesAsync` (closes transport boxes) are business writes running every minute
  inside this mechanism, not Hangfire jobs. They do not appear on the Recurring Jobs page and have
  no on/off switch except `Enabled` in appsettings.
- **Dead config**: `BackgroundRefresh:IManufactureCostCalculationService:Reload` and
  `BackgroundRefresh:ISalesCostCalculationService:Reload` are still in appsettings.json, marked
  "DEPRECATED" and `Enabled: false`; no code registers them.
- **Hydration off = no refresh at all.** `BackgroundServices:EnableHydration=false` (Development,
  Conductor, Test) skips startup loading *and* the periodic loop; tasks are still registered and
  a cache fills only when someone forces its task (page or
  `POST /api/backgroundrefresh/tasks/{taskId}/force-refresh`) or its owner loads lazily on read.
- **Readiness differs per environment**: production is ready after tier 2 (`ReadinessTier: 2`),
  staging after tier 1, development immediately. Margins and the financial overview (tier 3) are
  still loading for a while after production reports ready.
- **Premature catalogue merge (fixed)**: until PR #4254 a catalogue merge that ran before tier-1
  sources loaded was stamped valid for 4 h, so tier-2 `ISalesCostProvider` wrote M2 = 0 and tier-3
  margins baked it in after every restart (observed 2026-09-21, agent memory
  `gotcha_premature_merge_poisons_catalog_cache`). Every source the catalogue requires must stay a
  tier-1 task.
- **Feature doc gap**: there is no `docs/features` page for this mechanism; this doc and
  `job-background-refresh-hydration` are the reference.

## Code entry points
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshExtensions.cs` — DI, `RegisterRefreshTask`, `EnableHydration` gate
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshTaskRegistry.cs` — registry, execution, history
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/TierBasedHydrationOrchestrator.cs` — startup tiers, readiness
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshSchedulerService.cs` — periodic loop
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/RefreshTaskConfiguration.cs` — config parsing
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — catalog/cost/stock-up task registrations
- `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs` — admin endpoints
- `frontend/src/components/pages/automation/BackgroundTasks.tsx` — admin page
