---
process: module-smartsupp
kind: module
module: smartsupp
summary: E-shop live-chat console — mirrors Smartsupp chats into Heblo via webhooks and lets support staff read, answer (with an AI draft), and close them without leaving Heblo.
owns: []
verified_at: "5e993f9e2"
related: [sync-smartsupp-webhooks, flow-smartsupp-reply, job-smartsupp-webhook-audit-cleanup]
---

# Smartsupp (customer live chat)

## Purpose
Anela's e-shop (and its Facebook Messenger channel) runs customer chat in **Smartsupp**. This
module gives the support team a chat console inside Heblo (**Customer Support → Smartsupp Chats**,
Czech UI "Všechny konverzace") so they can:
- see open and closed (resolved) conversations with the full message history,
- see who the customer is — Smartsupp contact details, the Shoptet customer account behind the
  chat (customer group, price list, shipping address), browser/OS and visited pages,
- get an **AI draft reply** ("Návrh") built from the knowledge base (KnowledgeBase RAG) in the
  tone of previous agent replies, edit it and send it to the customer as themselves,
- close the conversation ("Uzavřít konverzaci"),
- see which colleague is already looking at / working on a chat (presence badge), both in Heblo
  and in the native Smartsupp app.

Heblo never polls Smartsupp for conversations: everything it knows arrives by **webhook**.
The draft-reply ratings feed the AI evaluation dataset reviewed on **Marketing → Feedback**
(tab "Smartsupp").

## Users & screens
- **Support staff** (permission `customer.smartsupp.read`, feature `Customer_Smartsupp`):
  `/customer/smartsupp` — conversation list (Open / Resolved, refreshed every 10 s), detail
  (refreshed every 30 s), composer with topic picker and AI draft, close button, contact panel
  with Shoptet customer card and visitor info card, presence badges. The feature has only a
  *read* access level: every read holder can also send, close and generate drafts (documented as
  intentional in `SmartsuppController`).
- **Feedback reviewers**: `/marketing/feedback` tab "Smartsupp" lists AI drafts with the
  1–5 precision/style scores operators gave them.
- **Admins** (`Admin_Administration` write): API-only webhook audit list/detail/replay
  (`/api/admin/smartsupp/webhooks`) and `POST /api/smartsupp/admin/refresh-orphan-contacts`.
  There is no admin UI page for these; they are called directly (e.g. Swagger/Postman).
- No MCP tools expose Smartsupp data.

## Processes
- `sync-smartsupp-webhooks` — Smartsupp → Heblo: signed webhook per chat event updates
  conversations, messages, contacts and native-agent presence; includes admin replay and the
  orphan-contact repair. Trigger: on-demand (each webhook POST).
- `flow-smartsupp-reply` — operator workflow: AI draft (KnowledgeBase + LLM, logged to
  `RagInteractionLogs`), send message and close conversation via the Smartsupp REST API,
  presence heartbeat, draft feedback; plus on-demand Smartsupp visitor / Shoptet customer lookups.
  Trigger: buttons on `/customer/smartsupp`.
- `job-smartsupp-webhook-audit-cleanup` — nightly purge of webhook audit rows (> 7 days) and
  dead presence rows (> 1 day). Hangfire `smartsupp-webhook-audit-cleanup`, `30 3 * * *`.

Plain reads (no own doc): list conversations (`GET /api/smartsupp/conversations`, status
`Open`/`Resolved`, page size 1–200) and conversation detail (`GET /api/smartsupp/conversations/{id}`,
incl. up to 20 other conversations of the same contact and agent names). Both are served purely
from Heblo's tables plus the in-memory agent-name cache.

## Data owned
All in `public` schema of the main Heblo DB; timestamps are UTC stored as
`timestamp without time zone`.
- `SmartsuppConversations` — one row per Smartsupp conversation (id = Smartsupp id). Status
  (`Open` / `Resolved` / `Pending`), contact name/email (denormalised), channel, domain, referer,
  location, tags, Shoptet variables (`VariablesJson`), rating, close type, assigned agents,
  last message preview, plus a 24 h visitor-info cache (`Visitor*` columns).
- `SmartsuppMessages` — one row per chat message (customer, agent, bot, trigger, system), with
  delivery status, page URL, response time, attachments JSON. FK to conversation, cascade delete.
- `SmartsuppContacts` — one row per Smartsupp contact (name, email, phone, note, ban, GDPR flag,
  tags, properties). Conversations link via `ContactId` (FK, set null on delete).
- `SmartsuppConversationPresences` — who is in a conversation right now; unique on
  (`ConversationId`, `AgentId`, `Source`), `Source` = `Heblo` (browser heartbeat) or
  `Smartsupp` (native-app `agent_joined` webhook).
- `SmartsuppWebhookAuditEntries` — every accepted or rejected webhook (raw body, headers,
  signature status, processing status/error/duration, replay count). Kept 7 days.
- Writes `RagInteractionLogs` rows with feature `SmartsuppDraftReply` (table owned by the RAG /
  KnowledgeBase area): prompt, answer, actually sent text, edit flag, operator scores.
- In-memory only: `SmartsuppAgentCache` (Smartsupp agent id → name, 1 h TTL, per app instance).
  It is **not** a table.

## External systems
- **Smartsupp REST API v2** (`Smartsupp:BaseUrl`, default `https://api.smartsupp.com/v2/`,
  Bearer `Smartsupp:ApiToken`):
  - in: `GET contacts/{id}`, `GET conversations/{id}` (sync / orphan repair),
    `GET agents` (names), `GET visitors/{id}` (visitor info)
  - out: `POST conversations/{id}/messages` (send), `PATCH conversations/{id}/close` (close)
  - Retries only on HTTP 429 (3×, exponential from 2 s or `Retry-After`); HTTP timeout
    `Smartsupp:HttpTimeoutSeconds` (30 s).
- **Smartsupp webhooks** → `POST /api/webhooks/smartsupp` (anonymous, HMAC-SHA256 in
  `X-Smartsupp-Hmac`).
- **Shoptet REST** `GET /api/customers/{guid}` via the ShoptetCustomers module's
  `IShoptetCustomerClient` (read-only customer card).
- **Anthropic LLM** via the app's default `IChatClient` (model `KnowledgeBase:ChatModel`,
  repo default `claude-sonnet-4-6`) for draft replies.

## Dependencies
- Reads from **KnowledgeBase** (`SearchDocumentsRequest` through
  `KnowledgeBaseSmartsuppKnowledgeSource`, top 5 chunks) and **ShoptetCustomers**
  (customer by GUID; that module's overview is written separately).
- Writes to the shared **RAG interaction log** (`RagInteractionLogs`), which the Marketing
  feedback page and the AI eval dataset read.
- No other Heblo module reads Smartsupp tables.

## Known quirks
- **Webhooks are the only ingestion path.** The REST bulk methods `SearchConversationsAsync` and
  `GetConversationMessagesAsync`, and repository methods `ListOpenConversationRefsAsync` /
  `MarkConversationResolvedAsync`, have no callers (leftovers of a removed polling sync — migration
  `RemoveSmartsuppSyncState`). A missed webhook is never self-healed; only admin replay (within
  the 7-day audit window) recovers it.
- `docs/features/smartsupp.md` is stale: it still describes a manual sync
  (`POST /api/smartsupp/sync`, "Sync now" button, Hangfire scheduler) that no longer exists,
  and calls `agent_joined`/`agent_left` no-ops (they now drive presence).
- Sending requires the operator's e-mail in `Smartsupp:SendMessage:AgentMap` (e-mail →
  Smartsupp agent id, five people in the repo default). Anyone else gets error 2707
  `SmartsuppAgentMappingNotFound` — can read and draft, but not send.
- The Shoptet customer card never shows recent orders: `RecentOrders` is always empty
  (TODO in `GetSmartsuppContactShoptetInfoHandler`; e-mail based order lookup was removed).
- Facebook Messenger chats used to be dropped on every event (REST-staged contact with
  `DateTimeKind.Unspecified`, ~134 failures/30 d) — fixed; affected events had to be replayed.
  Source: `memory/gotchas/smartsupp-staged-contact-datetime-kind.md`.
- `Smartsupp:AgentId` is bound but unused (reserved for a future bot reply).
- `Pending` status can be stored but cannot be listed (list validator accepts only
  `Open`/`Resolved`).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs` — DI, the 18 webhook reactions, options
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs` — console endpoints
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs` — webhook entry
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs` — admin audit/replay
- `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs` — Smartsupp REST calls
- `backend/src/Anela.Heblo.Persistence/Smartsupp/` — repositories and EF configurations
- `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx`, `frontend/src/api/hooks/useSmartsupp.ts` — UI and polling intervals
- `docs/integrations/smartsupp-webhook.md`, `smartsupp-api.md`, `smartsupp-visitor-api.md` — payload shapes and API findings
