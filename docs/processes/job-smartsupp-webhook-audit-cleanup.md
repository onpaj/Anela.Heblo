---
process: job-smartsupp-webhook-audit-cleanup
kind: job
module: smartsupp
summary: Nightly purge of Smartsupp webhook audit rows older than 7 days and of chat-presence rows not refreshed for a day.
owns:
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/**
verified_at: "5e993f9e2"
related: [sync-smartsupp-webhooks, flow-smartsupp-reply]
---

# Smartsupp webhook audit & presence cleanup

## Purpose
Housekeeping for the chat console. Every Smartsupp webhook is stored with its full raw body and
headers in `SmartsuppWebhookAuditEntries` (see `sync-smartsupp-webhooks`) so admins can debug
and replay failed events; this job keeps that table small by dropping rows older than a week.
It also clears abandoned "who is in this chat" presence rows (`SmartsuppConversationPresences`)
left behind when a browser tab crashed or an `agent_left` webhook never came. Nobody looks at its
output; its effect is that **replay of a failed webhook is only possible for 7 days**.

## Trigger
Hangfire recurring job `smartsupp-webhook-audit-cleanup` (category Integrations, display name
"Smartsupp Webhook Audit Cleanup"), cron `30 3 * * *` (03:30 daily, Europe/Prague per the
Recurring Jobs scheduler), enabled by default. Can be run or disabled from the Recurring Jobs
admin page.

## Data flow
1. Presence: `SmartsuppPresenceRepository.PurgeExpiredAsync(cutoff, cutoff)` with cutoff = now
   (UTC) − 1 day → `DELETE` (EF `ExecuteDelete`) from `SmartsuppConversationPresences` where
   `LastSeenAt < cutoff`, for both sources (`Heblo`, `Smartsupp`). Logs the count if > 0.
2. Audit: `SmartsuppWebhookAuditRepository.PurgeOlderThanAsync(now − 7 days)` loads all rows with
   `ReceivedAt < cutoff`, `RemoveRange`, `SaveChanges`. Logs the count or "nothing to delete".

## Logic & formulas
- Retention constants in code: audit `RetentionDays = 7` (by `ReceivedAt`, UTC), presence
  `PresenceRetentionDays = 1` (by `LastSeenAt`, UTC). Not configurable.
- Presence rows are already treated as inactive on read after 90 s (Heblo) / 30 min (Smartsupp);
  the job only removes the dead rows physically.
- Audit rows are deleted regardless of status — failed (`HandlerException`) rows that were never
  replayed are lost too.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| — | — | No configuration; retention is hard-coded. Enable/disable via the Recurring Jobs page. |

## Runtime facts
None.

## Known quirks
- Job metadata description says only "Deletes Smartsupp webhook audit entries older than 7 days";
  it also purges presence rows.
- The audit purge loads every expired row (incl. raw bodies) into memory before deleting — the
  load-then-`RemoveRange` pattern is deliberate so the job stays testable on the EF InMemory
  provider (agent memory `gotcha_inmemory_no_executedelete`). The presence purge uses
  `ExecuteDelete` and therefore cannot be unit-tested on InMemory.
- After 7 days a dropped chat event (see `sync-smartsupp-webhooks` → Known quirks) can no longer
  be replayed; there is no other recovery path.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppWebhookAuditCleanupJob.cs` — job, retention constants
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppWebhookAuditRepository.cs` — `PurgeOlderThanAsync`
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppPresenceRepository.cs` — `PurgeExpiredAsync`
