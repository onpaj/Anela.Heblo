---
process: flow-smartsupp-reply
kind: workflow
module: smartsupp
summary: Support operator answers an e-shop chat from Heblo — optional AI draft from the knowledge base, send to the customer and close the conversation through the Smartsupp API, with live presence, customer/visitor lookups and draft quality feedback.
owns:
  - backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/Pipeline/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/CloseConversation/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RecordPresence/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RemovePresence/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/Presence/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SubmitDraftReplyFeedback/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetDraftReplyFeedbackList/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetVisitorInfo/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/**
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs
  - backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppNameHelper.cs
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/**
verified_at: "5e993f9e2"
related: [sync-smartsupp-webhooks]
---

# Answering a Smartsupp chat from Heblo (draft → send → close)

## Purpose
Lets the support team handle e-shop chats entirely from **Customer Support → Smartsupp Chats**
(`/customer/smartsupp`): understand the customer (Shoptet account, browser, visited pages,
previous chats), get an **AI-written draft reply** ("Návrh") grounded in Anela's knowledge base
and written in the tone of earlier agent replies, edit it, send it to the customer **under the
operator's own Smartsupp agent identity**, and close the chat ("Uzavřít konverzaci"). Presence
badges show when a colleague (in Heblo or in the native Smartsupp app) already has the chat open,
so two people don't answer the same customer. Operators rate each draft 1–5 for precision and
style; those ratings plus the actually-sent text build the AI evaluation dataset shown on
**Marketing → Feedback**, tab "Smartsupp".

## Trigger
User-driven, all from `/customer/smartsupp`, permission `customer.smartsupp.read` (feature
`Customer_Smartsupp` has only a read level; read holders may also send/close/draft — intentional):

| Step | UI | Endpoint | Side effect |
|---|---|---|---|
| Open a chat | click in list | `POST /api/smartsupp/conversations/{id}/presence` every 20 s; `DELETE …/presence` on leave (keepalive fetch) | `SmartsuppConversationPresences` row (`Source = Heblo`) |
| Contact panel | "Detail kontaktu" | `GET …/{id}/shoptet-info`, `GET …/{id}/visitor-info` | Shoptet + Smartsupp REST reads; visitor cache written to `SmartsuppConversations` |
| AI draft | composer, optional topic ("Vyberte téma") | `POST …/{id}/draft-reply` `{ topic? }` | KnowledgeBase search + LLM call; `RagInteractionLogs` row |
| Send | composer send | `POST …/{conversationId}/messages` `{ content, draftLogId? }` | **Message delivered to the customer** via Smartsupp; `RagInteractionLogs.SentAnswer` |
| Close | "Uzavřít konverzaci" | `POST …/{id}/close` | **Conversation closed in Smartsupp** |
| Rate draft | feedback widget | `POST /api/smartsupp/draft-reply/feedback` | scores on the `RagInteractionLogs` row |
| Review ratings | `/marketing/feedback` tab Smartsupp | `GET /api/smartsupp/draft-reply/feedback/list` | read only |

Conversation state does not change locally on send/close: Heblo waits for Smartsupp's own
webhook (`conversation.agent_replied`, `conversation.closed`) to update the list
(see `sync-smartsupp-webhooks`). The list polls every 10 s, the open detail every 30 s.

## Data flow
**AI draft** (`GenerateDraftReplyHandler`):
1. Load conversation + messages from `SmartsuppConversations` / `SmartsuppMessages` (404 →
   2701 `SmartsuppConversationNotFound`).
2. Retrieval query = trimmed topic, else the customer's last 3 real messages joined by newline
   (page-visit "system" messages excluded); none → 2703 `SmartsuppConversationEmpty`. Truncated
   to 2000 chars.
3. Transcript: all messages in time order, labelled `Zákazník:` / `Agent:` / `Bot:`
   (system/trigger messages and empty texts skipped).
4. KnowledgeBase search: `ISmartsuppKnowledgeSource` → `KnowledgeBaseSmartsuppKnowledgeSource`
   → MediatR `SearchDocumentsRequest { TopK = 5 }`. No hits → context
   "(žádný relevantní kontext nebyl nalezen)".
5. System prompt `SmartsuppDraftReply:DraftReplySystemPrompt` (Czech default in code) with
   `{agent_name}` (operator's first name, "Anela" when unknown), `{transcript}`, `{context}`,
   `{topic}` ("(neuvedeno)" when none); user message "Napiš návrh odpovědi agenta na poslední
   zprávu zákazníka." → default `IChatClient` (Anthropic, model `KnowledgeBase:ChatModel`).
   Network/timeout errors → 2702 `SmartsuppDraftReplyAiUnavailable`.
6. `DraftReplyLoggingBehavior` saves a `RagInteractionLogs` row (feature `SmartsuppDraftReply`,
   user, duration, query, prompt, answer, conversation id, topic) and returns its id as `id`;
   logging failures are swallowed. Response also lists the 5 sources (filename, 200-char excerpt,
   score).

**Send** (`SendMessageHandler`):
1. Validate: content 1–4000 chars. Conversation must exist locally (2701).
2. Operator e-mail → Smartsupp agent id via `Smartsupp:SendMessage:AgentMap`
   (case-insensitive); missing → 2707 `SmartsuppAgentMappingNotFound`, nothing sent.
3. `POST {BaseUrl}conversations/{id}/messages` body `{ "content": { "type": "text", "text": … },
   "agent_id": … }` — the customer sees the message from that agent's Smartsupp profile.
   Any HTTP error status (4xx included), network error or timeout → 2706
   `SmartsuppSendMessageUnavailable`.
4. If `draftLogId` was sent: `RagInteractionLogs.SentAnswer`, `SentAt`, `WasEdited`
   (= sent text differs from the AI answer, ordinal) — best effort.
5. Returns Smartsupp's message id and creation time. No local `SmartsuppMessages` row is written.

**Close** (`CloseConversationHandler`): conversation must exist locally (2701) →
`PATCH {BaseUrl}conversations/{id}/close`. 5xx / network / timeout → 2708
`SmartsuppCloseConversationUnavailable`; 4xx surfaces as a real error (contract bug).

**Presence** (`SmartsuppPresenceService`): operator key = mapped Smartsupp agent id, else e-mail,
else user id; display name = user name. Upsert keeps `EnteredAt`, refreshes `LastSeenAt`.
Active = Heblo rows seen within `HeartbeatTtlSeconds` (90 s), Smartsupp rows within
`SmartsuppTtlMinutes` (30 min). One person present from both sources is collapsed into one badge.
List and detail endpoints attach `activeViewers`.

**Shoptet customer card** (`GetSmartsuppContactShoptetInfoHandler`): reads conversation
`VariablesJson` (variables the e-shop's Smartsupp snippet sends): `shoptet_user_guid`, else
`shoptet_guid` → `IShoptetCustomerClient.GetCustomerByGuidAsync` → Shoptet
`GET /api/customers/{guid}`. Returns full name, e-mail, customer group, price list, default
shipping address and `shoptet_cart_updated_at`. No GUID or unknown customer → 200 with
`contactInfo = null`.

**Visitor card** (`GetVisitorInfoHandler`): needs `VisitorId` (else 2705
`SmartsuppVisitorNotFound`). If `VisitorInfoFetchedAt` is empty or older than 24 h →
`GET {BaseUrl}visitors/{visitorId}` and cache user agent, OS, browser, version, visits count on
the conversation row. Chats count = other conversations of the same contact (max 20) + 1. Page
history = distinct `PageUrl` of the conversation's messages, first-visit order.

**Feedback** (`SubmitDraftReplyFeedbackHandler`): log must exist with feature
`SmartsuppDraftReply` (else 2709), belong to the caller (else 403), and not be rated yet (else
2710). Stores `PrecisionScore`, `StyleScore` (1–5) and comment. The list endpoint pages 10/20/50
rows, sorts by `CreatedAt`/`PrecisionScore`/`StyleScore`, filters by has-feedback and user, and
returns aggregate stats.

## Logic & formulas
- Draft: top-5 chunks, 2000-char query cap, 200-char source excerpts, last 3 customer messages
  as fallback query. Draft language, tone and signature rules are in the prompt (always Czech,
  imitate prior `Agent:` messages, formal "Dobrý den" when none, sign as `{agent_name}`, answer
  only from the context, otherwise promise a follow-up).
- Smartsupp REST calls retry only on HTTP 429 (3×, exponential from 2 s or `Retry-After`);
  timeout 30 s.
- Presence rows older than 1 day are purged nightly (`job-smartsupp-webhook-audit-cleanup`).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Smartsupp:SendMessage:AgentMap` | 5 entries: `ondra@`, `pepi@`, `bara@`, `janka@`, `eliska@anela.cz` → Smartsupp agent ids | Who may send, and as which Smartsupp agent; also the presence identity |
| `SmartsuppDraftReply:DraftReplySystemPrompt` | Czech prompt in `SmartsuppDraftReplyOptions` (section absent from appsettings) | LLM system prompt with placeholders |
| `KnowledgeBase:ChatModel` | `claude-sonnet-4-6` | Model used by the default chat client (shared with KnowledgeBase) |
| `Smartsupp:Presence:HeartbeatTtlSeconds` | `90` (code default) | Heblo presence lifetime without heartbeat (UI beats every 20 s) |
| `Smartsupp:Presence:SmartsuppTtlMinutes` | `30` (code default) | Safety TTL for native-app presence if `agent_left` never arrives |
| `Smartsupp:ApiToken` / `Smartsupp:BaseUrl` / `Smartsupp:HttpTimeoutSeconds` | secret / `https://api.smartsupp.com/v2/` / `30` | Smartsupp REST access |
| `Smartsupp:AgentId` | unset | Bound but unused (reserved for a future bot) |

## Runtime facts
None.

## Known quirks
- **Unmapped operators can't send.** A new support colleague must be added to
  `Smartsupp:SendMessage:AgentMap` (e-mail → Smartsupp agent id) or every send fails with 2707.
  Unmapped users still appear in presence, keyed by e-mail, so they don't merge with their
  native-app presence.
- **Sent message appears only after the webhook.** Send/close never touch local tables; if the
  `agent_replied` / `closed` webhook is lost or fails, Heblo keeps showing the old state until a
  replay.
- **Shoptet card has no orders.** `RecentOrders` is always empty (TODO; the e-mail fallback was
  removed — see `docs/integrations/shoptet-api.md`). Customers only identified by e-mail (no
  Shoptet GUID variable, e.g. Messenger chats) get no card at all.
- **Visitor cache is short-lived in practice**: every conversation webhook overwrites the
  `Visitor*` columns with null (see `sync-smartsupp-webhooks`), so an active chat re-fetches the
  visitor from Smartsupp on almost every card open, not once per 24 h.
- `GetVisitorInfo` does not catch Smartsupp REST errors; a Smartsupp outage makes the visitor card
  request fail (500) rather than show stale data.
- Draft logging is skipped for failed generations; a draft generated while the log save fails has
  no `id`, so its send and rating cannot be linked.
- The draft prompt's KB context is the plain-text chunk content; KnowledgeBase's product-link
  enrichment (keyed chat client) is not applied to Smartsupp drafts — it uses the unkeyed client.

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs` — all console endpoints and auth notes
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs`, `ConversationTranscriptBuilder.cs`, `SmartsuppDraftReplyOptions.cs` — draft pipeline and prompt
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Pipeline/DraftReplyLoggingBehavior.cs` — eval-log row
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageHandler.cs` — agent mapping and send
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/CloseConversation/CloseConversationHandler.cs` — close and error mapping
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Presence/SmartsuppPresenceService.cs` — presence identity and TTLs
- `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs` — REST calls, 429 retry
- `frontend/src/api/hooks/useSmartsupp.ts`, `frontend/src/components/customer-support/smartsupp/ChatComposer.tsx` — polling, heartbeat, composer
