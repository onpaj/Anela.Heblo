---
process: job-background-refresh-hydration
kind: job
module: background-refresh
summary: Loads every in-memory cache at startup tier by tier, reports readiness via /health/ready, then re-runs each refresh task on its own interval; admins can force a task or a whole tier.
owns:
  - backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/**
  - backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/**
  - backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs
  - backend/src/Anela.Heblo.Application/Common/BackgroundServicesReadyHealthCheck.cs
  - backend/src/Anela.Heblo.Xcc/Services/Dashboard/Tiles/BackgroundTaskStatusTile.cs
verified_at: "5e993f9e2"
related: [calc-margins, feed-stock-up, calc-bundle-sales-expansion]
---

# Background refresh: startup hydration and periodic reload

## Purpose
Answers "why is this number stale / empty / zero right after a deploy?" and "how often does it
update?". At startup every cache (catalogue stock, sales, prices, costs, margins, financial
overview) is loaded in dependency order so later tiers can build on earlier ones — costs need
sales and stock, margins need costs. Then each task reloads on its own interval. Administrators
watch and force tasks on **Background Tasky** (`/automation/background-tasks`). The task list is in
`module-background-refresh`.

## Trigger
- **Application start**, when `BackgroundServices:EnableHydration` is true (default; false in
  Development, Conductor and Test): hosted services `TierBasedHydrationOrchestrator`,
  `HydrationOrchestratorWrapper`, `BackgroundRefreshSchedulerService`.
- **Continuously** afterwards: one loop per enabled task, sleeping `RefreshInterval` between runs.
- **Manually**: `POST /api/backgroundrefresh/tasks/{taskId}/force-refresh` (one task) and
  `POST /api/backgroundrefresh/tiers/{tier}/run` (all enabled tasks of a tier), permission
  `Admin_Administration` write, from the Background Tasky page.

## Data flow
Module registration → `BackgroundRefreshTaskRegistry` → tier execution → owner's refresh method →
owner's cache. Nothing is persisted by this mechanism.

1. **Registration** (DI build): modules call `services.RegisterRefreshTask<TOwner>(methodName, (owner, ct) => ...)`.
   Task id = `<TOwner type name>.<methodName>`, e.g. `ICatalogRepository.RefreshSalesData`.
   The registry singleton reads each task's settings from
   `BackgroundRefresh:<Owner>:<Method>` (`InitialDelay`, `RefreshInterval`, `Enabled`,
   `HydrationTier`). A registered task with no section, or an unparsable `RefreshInterval`,
   throws when the registry is first built.
2. **Hydration** (`TierBasedHydrationOrchestrator`): take enabled tasks, group by `HydrationTier`,
   run tiers in ascending order. Inside a tier all tasks run **concurrently**; each waits its
   `InitialDelay` first, then runs in a fresh DI scope. A tier finishes when all its tasks finish.
3. **Readiness**: when every tier `<= BackgroundServices:ReadinessTier` has completed,
   `HydrationOrchestratorWrapper` reports ready and `/health/ready` (check
   `background-services-ready`) turns Healthy. Higher tiers keep loading in the background.
4. **Periodic loop** (`BackgroundRefreshSchedulerService`): waits until **all** tiers finish, then
   starts one loop per enabled task: run → wait `RefreshInterval` → run …. Errors are logged and the
   loop continues.
5. **Execution record**: every run (hydration, loop, manual) adds an entry to the in-memory history
   (`Running` → `Completed` / `Failed` with error message / `Cancelled`, start, end, duration,
   metadata `IsForceRefresh`). History keeps the newest 1 000 entries across all tasks.
6. **Reads**: `GET /api/backgroundrefresh/tasks`, `.../tasks/{taskId}/history?maxRecords=`,
   `.../history?maxRecords=`, `.../tasks/{taskId}/status` (permission `Admin_Administration` read);
   dashboard tile `backgroundtaskstatus`.

## Logic & formulas
- **Interval** is measured from the end of one run to the start of the next (`Task.Delay` after the
  run), so the real period is interval + run time; there is no cron and no fixed clock time.
- **Tier failure handling**:
  - optional tier (`> ReadinessTier`): an exception is logged as a warning and hydration continues;
  - required tier (`<= ReadinessTier`): the exception propagates; readiness is marked failed
    (`/health/ready` → Degraded with `FailureReason`), the periodic loop never starts, and the
    orchestrator's hosted service faults — with .NET 8's default
    `BackgroundServiceExceptionBehavior = StopHost` (not overridden anywhere) this stops the
    application. (Read from code, not observed.) Only exceptions that escape the task count:
    `RefreshSalesData` and `RefreshSetPartsData` catch failures and keep the stale cache, so an
    outage there shows as *Completed*; the other catalogue refreshes (history, stock, meta) let
    the exception escape.
- **ReadinessTier = 0** or no enabled task at/below it → ready immediately.
- **Manual tier run** runs the tier's enabled tasks **sequentially** in task-id order inside the
  HTTP request and stops at the first failure ("An unexpected error occurred during tier
  hydration"). Tier must be > 0.
- **Force refresh** runs the task synchronously inside the HTTP request; unknown id → 404 with the
  registry message. It works for disabled tasks too.
- **Tile**: completed = tasks whose latest history entry is `Completed`; a task never run (or
  currently running, failed, cancelled) counts as not completed.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `BackgroundServices:EnableHydration` | true (class); false in appsettings.Development / Conductor / Test | Registers hydration **and** the periodic scheduler |
| `BackgroundServices:ReadinessTier` | 1 (class); Production 2, Staging 1, Development 0 | Tiers that must finish before `/health/ready` is Healthy |
| `BackgroundRefresh:<Owner>:<Method>:RefreshInterval` | per task, see `module-background-refresh` | Required; `hh:mm:ss` |
| `BackgroundRefresh:<Owner>:<Method>:InitialDelay` | 0 unless set | Delay before the task's **hydration** run only |
| `BackgroundRefresh:<Owner>:<Method>:Enabled` | true if missing | false = skipped by hydration, loop and run-tier; force refresh still works |
| `BackgroundRefresh:<Owner>:<Method>:HydrationTier` | 1 if missing | Startup order and readiness grouping |
| `BackgroundRefresh:<Owner>:<Method>:Description` | — | Not read by code; documentation only |

## Runtime facts
- After every production restart (10 days up to 2026-09-21), `No sales history found` appeared
  40–90 s after start because tier-2 cost providers read a prematurely merged catalogue; fixed in
  PR #4254 — agent memory `gotcha_premature_merge_poisons_catalog_cache` — 2026-09-21.

## Known quirks
- **Every task runs twice at startup.** The periodic loop starts with an immediate run, so as soon as
  the last tier finishes each task runs again back-to-back with its hydration run (the code comment
  "No initial delay needed" assumes otherwise). Expect doubled Flexi/Shoptet load right after a
  deploy.
- **Scheduled runs are labelled as forced.** The loop calls `ForceRefreshAsync`, so its history
  entries carry `IsForceRefresh = true`, the same as a button click; only hydration runs carry
  false. History cannot tell manual from scheduled runs.
- **No overlap protection.** A forced run, a tier run and the periodic loop can execute the same
  task concurrently; nothing locks per task.
- **History is in memory only** (1 000 entries for all tasks, lost on restart). With 1-minute tasks
  (`ProcessPendingOperationsAsync`, `CompleteReceivedBoxesAsync`) the window is short: roughly
  the last 5–8 hours. Logs / App Insights are the long-term record.
- **The periodic loop waits for optional tiers too.** Production is "ready" after tier 2, but no
  cache is re-refreshed until tier 3 (margins, financial overview, the latter with a 5-minute
  initial delay) has also finished.
- **`InitialDelay` applies only to hydration**, not to the loop.
- **Validator not wired.** `RunHydrationTierRequestValidator` (tier > 0) exists but no
  `ValidationBehavior` is registered for it (validators are registered per module by hand); the
  endpoint still works because a tier with no tasks returns 404.
- **Same task id twice → last wins.** `AddOrUpdate` in the registry silently replaces an earlier
  registration with the same id.

## Code entry points
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshExtensions.cs` — DI wiring, `EnableHydration` gate, `RegisterRefreshTask` overloads
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/RefreshTaskConfiguration.cs` — reading `BackgroundRefresh:<Owner>:<Method>`
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshTaskRegistry.cs` — execution, history, force refresh
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/TierBasedHydrationOrchestrator.cs` — tiers, readiness, failure policy
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/HydrationOrchestratorWrapper.cs` — readiness reporting
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundRefresh/BackgroundRefreshSchedulerService.cs` — periodic loop
- `backend/src/Anela.Heblo.Application/Common/BackgroundServicesReadyHealthCheck.cs` — `/health/ready` contribution
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/` — force refresh, run tier, history, status
- `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs` — endpoints and permissions
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/Tiles/BackgroundTaskStatusTile.cs` — dashboard tile
