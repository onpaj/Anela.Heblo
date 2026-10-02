---
process: feed-marketing-action-to-outlook
kind: feed
module: marketing
summary: When PushEnabled is on, creating, editing, moving or deleting a marketing action in Heblo writes the same change to the Outlook marketing group calendar through Microsoft Graph, on behalf of the signed-in user.
owns:
  - backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/**
  - backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/**
  - backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/**
  - backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/**
  - backend/src/Anela.Heblo.Application/Features/Marketing/Services/IOutlookCalendarSync.cs
  - backend/src/Anela.Heblo.Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs
  - backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookCalendarSyncException.cs
  - backend/src/Anela.Heblo.Application/Features/Marketing/Configuration/MarketingCalendarOptions.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs
  - backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs
verified_at: "5e993f9e2"
related:
  - sync-marketing-calendar
---

# Marketing action push (Heblo → Outlook)

## Purpose
Lets the marketing team plan directly in Heblo's calendar (Marketingový kalendář,
`/marketing/calendar`) and still have every action appear in the shared Outlook group calendar
that the rest of the company sees. Each create / edit / drag-to-move / delete in Heblo is written
to Outlook **synchronously, before** Heblo saves; if Outlook refuses, the user sees an error and
Heblo does not save the change. The way back (Outlook → Heblo) is `sync-marketing-calendar`.

Only fields Outlook knows travel: title, description, start/end, all-day flag and one category.
Product tags and folder links stay in Heblo only.

## Trigger
On demand, from the calendar page (needs Marketingový kalendář **write**):

| UI action | Endpoint | Outlook call (when `PushEnabled`) |
|---|---|---|
| "Nová akce" → save | `POST /api/MarketingCalendar` | create event |
| edit → save | `PUT /api/MarketingCalendar/{id}` | update event; **create** one if the action has no `OutlookEventId` yet |
| drag & drop in month view | `PATCH /api/MarketingCalendar/{id}/move` | update event, only if it has an `OutlookEventId` |
| delete | `DELETE /api/MarketingCalendar/{id}` | delete event, only if it has an `OutlookEventId` |

With `MarketingCalendar:PushEnabled = false` (repo default) none of these call Outlook; the
change is saved in Heblo only.

## Data flow
1. Handler loads the action (`public."MarketingActions"`, with products and folder links) and
   applies the change in memory.
2. Graph call with a **delegated** token for the signed-in user (scope
   `https://graph.microsoft.com/Group.ReadWrite.All`), so Outlook shows the user as the
   organiser/editor:
   - create: `POST /v1.0/groups/{GroupId}/calendar/events` → returns the event `id`;
   - update / move: `PATCH /v1.0/groups/{GroupId}/calendar/events/{OutlookEventId}`;
   - delete: `DELETE /v1.0/groups/{GroupId}/calendar/events/{OutlookEventId}`.
3. On success: `OutlookEventId` = event id, `OutlookSyncStatus = Synced`,
   `OutlookSyncedAt` (column for `OutlookLastAttemptAt`) = now.
4. Save to the DB (`MarketingActions`; `MarketingActionProducts` / `MarketingActionFolderLinks`
   replaced as a whole on create/edit). Delete is a **soft delete** (`IsDeleted`, `DeletedBy…`).

## Logic & formulas
Event body sent to Graph:

| Graph field | From | Rule |
|---|---|---|
| `subject` | `Title` | as stored (trimmed, ≤ 200) |
| `body` | `Description` | `contentType: text`, empty string if none |
| `start` | `StartDate` | `yyyy-MM-ddTHH:mm:ss.fffffff`, `timeZone: "UTC"` |
| `end` | `EndDate` | no end → start + 1 hour; all-day → `EndDate` + 1 day (Heblo inclusive → Graph exclusive); otherwise as is |
| `isAllDay` | `IsAllDay` | sent explicitly |
| `categories` | `ActionType` | one category: the **first** `CategoryMappings` entry with that type, else the enum name (e.g. `SocialMedia`) |

- **All-day flag in Heblo**: on create/edit `IsAllDay` = both start and end are exactly midnight
  (`ComputeIsAllDay`); an action with no end date is never all-day. Move (drag & drop) keeps
  the existing flag.
- **Error mapping**: Graph 403 or missing consent (`MsalUiRequiredException`) →
  `MarketingCalendarAccessDenied`; any other Graph failure → `MarketingCalendarSyncFailed`;
  Heblo saves nothing in either case. On delete, Graph 404 is treated as "already gone" and
  the soft delete proceeds.
- **Create compensation**: if the Outlook event was created but the DB save fails, Heblo deletes
  the new Outlook event again; if that delete also fails the event is left orphaned in Outlook
  (logged as error) and returns as a new action at the next hourly sync.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `MarketingCalendar:PushEnabled` | `false` | master switch for this feed; read live on every request (`IOptionsMonitor`) |
| `MarketingCalendar:GroupId` | `""` | target Microsoft 365 group; startup validation requires it when `PushEnabled` is true |
| `MarketingCalendar:CategoryMappings` | 6 colour categories | reverse lookup action type → Outlook category (see `sync-marketing-calendar`) |
| `UseMockAuth` / `BypassJwtValidation` | — | either true → `NoOpOutlookCalendarSync`: create returns an empty id, which `MarkOutlookSynced` rejects (see quirks) |

## Runtime facts
- `MarketingCalendar__PushEnabled` is **true** in production — agent memory
  `gotcha_graph_allday_end_is_exclusive` — Sept 2026.

## Known quirks
- **Outlook first, DB second, no transaction across them.** Update/move/delete: if Outlook
  accepted the change but the DB save fails, Outlook has the new state and Heblo the old one;
  the hourly sync then copies Outlook's state into Heblo (for delete: the event is gone in
  Outlook, so the next sync soft-deletes the row as "Outlook sync").
- **Editing a Heblo-only action exports it.** With `PushEnabled` on, saving an edit of an action
  that has no `OutlookEventId` creates a new Outlook event; from then on the hourly sync owns
  its title/dates/type.
- **Drag & drop does not export a Heblo-only action** (move pushes only when an event id exists).
- **`OutlookSyncStatus` is only `NotSynced` or `Synced`.** `OutlookSyncError` is never written
  and there is no retry; the red "Synchronizace s Outlookem selhala – bude opakována" dot in the
  list (`MarketingActionGrid`, status `'Failed'`) can never appear. A failed push is visible only
  as the error toast.
- **Category round-trip is not injective**: if two Outlook categories map to the same type,
  Heblo always pushes the first one listed, so an event can change colour after an edit in Heblo.
- **Mock/bypass auth** (`NoOpOutlookCalendarSync`) with `PushEnabled` true: create returns an
  empty id and `MarkOutlookSynced` throws `ArgumentException`, so creating an action fails
  (500) in such environments. Keep `PushEnabled` false locally.
- **Timed events before the UTC fix** were labelled `Europe/Prague` while holding UTC values and
  drifted by the UTC offset in Outlook; the adapter now declares `UTC` for both start and end
  (agent memory, Sept 2026 — fixed in code since).
- **Folder links are lost when an action is edited in the UI** (read from code, not observed):
  the API returns folder links as `{folderKey, folderType}`, but the edit modal
  (`MarketingActionModal`, typed by `MarketingActionGrid`) reads `fl.path`, so every existing
  link loads with an empty path, is filtered out on save, and `PUT` replaces the set with
  nothing. The optional "Popis" label of a folder link is never sent to the API at all.
- **Folder type labels do not match enum names**: General = "Obrázky", Seasonal = "Texty",
  ProductLine = "Videa", Campaign = "Grafika", Other = "Ostatní".

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` — create + compensating delete
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` — update or first export
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs` — drag & drop reschedule
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs` — delete, 404 tolerance
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` — Graph URLs, delegated token, event body, `BuildGraphEnd`
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` — real vs no-op registration
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — `ComputeIsAllDay`, `MarkOutlookSynced`, soft delete
- `backend/src/Anela.Heblo.API/Controllers/MarketingCalendarController.cs` — endpoints and permissions
