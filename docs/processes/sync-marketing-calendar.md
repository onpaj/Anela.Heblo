---
process: sync-marketing-calendar
kind: sync
module: marketing
summary: Mirrors the Outlook marketing group calendar into Heblo marketing actions (create, update, soft-delete) every hour and on demand from the "Import z Outlooku" button; Outlook is the source of truth.
owns:
  - backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/Jobs/MarketingCalendarSyncJob.cs
  - backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/OutlookEventDto.cs
  - backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs
  - backend/src/Anela.Heblo.Application/Features/Marketing/Services/IMarketingCalendarSyncService.cs
  - backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs
  - backend/src/Anela.Heblo.Application/Features/Marketing/Services/*CategoryMapper.cs
  - backend/src/Anela.Heblo.Application/Features/Marketing/Services/SyncActor.cs
  - backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/**
  - backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/ImportFromOutlookRequest.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs
  - backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs
verified_at: "5e993f9e2"
related:
  - feed-marketing-action-to-outlook
---

# Marketing calendar sync (Outlook → Heblo)

## Purpose
The marketing team plans its actions (posts, blog articles, newsletters, PR, events, meetings) in a
shared **Outlook group calendar**. Heblo shows the same plan on the marketing calendar page
(Marketingový kalendář, `/marketing/calendar`) where actions can additionally be tagged with
products and asset folders. This sync keeps Heblo's copy (`MarketingActions`) in step with Outlook:
new Outlook events appear in Heblo, edits in Outlook overwrite the Heblo copy, events deleted in
Outlook are hidden in Heblo. **Outlook always wins** — there is no conflict detection.

Actions created in Heblo that never reached Outlook (no `OutlookEventId`) are never touched by
this sync. The opposite direction (Heblo edits pushed to Outlook) is `feed-marketing-action-to-outlook`.

## Trigger
- **Hangfire** `marketing-calendar-sync`, cron `0 * * * *` (every hour on the hour, Europe/Prague),
  enabled by default, category Marketing. Window: **30 days back → 12 months ahead** of now (UTC).
  `[DisableConcurrentExecution(600)]` (overlapping runs would race on the unique Outlook-id index)
  and `[AutomaticRetry(Attempts = 0)]` — a failed run is not retried, the next hour runs anyway.
  Skips (logs and returns) when disabled in Recurring Jobs or when `MarketingCalendar:GroupId` is empty.
- **On demand**: button "Import z Outlooku" on `/marketing/calendar` →
  `POST /api/MarketingCalendar/import-from-outlook` (`{fromUtc, toUtc, dryRun}`, needs
  Marketingový kalendář **write**). The user picks the date range; "Jen simulace (dry run)" reports
  what would change without writing. Same service, but stamped with the clicking user instead of
  "Outlook sync".

## Data flow
1. **List events** — Microsoft Graph
   `GET /v1.0/groups/{GroupId}/calendarView?startDateTime=…&endDateTime=…&$select=id,subject,body,start,end,isAllDay,categories`
   with header `Prefer: outlook.timezone="UTC"`, following `@odata.nextLink` pages. Authenticated
   with an **app-only** token (`https://graph.microsoft.com/.default`), so it works from Hangfire
   with no user signed in.
2. **Match** event ids against `public."MarketingActions"."OutlookEventId"` (case-insensitive),
   **including soft-deleted rows**.
3. **Per event** (errors are caught per event and reported as Failed; the run continues):
   - no matching row → **create** a new action (`CreatedByUserId` = actor);
   - matching row with any difference (title, description, start, end, all-day flag, action
     type) → **update** it; identical → **skipped**;
   - matching row that was soft-deleted **by the sync** (`DeletedByUserId = "system"`) →
     **restore** and update. A row deleted by a person in Heblo stays hidden, though its fields
     are still overwritten if the Outlook event changed.
4. **Orphans** — non-deleted actions with an `OutlookEventId` whose range overlaps the window
   (`StartDate <= to` and `COALESCE(EndDate, StartDate) >= from`) but whose event was not
   returned in step 1 are checked one by one with
   `GET /v1.0/groups/{GroupId}/calendar/events/{id}`: **404 → soft-delete** (attributed to
   `system` / "Outlook sync", whoever triggered the run); found → the event moved out of the
   window, update it normally.
5. **Persist** — all creates/updates/deletes are staged and saved in **one** `SaveChanges`.
   If that save fails, nothing is written, every staged item is reported Failed and the
   entities are detached.
6. **Result** — counts Created / Updated / Deleted / Skipped / Failed, per-item list and the
   set of unmapped Outlook categories. The job only logs them (warning for unmapped categories);
   the import dialog shows them (`UnmappedCategoriesPanel`).

Product associations (`MarketingActionProducts`) and folder links (`MarketingActionFolderLinks`)
are Heblo-only and are never changed by the sync.

## Logic & formulas
| Heblo field | From Outlook | Rule |
|---|---|---|
| `Title` | `subject` | cut to 200 chars, trimmed |
| `Description` | `body.content` | HTML stripped (script/style removed, tags removed, entities decoded, whitespace collapsed), cut to 5000 chars; empty → null |
| `StartDate` | `start.dateTime` | read as UTC (pinned by the `Prefer` header) |
| `EndDate` | `end.dateTime` | all-day: Graph's end is **exclusive**, Heblo's is **inclusive** → end − 1 day (clamped to start if malformed). Timed: end = start → null (a point in time), else end as is |
| `IsAllDay` | `isAllDay` | copied |
| `ActionType` | `categories` | first category found in `MarketingCalendar:CategoryMappings` (case-insensitive); none matched or no category → **SocialMedia** (Sociální sítě) and the unmatched names are reported as unmapped |
| `OutlookSyncStatus` | — | `Synced`, `OutlookSyncedAt` = run time |

Repo default category mapping (Outlook colour category → Heblo type / Czech label):

| Outlook category | ActionType | UI label |
|---|---|---|
| Yellow Category | SocialMedia | Sociální sítě |
| Green Category | Blog | Blog |
| Purple Category | Newsletter | Newsletter |
| Orange Category | PR | PR |
| Red Category | Event | Událost |
| Teal Category | Meeting | Meeting |

Mapping changes are picked up without restart (`IOptionsMonitor`). A blank mapping key fails
startup validation.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `MarketingCalendar:GroupId` | `""` | Microsoft 365 group whose calendar is mirrored; empty → job skips, manual import fails at Graph |
| `MarketingCalendar:CategoryMappings` | 6 colour categories (table above) | Outlook category name → `MarketingActionType` |
| Recurring job `marketing-calendar-sync` | `0 * * * *`, enabled | Toggle/cron in Recurring Jobs (`RecurringJobConfigurations`) |
| `UseMockAuth` / `BypassJwtValidation` | — | when either is true, `NoOpOutlookCalendarSync` is used: listing returns nothing and orphan look-ups throw, so nothing is created or deleted |

Window constants (`MarketingCalendarSyncJob.PastDays = 30`, `FutureMonths = 12`) are code, not config.

## Runtime facts
- About 15 imported all-day actions in prod were one day too long before PR #4224 fixed the
  exclusive/inclusive end conversion; they self-heal only inside the sync window — agent memory
  `gotcha_graph_allday_end_is_exclusive` — Sept 2026.

## Known quirks
- **Outlook overwrites Heblo edits.** With `PushEnabled` off, editing an imported action in Heblo
  changes only Heblo, and the next hourly run sets title/dates/type back to Outlook's values.
- **Unmapped or missing categories become SocialMedia**, silently in the calendar (only a log
  warning / the import dialog panel names them).
- **Deleting in Heblo hides an action for good** (unless the sync itself deleted it); a later
  Outlook change still rewrites its fields but it stays hidden.
- **Orphan check costs one Graph GET per orphan** — that is why a run can outlast an hour and
  why concurrent runs are blocked. An orphan look-up error (non-404) reports the action as
  Failed and leaves it untouched; it is never treated as a deletion.
- **One bad row fails the whole batch**: a DB error (e.g. on the unique
  `IX_MarketingActions_OutlookEventId` index) rolls back every create/update/delete of the run.
- **Rows outside the window never self-heal.** Actions ending more than 30 days ago keep whatever
  they were last synced with, including the pre-#4224 one-day-too-long all-day ends.
- **Legacy pushes before #4224** sit in Outlook as timed (`isAllDay: false`) 24-hour events, so
  import keeps them as timed — needs a one-time re-push (memory note, Sept 2026).
- **No `lastModifiedDateTime` comparison** — every field difference is "Outlook wins", there is no
  "who edited last" logic.
- **Recurring Outlook series**: `calendarView` returns individual occurrences, each with its own
  event id, so each occurrence becomes its own Heblo action (Graph behaviour; not tested in repo).
- `docs/integrations/microsoft-graph-calendar.md` "Known gaps" still says there is no persisted
  all-day flag; `MarketingActions.IsAllDay` exists since migration `AddIsAllDayToMarketingAction`
  (2026-09-21).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/Jobs/MarketingCalendarSyncJob.cs` — job id, cron, window, locks
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs` — create/update/restore/orphan rules, single-save batch
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs` — field mapping, HTML stripping, all-day end conversion
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCategoryMapper.cs` — category ⇄ action type
- `backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/OutlookEventDto.cs` — Graph payload, UTC handling
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` — `ListEventsAsync`, `GetEventAsync` (Graph calls, app-only token)
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` — `GetByOutlookEventIdsAsync` (includes deleted), `GetSyncedInWindowAsync`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/ImportFromOutlookHandler.cs` — manual import
- `frontend/src/components/marketing/detail/ImportFromOutlookModal.tsx` — import dialog
- `docs/integrations/microsoft-graph-calendar.md` — Graph time-zone and all-day findings
