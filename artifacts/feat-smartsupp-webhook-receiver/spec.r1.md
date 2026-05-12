# Specification: Smartsupp Webhook Receiver

## Summary
Replace the broken Hangfire-based `SmartsuppSyncJob` (currently failing due to the dropped `SmartsuppSyncState` watermark table) with an HMAC-authenticated webhook endpoint that ingests conversation and message events from Smartsupp in real time. The existing API-client sync logic is retained behind a manual "Sync now" UI button for backfill and disaster recovery, and the recurring poll is removed.

## Background
The Smartsupp integration historically polled the Smartsupp REST API every 2 minutes using a Hangfire recurring job, persisting a watermark in `SmartsuppSyncState`. Migration `20260512190557_DropSmartsuppSyncState` dropped that table while the job code still references it via `SmartsuppRepository`, so the job throws at runtime. Smartsupp supports an HMAC-signed webhook delivery model (documented at https://docs.smartsupp.com/rest-api/webhooks/) which is a better fit: real-time delivery, no polling overhead, and signature-based authenticity. Smartsupp has no self-service webhook UI — registration is an email request to `support@smartsupp.com` that returns an `app_id` and shared secret per registered URL. Staging and production URLs will be registered together in the initial request.

## Functional Requirements

### FR-1: Authenticated webhook endpoint
A new `POST /api/webhooks/smartsupp` endpoint accepts Smartsupp event callbacks. The endpoint is anonymous (no Heblo auth) — authenticity is established via HMAC-SHA256 over the raw request body using the shared secret, supplied in the `X-Smartsupp-Hmac` header as lowercase hex.

**Acceptance criteria:**
- A request with a valid HMAC for one of the subscribed events returns `200 OK` with an empty body and the corresponding entity is upserted.
- A request whose computed HMAC does not match the header returns `401 Unauthorized` with no body and writes a warning log (no secret/header/body content in the log).
- A request with a valid HMAC but malformed JSON returns `200 OK` (re-delivery would not help) and writes an error log.
- A request with a valid HMAC for an unsubscribed/unknown event name returns `200 OK` and writes an info log; no upsert occurs.
- HMAC comparison is constant-time (`CryptographicOperations.FixedTimeEquals`).
- The raw body bytes used for HMAC are exactly the bytes received (request buffering enabled before any model binding).
- If `Smartsupp:WebhookAppId` is configured, payloads whose envelope `app_id` does not match are rejected (`401`, warning log).

### FR-2: Supported events
The endpoint handles the following Smartsupp events:
- `conversation.created`
- `conversation.updated`
- `conversation.closed`
- `message.created`

**Acceptance criteria:**
- For each conversation event, the `data` payload is mapped to `SmartsuppConversation` and persisted via `ISmartsuppRepository.UpsertConversationAsync` + `SaveChangesAsync`.
- For `message.created`, the `conversationId` is extracted from `data`, the message is mapped to `SmartsuppMessage`, and `ISmartsuppRepository.UpsertMessagesAsync(conversationId, [msg], ct)` is called.
- Any other event name returns `Handled=false` from the handler and does not throw.

### FR-3: Idempotency and ordering
Repeated delivery of the same event must not cause duplicate rows or stale overwrites.

**Acceptance criteria:**
- Conversation upsert is keyed on `Id`; redelivery of the same payload is a no-op beyond the existing upsert semantics.
- `UpsertConversationAsync` skips the write when `existing.UpdatedAt > incoming.UpdatedAt` (handles out-of-order delivery).
- Message upsert is keyed on message `Id`; redelivery does not produce duplicates.
- No separate dedupe/seen-event table is introduced.

### FR-4: Removal of the recurring poll
The Hangfire-based recurring sync is removed entirely.

**Acceptance criteria:**
- `SmartsuppSyncJob`, `SmartsuppSyncState`, `SmartsuppSyncJobTests`, the `GetOrCreateSyncStateAsync` / `SetSyncWatermarkAsync` methods on `ISmartsuppRepository` and its EF implementation, the `SmartsuppSyncState` DbSet, and the job's DI registration in `SmartsuppModule` are all deleted.
- `SmartsuppOptions.PollIntervalMinutes` is removed; the corresponding `appsettings.json` key is removed.
- A solution-wide search for `SmartsuppSyncState` and `SmartsuppSyncJob` returns no production-code references after the change.

### FR-5: Manual "Sync now" backfill
The existing polling logic (paged conversation fetch + per-conversation message fetch) is exposed as a manual, on-demand operation triggered from the UI for backfill or recovery during outages.

**Acceptance criteria:**
- A new MediatR use case `RunManualSync` accepts an optional `Since` (default: `now - 7 days`) and returns `{ ConversationsProcessed, MessagesProcessed, StartedAt, CompletedAt }`.
- The handler calls `ISmartsuppApiClient.SearchConversationsAsync` paged by `updatedAt > since`, upserts each conversation, then calls `GetConversationMessagesAsync` and upserts messages — porting the existing `SmartsuppSyncJob.ExecuteAsync` behaviour without watermark persistence.
- A new `POST /api/smartsupp/sync` action on `SmartsuppController` (authorized) dispatches the request and returns the response.
- A "Sync now" button appears in the header of `SmartsuppChatsPage`. While the request is in flight the button is disabled. On success a toast displays the counts and the chat list refreshes; on failure a toast surfaces the error.

### FR-6: Configuration
`SmartsuppOptions` exposes the webhook secret and optional app-id guard; `PollIntervalMinutes` is removed.

**Acceptance criteria:**
- `SmartsuppOptions` has `string WebhookSecret { get; set; } = ""` and `string? WebhookAppId { get; set; }`; `PollIntervalMinutes` is removed.
- `appsettings.json` `Smartsupp` section adds `"WebhookSecret"` (placeholder pointing to user secrets) and `"WebhookAppId": ""`, and drops `PollIntervalMinutes`.
- The real secret is stored in the API project's `secrets.json` under `Smartsupp:WebhookSecret` for local development; in staging/production it is supplied via the environment's secret store.

## Non-Functional Requirements

### NFR-1: Performance
- The webhook endpoint must respond `2xx` quickly to avoid Smartsupp retries. Target: p95 ≤ 500 ms end-to-end under normal load; the controller path itself (HMAC + parse + single upsert + commit) should not exceed ~200 ms in steady state.
- Manual sync is bounded by external API paging; it runs synchronously inside the request and is acceptable up to ~30 s for a 7-day window. If consistently slower in production, revisit by moving to a background job — out of scope here.

### NFR-2: Security
- HMAC verification is mandatory and constant-time.
- Optional `WebhookAppId` check provides defence in depth against secret reuse across tenants.
- Secrets are never logged. Warning logs on signature mismatch include only the remote IP, never the header value, body, or secret.
- Handler-level logs use entity IDs only; visitor PII is not logged above `Debug`.
- The webhook endpoint is `AllowAnonymous` but is the only anonymous endpoint added; the manual-sync endpoint requires the standard Heblo authorization.

### NFR-3: Reliability
- Smartsupp retries non-2xx responses. The endpoint returns `200 OK` for valid-signature requests with unknown events or malformed bodies to avoid retry storms on irrecoverable conditions.
- Idempotent upserts + timestamp guard ensure retries and out-of-order delivery cannot corrupt state.

### NFR-4: Observability
- Info log on every authenticated event: `smartsupp webhook event={Event} account={AccountId} app={AppId}`.
- Warning log on signature mismatch with `RemoteIp` only.
- Info log on unknown event names.
- Debug log per event in the handler, including the entity Id only.

### NFR-5: Testability
- ≥ 80% coverage on new code (HMAC verifier, `ProcessWebhookEventHandler`, `RunManualSyncHandler`, webhook controller wiring).

## Data Model
No schema changes are introduced by the webhook itself — Smartsupp payload data flows into the existing `SmartsuppConversation` and `SmartsuppMessage` tables via the existing repository methods.

Schema removal (already partially executed by migration `20260512190557_DropSmartsuppSyncState`):
- `SmartsuppSyncState` entity, DbSet, and repository helpers are removed from code to match the already-dropped table.

Behavioural change to existing model:
- `SmartsuppRepository.UpsertConversationAsync` gains a guard: when `existing.UpdatedAt > incoming.UpdatedAt`, the update is skipped. The entity shape is unchanged.

## API / Interface Design

### Webhook endpoint
```
POST /api/webhooks/smartsupp
Headers:
  Content-Type: application/json
  X-Smartsupp-Hmac: <lowercase hex HMAC-SHA256 of raw body using shared secret>
Body (Smartsupp envelope):
  {
    "type": "event_callback",
    "event": "conversation.created" | "conversation.updated" | "conversation.closed" | "message.created" | <other>,
    "timestamp": "<ISO 8601>",
    "account_id": "...",
    "app_id": "...",
    "data": { ... }
  }
Responses:
  200 OK (empty body) — signature valid (handled, unhandled-event, or malformed JSON)
  401 Unauthorized (empty body) — signature mismatch or app_id mismatch when configured
```

### Manual sync endpoint
```
POST /api/smartsupp/sync         [Authorize]
Body (optional): { "since": "<ISO 8601 datetime>" }   // default: now - 7 days
Response 200:
  {
    "conversationsProcessed": <int>,
    "messagesProcessed": <int>,
    "startedAt": "<ISO 8601>",
    "completedAt": "<ISO 8601>"
  }
```

### MediatR contracts
- `ProcessWebhookEventRequest : IRequest<ProcessWebhookEventResponse>` — class (not record), fields: `EventName`, `Timestamp` (`DateTime`), `AccountId`, `AppId`, `Data` (`JsonElement`).
- `ProcessWebhookEventResponse` — class: `bool Handled`, `string? Reason`.
- `RunManualSyncRequest : IRequest<RunManualSyncResponse>` — class: `DateTime? Since`.
- `RunManualSyncResponse` — class: `int ConversationsProcessed`, `int MessagesProcessed`, `DateTime StartedAt`, `DateTime CompletedAt`.

(DTOs are classes per project rule — never records, to keep OpenAPI client generation deterministic.)

### Controller flow (webhook)
1. `HttpContext.Request.EnableBuffering()`, read body into `byte[]` via `StreamReader`/`MemoryStream` before any model binding.
2. Compute `HMACSHA256` over raw bytes; compare constant-time against `X-Smartsupp-Hmac` (lowercase hex, trimmed).
3. On mismatch → `LogWarning` (remote IP only) → `401`.
4. Parse JSON envelope; on parse failure → `LogError` → `200`.
5. If `WebhookAppId` configured and envelope `app_id` differs → `LogWarning` → `401`.
6. `LogInformation` with event/account/app.
7. Dispatch `ProcessWebhookEventRequest` via MediatR.
8. Return `200 OK` regardless of `Handled`.

### Frontend changes
- `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx` gains a "Sync now" header button. The handler calls the regenerated typed client for `POST /api/smartsupp/sync`. Uses absolute URL via `${apiClient.baseUrl}…` per project rule. Disabled while the call is in flight. Success toast: `"Synced {C} conversations, {M} messages"`. Failure toast surfaces the error message. After success, the chat list is refetched.
- The OpenAPI TypeScript client is regenerated by the standard build step.

## Dependencies
- **External:** Smartsupp REST/webhook platform — webhook URL registration is a manual email request to `support@smartsupp.com`; they reply with `app_id` and shared secret.
- **Internal — retained:** `ISmartsuppApiClient` (used by manual sync), `ISmartsuppRepository` (already supports `UpsertConversationAsync` and `UpsertMessagesAsync`), MediatR auto-registration in `SmartsuppModule`, EF `ApplicationDbContext`, ASP.NET Core authorization for the manual-sync endpoint, the existing toast/notification component used elsewhere on the page.
- **Internal — removed:** Hangfire recurring registration for `SmartsuppSyncJob`, the `SmartsuppSyncState` entity and its DbSet, repository sync-state helpers, `PollIntervalMinutes` option.
- **Build:** OpenAPI TypeScript client regeneration on build (existing pipeline).

## Out of Scope
- Self-service webhook URL configuration UI — registration remains an out-of-band email request to Smartsupp.
- Webhook delivery dashboards, replay tooling, or visibility into Smartsupp's own delivery log.
- Persistence of a seen-event table or any new dedupe schema — upsert + timestamp guard are sufficient.
- Changes to the underlying `SmartsuppConversation` / `SmartsuppMessage` shape, mapping helpers, or storage layout beyond the timestamp guard.
- Moving manual sync into a background job (e.g., Hangfire one-off). The handler runs synchronously inside the HTTP request.
- Rate limiting on `POST /api/webhooks/smartsupp` beyond standard host protections.
- Migrating other Smartsupp data (e.g., agents, contacts) into the webhook flow — only the four listed events are in scope.
- Multi-tenant `app_id` routing. `WebhookAppId` is a single optional guard value.

## Open Questions
None.

## Status: COMPLETE