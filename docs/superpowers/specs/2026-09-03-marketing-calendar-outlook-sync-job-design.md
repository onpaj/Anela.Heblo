# Marketing Calendar — automatic Outlook → Heblo sync job

**Date:** 2026-09-03
**Status:** approved design, pending implementation plan

## Problem

The marketing calendar in Heblo is populated from the Outlook group calendar
only when a user clicks **Import from Outlook** (`ImportFromOutlookModal` →
`POST /api/marketing-calendar/import` → `ImportFromOutlookHandler`). There is
no recurring job, so Heblo drifts from Outlook between clicks. Worse, the
import is create/update only: an event deleted in Outlook/Teams is never
removed from Heblo, so stale actions accumulate.

## Decisions

| Question | Decision |
|---|---|
| Source of truth | **Outlook is master.** Heblo mirrors it; Heblo edits to Outlook-backed actions are overwritten on the next run. `MarketingCalendar:PushEnabled` stays off. |
| Sync window | `[now − 30 days, now + 12 months]` |
| Cadence | Hourly (`0 * * * *`, Europe/Prague) |
| Architecture | Extract the import loop into an application service used by both the manual handler and the new job (approach B). |

Actions created directly in Heblo (`OutlookEventId == null`) are never touched
by the sync. Heblo-only data on Outlook-backed actions (product associations,
folder links) is preserved because the mapper only writes title, description,
dates, and action type.

## Components

### `MarketingCalendarSyncService` (new, `Features/Marketing/Services/`)

```
Task<MarketingSyncResult> SyncAsync(
    DateTime fromUtc, DateTime toUtc, SyncActor actor, bool dryRun, CancellationToken ct)
```

Dependencies: `IMarketingActionRepository`, `IOutlookCalendarSync`,
`IMarketingCategoryMapper`, `ILogger`. Registered scoped in `MarketingModule`.

Behaviour is today's `ImportFromOutlookHandler.Handle` body moved verbatim
(create / update / skip, unmapped-category accumulation, single
`SaveChangesAsync` per run, batch-failure reported as all-failed) plus the
reconciliation step below.

`SyncActor` is a record `(string UserId, string? Username)` with
`SyncActor.System = ("system", "Outlook sync")` and
`SyncActor.FromUser(CurrentUser)`. `OutlookEventImportMapper` takes a
`SyncActor` instead of `CurrentUser`.

`MarketingSyncResult` mirrors `ImportFromOutlookResponse` (Created, Updated,
Skipped, Failed, Items, UnmappedCategories) and adds `Deleted`.
`ImportStatus` gains `Deleted` and `WouldDelete`.

### Reconciliation (inside `SyncAsync`, after the create/update loop)

1. Load non-deleted actions with a non-null `OutlookEventId` whose `StartDate`
   is within `[fromUtc, toUtc]` — new repository method
   `GetSyncedInWindowAsync(from, to)`.
2. Orphans = those whose `OutlookEventId` is not in the Graph `calendarView`
   response.
3. For each orphan, call `IOutlookCalendarSync.GetEventAsync(id)`:
   - `null` (404) → `action.SoftDelete(actor.UserId, actor.Username, utcNow)`;
     counted as `Deleted` (`WouldDelete` in dry-run, no write).
   - event returned → it was moved outside the window; apply it as a normal
     update (`ApplyChanges`) — never a false delete.
   - throws → counted as `Failed` for that item; the run continues.

Every deletion is individually confirmed, so no "empty response wipes the
calendar" failure mode exists and no threshold guard is needed.

### Restore on reappearance

`GetByOutlookEventIdsAsync` already includes soft-deleted rows
(`IgnoreQueryFilters`) with the deliberate intent "a deleted import must not
be re-created". That intent is kept for **user** deletions. For rows the sync
itself deleted (`DeletedByUserId == SyncActor.System.UserId`), a reappearing
event restores the row: new domain method
`MarketingAction.Restore(userId, username, utcNow)` clears `IsDeleted`,
`DeletedAt`, `DeletedBy*` and bumps `ModifiedAt/By`. Counted as `Updated`.

### `IOutlookCalendarSync.GetEventAsync` (new)

`Task<OutlookEventDto?> GetEventAsync(string outlookEventId, CancellationToken ct)`
— `GET {CalendarEventsBaseUrl}/{id}?$select=id,subject,body,start,end,categories`
with the **app** token (same as `ListEventsAsync`). Returns `null` on 404,
throws `OutlookCalendarSyncException` otherwise. `NoOpOutlookCalendarSync`
returns `null`.

### `MarketingCalendarSyncJob` (new, `Features/Marketing/Infrastructure/Jobs/`)

`IRecurringJob`, auto-discovered by the assembly scan. Modelled on
`PlaudPollingJob`.

- `JobName = "marketing-calendar-sync"`, `DisplayName = "Marketing — sync
  Outlook calendar"`, `CronExpression = "0 * * * *"`, `DefaultIsEnabled = true`.
- `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]`.
- Skips (info log) when `IRecurringJobStatusChecker` says disabled, or when
  `MarketingCalendarOptions.GroupId` is blank (no Microsoft 365 configured).
- Window constants: `PastDays = 30`, `FutureMonths = 12` (not configurable —
  YAGNI).
- Calls `SyncAsync(from, to, SyncActor.System, dryRun: false)`; logs counts
  and unmapped categories. Graph/token exceptions propagate so the Hangfire
  run fails visibly.

### `ImportFromOutlookHandler` (slimmed)

Auth check (unchanged) → `SyncAsync(request.FromUtc, request.ToUtc,
SyncActor.FromUser(currentUser), request.DryRun)` → map `MarketingSyncResult`
to `ImportFromOutlookResponse` (adds `Deleted`).

### Frontend

`ImportFromOutlookModal` shows a **Smazáno** (`deleted`) count alongside
created/skipped/failed; `WouldDelete` rows render in the dry-run list. Strings
added to `i18n.ts`. Regenerate the TypeScript client (`Deleted` field). No
other UI changes.

## Error handling

- Per-event failures (mapping, orphan confirmation) are logged at Warning and
  reported as `Failed` items; the run continues.
- Batch `SaveChangesAsync` failure reports every staged create/update/delete
  as `Failed` and persists nothing (existing behaviour, now also covering
  deletes).
- Job-level Graph failures (token, `ListEvents` non-2xx) throw → Hangfire
  marks the run failed; next hourly run retries naturally.

## Testing

- `MarketingCalendarSyncServiceTests` (new): create / update / skip ported
  from handler tests; orphan + 404 → soft-deleted with system actor;
  orphan + found → updated, not deleted; orphan + throw → `Failed`;
  sync-deleted row reappears → restored; user-deleted row reappears → stays
  deleted; dry-run reports `WouldDelete` and writes nothing; batch-save
  failure → all `Failed`. Fake `IOutlookCalendarSync`, no Graph.
- `ImportFromOutlookHandlerTests`: auth gate + delegation + response mapping.
- `MarketingCalendarSyncJobTests` (new): disabled → skip; blank `GroupId` →
  skip; enabled → service called with expected window and `SyncActor.System`.
- `OutlookCalendarSyncServiceTests`: `GetEventAsync` 404 → null; 200 → dto;
  500 → throws.
- `MarketingActionTests`: `Restore` clears deletion fields.
- FE: `ImportFromOutlookModal.test.tsx` asserts the deleted count renders.
- No E2E: no new user flow; E2E runs against deployed staging.

## Out of scope

Enabling `PushEnabled`, two-way conflict resolution, a "last synced"
indicator in the UI, configurable window/cadence.
