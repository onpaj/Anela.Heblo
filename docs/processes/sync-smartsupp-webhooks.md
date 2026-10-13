---
process: sync-smartsupp-webhooks
kind: sync
module: smartsupp
summary: Smartsupp pushes every chat event to a signed webhook; Heblo audits it and upserts conversations, messages, contacts and native-agent presence, with admin replay and an orphan-contact repair for events that failed.
owns:
  - backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs
  - backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs
  - backend/src/Anela.Heblo.API/Webhooks/Smartsupp/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ListWebhookAudit/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetWebhookAuditEntry/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppAgentCache.cs
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs
  - backend/src/Anela.Heblo.Domain/Features/Smartsupp/**
  - backend/src/Anela.Heblo.Persistence/Smartsupp/**
  - backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/**
verified_at: "5e993f9e2"
related: [flow-smartsupp-reply, job-smartsupp-webhook-audit-cleanup]
---

# Smartsupp webhooks → Heblo chat mirror

## Purpose
Keeps Heblo's copy of the e-shop live chat current, so the support console
(**Customer Support → Smartsupp Chats**, `/customer/smartsupp`) shows new conversations, every
customer/agent/bot message, who the customer is, ratings, closures and which native Smartsupp
agent is currently in a chat. It is the **only** way chat data enters Heblo — nothing polls
Smartsupp. Every incoming call is also stored in an audit table so a failed event can be
inspected and replayed by an admin.

## Trigger
- On demand: Smartsupp calls `POST /api/webhooks/smartsupp` (anonymous; body ≤ 1 MiB) for each
  subscribed event. Near real time; there is no schedule.
- Admin, by hand (`Admin_Administration` write, API only — no UI page):
  - `GET /api/admin/smartsupp/webhooks` — audit list, filters `from`, `to`, `eventName`,
    `signatureStatus`, `processingStatus`, `skip`, `take` (default 50), newest first.
  - `GET /api/admin/smartsupp/webhooks/{id}` — one entry incl. raw body and headers.
  - `POST /api/admin/smartsupp/webhooks/{id}/replay` — re-run a stored event.
  - `POST /api/smartsupp/admin/refresh-orphan-contacts` — re-link conversations that have no
    contact name/email.

## Data flow
Source: Smartsupp webhook (JSON envelope `event`, `timestamp`, `account_id`, `app_id`, `data`).
Target: `public` tables `SmartsuppWebhookAuditEntries`, `SmartsuppConversations`,
`SmartsuppMessages`, `SmartsuppContacts`, `SmartsuppConversationPresences`.

1. `SmartsuppWebhookController.Receive` buffers the raw body and records payload size.
2. **Ignore list** — if `event` is in `Smartsupp:IgnoredEventTypes`, return 200 immediately:
   no signature check, no audit row.
3. **Signature** — `SmartsuppHmacVerifier`: lowercase hex HMAC-SHA256 of the raw body with
   `Smartsupp:WebhookSecret`, constant-time compared with header `X-Smartsupp-Hmac`. Missing
   header or empty secret → fail. Failure → audit row (`SignatureStatus` `Missing`/`Mismatch`),
   **401**.
4. **Malformed JSON** → audit row (`ProcessingStatus = MalformedJson`), 200.
5. **App id** — if `Smartsupp:WebhookAppId` is non-empty and differs from `app_id` → audit row
   (`AppIdMismatch`), 401.
6. Audit row saved with `SignatureStatus = Valid`, `ProcessingStatus = NotProcessed`.
7. MediatR `ProcessWebhookEventRequest` → `ProcessWebhookEventHandler` picks the reaction whose
   `EventName` matches exactly (table below), runs it, then `SaveChangesAsync`. Unknown events:
   `visitor.*` → outcome "observed", `app.*` → "ignored", anything else → "unknown"; nothing
   written, still `Success`.
8. Audit row updated to `Success` or `HandlerException` (full exception text in
   `ProcessingError`) with duration. **The response is 200 either way**, so Smartsupp never
   retries a failed event.

### Event → effect
| Event | Effect |
|---|---|
| `conversation.opened`, `conversation.closed`, `conversation.closed_by_contact`, `conversation.rated`, `conversation.agent_assigned`, `conversation.agent_unassigned` | Map `data.conversation` (opened/closed fall back to `data` itself) → contact enrichment → upsert conversation. `closed`: `CloseType` = `data.close_type`, `ClosedByAgentId` = `data.agent_id`, `LastClosedAt` = event time. `closed_by_contact`: `CloseType = "contact"`. `rated`: `Rating` = `data.rating_value`, `RatingText` = `data.rating_text`. `agent_assigned`: `AssignedAgentIdsJson` = `[data.assigned]`. `agent_unassigned`: cleared. |
| `conversation.contact_replied`, `conversation.agent_replied`, `conversation.bot_replied` | Upsert conversation (if `data.conversation` present, with enrichment) and the message in `data.message` |
| `conversation.message_delivered` / `conversation.message_delivery_failed` | Set `DeliveryStatus` `delivered` (+ `DeliveredAt` = event time) / `failed` on the existing message; unknown message id → no-op |
| `conversation.agent_joined` / `conversation.agent_left` | Upsert / delete presence row (`Source = Smartsupp`, agent display name from the agent cache, falling back to the id) |
| `contact.created`, `contact.updated`, `contact.acquired` | Upsert contact, then copy its name/email onto every conversation with that `ContactId` |
| `contact.banned`, `contact.unbanned` | Upsert contact only |

### Contact enrichment (`SmartsuppContactEnricher`)
Conversation webhooks carry only `contact_id`. If that contact is not in `SmartsuppContacts`,
Heblo calls `GET {BaseUrl}contacts/{id}` and stages it, then fills the conversation's
`ContactName`/`ContactEmail` if empty. On REST error or 404 it **fails open**: `ContactId` is set
to null and the conversation is saved unlinked (shows without a name).

### Replay (`ReplayWebhookEventHandler`)
Loads the audit row, re-parses `data` from `RawBody`, sends the same `ProcessWebhookEventRequest`
(timestamp = stored `EventTimestamp`, else now), then increments `ReplayCount` and sets
`LastReplayedAt`/`LastReplayedBy` (current user's name). No signature/app-id/ignore checks.
404 if the id is unknown (or already purged), `InvalidOperation` if the stored body is not JSON.

### Orphan-contact repair (`RefreshOrphanContactsHandler`)
For every conversation with `ContactName` **and** `ContactEmail` null: `GET conversations/{id}`
from Smartsupp; if it has a `contact_id`, re-attach it, run the enricher, upsert and save.
Returns `Scanned`, `Updated`, `SkippedNoContactId`, `Failed`, `FailedIds`. Runs synchronously in
the HTTP request, one REST call (or two) per orphan, all conversations ever stored — no batching.

## Logic & formulas
- **Status mapping**: Smartsupp `open` → `Open`, `closed` → `Resolved`, `pending` → `Pending`,
  missing/other → `Open`.
- **Author type** from message `sub_type`: `agent` → Agent, `bot` → Bot, `contact` → Visitor,
  `system` → System, `trigger` → Trigger, anything else → Visitor. Message text = `content.text`,
  else `content.html`, else a plain `content` string.
- **Timestamps**: all converted to UTC. Missing `created_at`/`updated_at` → *now*; missing
  envelope `timestamp` → *now*.
- **Last-write-wins guard**: conversation and contact upserts (raw SQL `INSERT … ON CONFLICT
  ("Id") DO UPDATE … WHERE EXCLUDED."UpdatedAt" >= existing."UpdatedAt"`) ignore an event older
  than what is stored. When newer, **every column is overwritten** with the event's values,
  except `ContactName`, `ContactEmail`, `ContactAvatarUrl`, which keep the old value when the
  event has null (COALESCE).
- Messages are upserted through EF (insert new id, otherwise update content/delivery/etc.).
- Conversation/contact upserts execute immediately; message, delivery and denormalisation
  changes are written by the final `SaveChangesAsync` — the two are not in one transaction.
- **Agent names** (`SmartsuppAgentCache`): singleton, `GET agents`, 1 h TTL; on failure returns
  the last good map or an empty one.
- **Metrics** (meter in `SmartsuppWebhookMetrics`): `smartsupp.webhook.received_total`
  (tags event, outcome handled/observed/ignored/unknown/error),
  `smartsupp.webhook.signature_failures_total`, `smartsupp.webhook.handle_duration_ms`,
  `smartsupp.webhook.payload_bytes`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Smartsupp:WebhookSecret` | `-- stored in secrets.json --` (real value is a secret outside the repo) | HMAC key; empty → every webhook rejected 401 |
| `Smartsupp:WebhookAppId` | `""` | When set, only this `app_id` is accepted |
| `Smartsupp:IgnoredEventTypes` | `[]`; Staging: `visitor.connected`, `visitor.updated`, `visitor.triggered`, `visitor.disconnected`; Production: same + `visitor.typing` | Events dropped before signature check, never audited |
| `Smartsupp:ApiToken` | `-- stored in secrets.json --` | Bearer token for contact/conversation/agent REST lookups |
| `Smartsupp:BaseUrl` | `https://api.smartsupp.com/v2/` | REST base |
| `Smartsupp:HttpTimeoutSeconds` | `30` | REST HTTP timeout |

## Runtime facts
- Before the fix in `SmartsuppContactEnricher.MapContactDataToEntity`, ~134 events / 30 days
  failed with `HandlerException` ("Cannot write DateTime with Kind=Unspecified …"), dropping
  mostly Facebook Messenger conversations (~81 % failure vs ~23 % for other channels);
  `contact.created` was 60/60 success. Recovery = replay of `ProcessingStatus = 3` rows —
  `memory/gotchas/smartsupp-staged-contact-datetime-kind.md` — undated (pre-2026-10).

## Known quirks
- **Failed events are not retried.** The controller answers 200 after a `HandlerException`, and
  nothing re-processes it automatically. Recovery is a manual replay, possible only while the
  audit row exists (7 days, see `job-smartsupp-webhook-audit-cleanup`).
- **A missed webhook is never healed.** No polling sync exists; REST `SearchConversationsAsync` /
  `GetConversationMessagesAsync` and repository `ListOpenConversationRefsAsync` /
  `MarkConversationResolvedAsync` are dead code from the removed sync.
- **Visitor-info cache is wiped by every conversation upsert**: the webhook mapper never sets the
  `Visitor*` columns, and the upsert overwrites them with null, so the next visitor-info open
  re-fetches from Smartsupp (see `flow-smartsupp-reply`). Same "overwrite with whatever the payload
  has" applies to rating, close type, tags and assigned agents if a later event's conversation
  object omits them.
- **Ignored events skip authentication and audit** — the ignore list is checked on the unverified
  body before the HMAC check, by design to save audit volume.
- **Replay does not update the audit outcome**: `ProcessingStatus` stays `HandlerException` even
  after a successful replay — look at `ReplayCount`/`LastReplayedAt`. If the replay itself
  throws, the endpoint fails and `ReplayCount` is not incremented.
- **Fail-open enrichment** leaves conversations without a contact (shown nameless). These are what
  `refresh-orphan-contacts` repairs; it also scans conversations that simply never had a contact
  (anonymous chats), so `SkippedNoContactId` is normally large.
- `contact.*` and enrichment both write `SmartsuppContacts` via raw SQL: all DateTimes passed
  there must be `DateTimeKind.Utc` (gotcha above; `memory/gotchas/smartsupp-staged-contact-datetime-kind.md`).
- **Poisoned-context edge case (code reading, not observed)**: if the handler's final
  `SaveChangesAsync` fails (e.g. a message whose conversation row does not exist → FK violation),
  the failed entity stays tracked in the shared `ApplicationDbContext`; the controller's
  `UpdateOutcomeAsync` then saves the same context, throws again, and the request ends in **500**
  with the audit row stuck at `NotProcessed` instead of `HandlerException`. Filter the audit list on
  `NotProcessed` too when hunting failures.
- `conversation.agent_unassigned` clears **all** assigned agents, not just the one unassigned;
  `agent_assigned` replaces the list with the single new agent.

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs` — envelope, ignore list, signature, audit, 200/401 rules
- `backend/src/Anela.Heblo.API/Webhooks/Smartsupp/SmartsuppHmacVerifier.cs` — HMAC check
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/ProcessWebhookEventHandler.cs` — dispatch
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/` — one class per event
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Mappers/SmartsuppPayloadMapper.cs` — field mapping
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs` — REST contact staging
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` — raw-SQL upserts and the UpdatedAt guard
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs`, `.../RefreshOrphanContacts/RefreshOrphanContactsHandler.cs` — recovery tools
- `docs/integrations/smartsupp-webhook.md` — payload examples per event and Postman HMAC recipe
