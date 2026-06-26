# Marketing Calendar — Two-Way Outlook Sync via Delegated Writes

**Date:** 2026-05-17
**Status:** Approved design — ready for implementation planning

## Problem

The Marketing Calendar syncs **from** the Microsoft 365 group calendar (import works) but
not **to** it. Creating a marketing action in the app fails to push the event to Outlook
with `403`.

### Root cause (confirmed)

`OutlookCalendarSyncService` uses an **app-only** token
(`ITokenAcquisition.GetAccessTokenForAppAsync`) for every Graph call. Microsoft Graph
**does not support application permissions for writing to a Microsoft 365 group
calendar** — only members of the group can write, which requires a *delegated* token.

Evidence:

- App Insights (`aiHeblo`, role `Heblo-API-Production`) shows the write failing on a
  ~5-minute cadence (the retry service):
  `Graph CreateEvent failed with status 403. Response: {"error":{"code":"ErrorAccessDenied","message":"Access is denied. Check credentials and try again."}}`
- `ErrorAccessDenied` is an **Exchange mailbox store** error — Graph accepted the token
  and the permission check passed; the group mailbox itself rejected the write. A missing
  permission would instead yield `Authorization_RequestDenied`.
- The `Heblo-Service` app registration (`8b34be89-…`, SP `3997d0b1-…`) already has
  admin-consented application permissions `Group.ReadWrite.All` and `Calendars.ReadWrite`.
  No application permission can fix this — none grants group-calendar writes.
- Reads keep working because `ListEventsAsync` (`GET /groups/{id}/calendarView`) **is**
  supported app-only via `Group.Read.All`.
- Git commit `6604dd10` ("switch Outlook calendar sync from user mailbox to M365 group
  endpoint") introduced the regression: user-mailbox writes supported app-only; group
  writes do not.

## Solution overview

Switch Marketing write operations to **delegated** (on-behalf-of) tokens — the signed-in
user's identity. A user who is a member of the marketing group can then write to its
calendar. Invert the persistence order: **write to Outlook first; persist to the DB only
after the Outlook write succeeds.** This makes the Outlook write the permission gate and
keeps the app DB and the group calendar strongly consistent — there is no "unsynced" or
"failed" state to track.

## Section 1 — Write flow (create / update / delete)

All three Marketing write operations push to Outlook first using the signed-in user's
delegated token, and commit the DB change only on success.

- **Create:** build the `MarketingAction` in memory → `CreateEventAsync` (delegated) → on
  success set `OutlookEventId` → save to DB. If the Outlook push throws, return an error
  and persist nothing.
- **Update:** apply changes in memory → `UpdateEventAsync`, or `CreateEventAsync` if the
  action has no `OutlookEventId` yet → on success `SaveChangesAsync`. If Outlook throws,
  reject; DB unchanged.
- **Delete:** `DeleteEventAsync` → on success soft-delete in the DB. If the Outlook event
  is already gone (Graph `404`), treat as success and proceed. Any other Outlook error
  rejects the delete.
- **`PushEnabled = false`** (and mock-auth dev via `NoOpOutlookCalendarSync`): skip the
  Outlook step, write the DB only. The flag is kept as the environment toggle for
  environments without a configured group calendar.

## Section 2 — Partial-failure handling

New edge case: the Outlook write succeeds but the subsequent DB save fails, orphaning an
Outlook event with no DB record. The create handler issues a **compensating delete** of
the just-created Outlook event, then returns an error. If the compensating delete also
fails, the orphan is logged (rare — DB outage). Update/delete do not orphan: update only
mutates an existing event, and delete persists after the Outlook call but a failed DB
delete simply leaves both records present (consistent).

## Section 3 — Error surfacing

`OutlookCalendarSyncException` already carries the HTTP status. Handlers map it to
client-facing error codes:

- **`403`** → new error code `MarketingCalendarAccessDenied` → message: *"You don't have
  permission to write to the marketing calendar. You must be a member of the marketing
  group."*
- **Any other failure** → new error code `MarketingCalendarSyncFailed` → message:
  *"Couldn't reach the Outlook calendar. Please try again."*

The frontend renders this as a normal form error / toast; the create/edit modal stays
open so the user does not lose input.

## Section 4 — Auth wiring (delegated / on-behalf-of)

- `OutlookCalendarSyncService` write methods (`CreateEventAsync`, `UpdateEventAsync`,
  `DeleteEventAsync`) switch from `GetAccessTokenForAppAsync` to
  **`GetAccessTokenForUserAsync`** with scope
  `https://graph.microsoft.com/Group.ReadWrite.All`.
- `ListEventsAsync` (import) **stays app-only** (`GetAccessTokenForAppAsync`) — group
  calendar reads work app-only and need no member context.
- `AuthenticationExtensions.ConfigureRealAuthentication`: add
  `.EnableTokenAcquisitionToCallDownstreamApi()` and a token cache to the
  `AddMicrosoftIdentityWebApiAuthentication` builder (currently only the WebApp builder
  has it) so the API can perform the OBO exchange for Bearer-authenticated SPA requests.
- **Azure (manual, outside the code change):** the API app registration needs the
  **delegated** `Group.ReadWrite.All` Microsoft Graph permission with admin consent. To
  be documented in `docs/integrations/` (or the relevant existing doc).

## Section 5 — Removals

The retry service and failure-tracking are obsolete once a write either fully succeeds or
fully fails:

- `OutlookSyncRetryHostedService` and its `AddHostedService` registration in
  `MarketingModule` — deleted.
- `IMarketingActionRepository.GetFailedOutlookSyncAsync` and its implementation — deleted
  (only the retry service consumed it).
- Domain sync-failure tracking — `MarkOutlookFailed` and the "Failed" sync-status value —
  now unreachable; removed. Exact domain shape (`MarketingAction`, sync-status enum,
  `ClearOutlookLink`, `MarkOutlookSynced`) to be confirmed during planning so only dead
  members are removed.

## Section 6 — Testing

- **Handler unit tests** (`Create`/`Update`/`Delete`): Outlook-first ordering; Outlook
  failure → no DB mutation; DB failure after a successful create → compensating delete
  fires; `PushEnabled = false` → DB-only path.
- **`OutlookCalendarSyncService` tests:** delegated-token path; `403` → access-denied
  mapping; delete `404` → treated as success.
- Existing Marketing handler tests that assume the old DB-first order are updated.
- Target ≥ 80% coverage on changed code per project testing rules.

## Out of scope

- Changing the import (read) path.
- Concurrency control for simultaneous edits of the same action.
- Re-introducing automatic background retry (explicitly removed; see Section 5).
