---
process: module-marketing
kind: module
module: marketing
summary: The marketing calendar — a shared plan of marketing actions (posts, blog, newsletter, PR, events, meetings) mirrored with the Outlook marketing group calendar and tagged with products and asset folders.
owns: []
verified_at: "5e993f9e2"
related:
  - sync-marketing-calendar
  - feed-marketing-action-to-outlook
---

# Marketing calendar

## Purpose
Gives the marketing team one plan of what goes out when: social posts, blog articles,
newsletters, PR, events and meetings (marketing actions, *marketingové akce*). The plan lives in a
shared **Outlook group calendar** (source of truth) and is mirrored into Heblo, where each action
can additionally be linked to **products** (product code or code prefix, e.g. a whole product line)
and to **asset folders** (images, texts, videos, graphics). That lets the team answer "what are we
running for this product, and when?" without leaving Heblo.

Not in this module: marketing spend and ROAS (`MarketingPerformance`, page `/marketing/performance`),
imported ad invoices (`MarketingInvoices`), the photo bank (`/marketing/photobank`) and Ecomail — they
are separate modules even though they share the "Marketing" menu and recurring-job category.

## Users & screens
- **Marketingový kalendář** — `/marketing/calendar`, menu Marketing. Needs permission
  `marketing.marketing_calendar.read`; every change needs `marketing.marketing_calendar.write`.
  - Calendar grid (FullCalendar, 5-week and 2-week views) and a mobile agenda view; drag & drop moves an action.
  - "Nová akce" / click an action → modal: title, description, type (Sociální sítě, Blog,
    Newsletter, PR, Událost, Meeting), from/to date, assigned products (*Přiřazené produkty*,
    autocomplete or free code prefix), folder links (path + type).
  - List/grid with filters: text search (title, description), type, product prefix, date ranges.
  - "Import z Outlooku" → date range + "Jen simulace (dry run)" → result with counts and any
    unmapped Outlook categories.
- No dashboard tile and no MCP tool reads marketing actions.

## Processes
- `sync-marketing-calendar` — Outlook group calendar → Heblo; Hangfire `marketing-calendar-sync`
  hourly (`0 * * * *`), window 30 days back → 12 months ahead, plus the manual "Import z Outlooku".
- `feed-marketing-action-to-outlook` — Heblo create/edit/move/delete → Outlook event, synchronous
  per user action, only when `MarketingCalendar:PushEnabled` is true.

Plain reads without a process doc: `GET /api/MarketingCalendar` (paged list),
`GET /api/MarketingCalendar/{id}`, `GET /api/MarketingCalendar/calendar?startDate&endDate`
(actions overlapping the range). Product and folder tags are edited as part of create/edit and
never leave Heblo.

## Data owned
All in schema `public`:
- `MarketingActions` — one planned action: title, description, `ActionType` (int enum),
  `StartDate`/`EndDate` (UTC, end **inclusive**, nullable = point in time), `IsAllDay`, audit
  columns, soft-delete columns (`IsDeleted`, `DeletedAt`, `DeletedByUserId` — `"system"` when
  the sync deleted it), Outlook link (`OutlookEventId`, unique when not null;
  `OutlookSyncStatus` stored as text `NotSynced`/`Synced`; `OutlookSyncedAt`; `OutlookSyncError`).
  Global query filter hides soft-deleted rows.
- `MarketingActionProducts` — action ↔ product code prefix (upper-cased, ≤ 50 chars), PK
  (action, prefix). Free text; not validated against the catalogue.
- `MarketingActionFolderLinks` — action ↔ folder key (path, ≤ 100 chars) + `FolderType`,
  PK (action, folder key).

## External systems
- **Microsoft 365 / Graph — Outlook group calendar** of group `MarketingCalendar:GroupId`:
  - read (`calendarView`, `calendar/events/{id}`) with an app-only token — `sync-marketing-calendar`;
  - write (`POST`/`PATCH`/`DELETE calendar/events`) with the signed-in user's delegated token,
    scope `Group.ReadWrite.All` — `feed-marketing-action-to-outlook`.
  Outlook colour categories ⇄ action types via `MarketingCalendar:CategoryMappings`.

## Dependencies
- Reads no other Heblo module in the backend. The product picker in the modal uses the catalogue
  autocomplete on the frontend only.
- No other module reads marketing actions.
- Hangfire / Recurring Jobs (`RecurringJobConfigurations`) holds the job toggle and cron.

## Known quirks
- **Outlook is the source of truth**: with push off, edits to imported actions in Heblo are
  overwritten within the hour; with push on, Heblo writes Outlook first. Details in the two
  process docs.
- **Folder links are dropped when an action is edited in the UI** (read from code): the modal
  reads `path` while the API returns `folderKey`. See `feed-marketing-action-to-outlook`.
- **Product filter matches the other way round**: the list filter `ProductCodePrefix` returns
  actions whose stored prefix is a *prefix of the searched code* (`searched.StartsWith(stored)`),
  so searching a full product code finds actions tagged with its product line, but searching a
  short prefix does not find actions tagged with longer codes.
- **`OutlookSyncStatus` has no failure state**, so the red "sync failed" dot in the list is dead UI.
- **`docs/architecture/module-map.md` §23** groups this module with Marketing Invoices; the code
  and permissions are separate (`Features/Marketing` vs `Features/MarketingInvoices`).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs` — DI, options validation
- `backend/src/Anela.Heblo.API/Controllers/MarketingCalendarController.cs` — endpoints, `Marketing_MarketingCalendar` permission
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — entity, all-day rule, soft delete, Outlook link
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` — list/calendar queries, filters
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionConfiguration.cs` — table, indexes, query filter
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` — the page
- `frontend/src/components/marketing/detail/MarketingActionModal.tsx` — create/edit form
- `docs/integrations/microsoft-graph-calendar.md` — Graph findings
