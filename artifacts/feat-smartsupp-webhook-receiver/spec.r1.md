# Specification: Smartsupp Webhook Receiver

## Summary
Replace the broken Hangfire-based polling sync (`SmartsuppSyncJob`, whose watermark table was dropped in migration `20260512190557_DropSmartsuppSyncState`) with an authenticated webhook receiver that ingests Smartsupp conversation and message events in real time. Retire the recurring poll entirely while preserving the underlying API client behind a manual "Sync now" UI button for backfill and disaster recovery.

## Background
Anela Heblo currently mirrors Smartsupp customer-support conversations through a Hangfire recurring job that polls Smartsupp's REST API every two minutes (disabled by default). The watermark/state table backing this job (`SmartsuppSyncState`) was dropped in today's EF migration, which leaves the job throwing at runtime because `SmartsuppRepository` still references it. Smartsupp supports webhook subscriptions (HMAC-SHA256 signed, no UI registration — they activate via email to `support@smartsupp.com`), so the strategic move is event-driven ingestion with a manual fallback rather than fixing the broken poller.

This work delivers (1) a webhook endpoint, (2) MediatR-based event processing with idempotent upserts, (3) deletion of the dead recurring job and its state model, and (4) a "Sync now" manual button that reuses the existing `SmartsuppApiClient` for backfill.

## Functional Requirements

### FR-1: Webhook receiver endpoint
Expose a single, anonymous HTTP endpoint that Smartsupp can POST event envelopes to. The endpoint must capture the raw request body before any JSON deserialization (HMAC must be computed on the exact bytes received), verify the signature, parse the envelope, dispatch to MediatR, and respond `200 OK` (empty body) on success.

**Acceptance criteria:**
- `POST /api/webhooks/smartsupp` exists on `SmartsuppWebhookController` decorated with `[ApiController]`, `[Route("api/webhooks/smartsupp")]`, `[AllowAnonymous]`.
- The controller calls `HttpContext.Request.EnableBuffering()` and reads the body into a `byte[]` before any JSON binding.
- Successful flow (valid HMAC + known event) returns `200 OK` with empty body.
- Valid HMAC + malformed JSON body returns `200 OK` (retrying would not help Smartsupp) and logs an error.
- Valid HMAC + unknown event name returns `200 OK` and logs at Info (future-proof against new event types).
- Invalid HMAC returns `401 Unauthorized` with empty body and logs a warning containing remote IP only (never the signature header, secret, or body).
- Response is sent before any heavy downstream work whenever possible (target sub-second latency to satisfy Smartsupp's retry policy).

### FR-2: HMAC-SHA256 signature verification
Verify `X-Smartsupp-Hmac` against `HMACSHA256(rawBody, WebhookSecret)` using a constant-time comparison.

**Acceptance criteria:**
- Computed signature is hex-encoded, lowercased; the header value is trimmed and lowercased before comparison.
- Comparison uses `CryptographicOperations.FixedTimeEquals` over ASCII bytes of both hex strings.
- Missing/empty header → reject as invalid signature.
- Signature mismatch → reject with `401`.
- Implementation is an inline static helper in the controller (single consumer; no filter/middleware abstraction).

### FR-3: Optional app_id verification (defence in depth)
When `Smartsupp:WebhookAppId` is configured, the controller rejects payloads whose envelope `app_id` does not match.

**Acceptance criteria:**
- If `WebhookAppId` is null/empty, no app_id check is performed.
- If `WebhookAppId` is set and the envelope's `app_id` differs, the request is rejected with `401 Unauthorized` and a warning logged.
- HMAC verification still runs first; app_id check only runs on HMAC pass.

### FR-4: Event dispatch via MediatR
The controller deserializes the envelope to a `ProcessWebhookEventRequest` (containing `EventName`, `Timestamp`, `AccountId`, `AppId`, and raw `Data` as `JsonElement`) and dispatches via MediatR. The handler maps event types to repository operations.

**Acceptance criteria:**
- `ProcessWebhookEventRequest` is a **class** (not record) implementing `IRequest<ProcessWebhookEventResponse>`.
- `ProcessWebhookEventResponse` is a class with `bool Handled` and `string? Reason`.
- Handler dispatch table:
  - `conversation.created`, `conversation.updated`, `conversation.closed` → map `Data` to `SmartsuppConversation`, call `ISmartsuppRepository.UpsertConversationAsync` then `SaveChangesAsync`.
  - `message.created` → extract `conversationId` from `Data`, map to `SmartsuppMessage`, call `ISmartsuppRepository.UpsertMessagesAsync(conversationId, [message], ct)`.
  - Any other event name → log at Info, return `Handled=false, Reason="unknown event"`, no throw.
- Files live in `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/`.
- MediatR registration is automatic via existing scan in `SmartsuppModule.cs`.

### FR-5: Idempotent processing and out-of-order safety
Retries and out-of-order delivery from Smartsupp must not corrupt state.

**Acceptance criteria:**
- Existing `SmartsuppRepository.UpsertConversationAsync` upsert-by-Id behaviour is preserved.
- A timestamp guard is added inside `UpsertConversationAsync`: if `existing.UpdatedAt > incoming.UpdatedAt`, skip the update (still treat operation as successful — do not throw).
- `UpsertMessagesAsync` continues to key on message Id (already idempotent).
- No separate dedupe/event-log table is introduced; upsert + timestamp guard are sufficient.

### FR-6: Configuration and secret management
Extend `SmartsuppOptions` with the webhook secret + optional app_id, and remove the polling interval that no longer has a consumer.

**Acceptance criteria:**
- `SmartsuppOptions.WebhookSecret` added as `string` with `""` default.
- `SmartsuppOptions.WebhookAppId` added as nullable `string?` with `null` default.
- `SmartsuppOptions.PollIntervalMinutes` removed.
- `appsettings.json` Smartsupp section gains `"WebhookSecret": ""` (placeholder) and `"WebhookAppId": ""`, and drops `PollIntervalMinutes`.
- Real secret is stored in the API project's `secrets.json` under `Smartsupp:WebhookSecret` per project convention.
- No secret material is ever logged.

### FR-7: Retire recurring sync job and state model
Delete the broken Hangfire job and the `SmartsuppSyncState` domain entity along with all related plumbing.

**Acceptance criteria:**
- Deleted files: `SmartsuppSyncJob.cs`, `SmartsuppSyncState.cs`, `SmartsuppSyncJobTests.cs`.
- `SmartsuppRepository.GetOrCreateSyncStateAsync` and `SetSyncWatermarkAsync` removed.
- `ISmartsuppRepository` interface entries for those two methods removed.
- `ApplicationDbContext.SmartsuppSyncState` DbSet removed.
- `SmartsuppModule.cs` no longer registers `SmartsuppSyncJob`.
- Solution still compiles with `dotnet build` and there are no remaining references to `SmartsuppSyncState` anywhere in the codebase.

### FR-8: Manual "Sync now" use case (backfill / disaster recovery)
Reuse the polling logic as an on-demand MediatR use case that the UI can trigger.

**Acceptance criteria:**
- New folder `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/` contains `RunManualSyncRequest.cs`, `RunManualSyncResponse.cs`, `RunManualSyncHandler.cs`.
- `RunManualSyncRequest` is a class implementing `IRequest<RunManualSyncResponse>` with optional `DateTime? Since` (handler defaults to `DateTime.UtcNow - 7 days` when null).
- `RunManualSyncResponse` is a class with `int ConversationsProcessed`, `int MessagesProcessed`, `DateTime StartedAt`, `DateTime CompletedAt`.
- Handler ports the existing `SmartsuppSyncJob.ExecuteAsync` logic: paged `ISmartsuppApiClient.SearchConversationsAsync` filtered by `updatedAt > Since`, upsert each conversation, then `GetConversationMessagesAsync` per conversation, upsert messages.
- No watermark persistence — counts are returned in the response only.
- The handler is cancellable via `CancellationToken`.

### FR-9: Manual sync HTTP endpoint
Surface the manual sync use case behind an authenticated controller action on the existing `SmartsuppController`.

**Acceptance criteria:**
- `POST /api/smartsupp/sync` action exists on `SmartsuppController` with `[Authorize]`.
- Action accepts an optional body containing `Since` and dispatches `RunManualSyncRequest` via MediatR.
- Returns `200 OK` with `RunManualSyncResponse` body on success.
- Errors propagate via the project's standard error envelope.

### FR-10: "Sync now" UI button
Add a manual sync trigger to the Smartsupp chats page.

**Acceptance criteria:**
- `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx` gains a "Sync now" button in its page header.
- Button calls the regenerated typed client method for `POST /api/smartsupp/sync` (no `Since` argument; defaults applied on the server).
- Button disables itself while the call is in flight and shows a spinner/loading indicator.
- On success, a toast displays `ConversationsProcessed` and `MessagesProcessed` counts and the chat list refreshes.
- On failure, a toast surfaces the error message.
- OpenAPI TypeScript client is regenerated as part of the standard build (no manual edits to `api-client.ts`).

### FR-11: Observability
Operationally useful logs without leaking visitor PII or secrets.

**Acceptance criteria:**
- Info-level log on successful signature verification: `smartsupp webhook event={Event} account={AccountId} app={AppId}`.
- Warning-level log on signature mismatch: `smartsupp webhook signature mismatch from {RemoteIp}` (no header, secret, or body content).
- Info-level log for unknown event: `smartsupp webhook unknown event={Event}`.
- Handler logs at Debug per event, including only entity Id — no message body, no visitor name/email.
- Manual sync logs start, page progress (Debug), and final counts (Info).

## Non-Functional Requirements

### NFR-1: Performance
- Webhook endpoint p95 latency: ≤ 500 ms end-to-end (parse + verify + upsert + 200 response) under normal load.
- Smartsupp documents non-2xx triggering retries; the endpoint must avoid synchronous long-running work that risks exceeding Smartsupp's read timeout.
- Manual sync handler must stream / page through API results rather than loading entire dataset into memory.

### NFR-2: Security
- HMAC-SHA256 verification of every webhook call using `CryptographicOperations.FixedTimeEquals` (timing-attack resistant).
- Secret is stored in `secrets.json` locally and configuration provider (Key Vault / Azure App Configuration / appsettings overrides) in deployed environments — never committed to source.
- Manual sync endpoint requires standard application authentication (`[Authorize]`).
- Webhook endpoint is anonymous by necessity (Smartsupp does not send a bearer token) — HMAC + optional `app_id` check are the only authentication mechanisms.
- No PII (visitor names, emails, message text) in any log line at Info or above.

### NFR-3: Reliability
- All processing paths must be idempotent (Smartsupp retries on non-2xx).
- Out-of-order events are tolerated via the `UpdatedAt` timestamp guard.
- Webhook handler must never throw an unhandled exception that would bubble out as a 5xx (which would induce retries). Map parse failures and downstream errors to a 200 response with logged context, except for HMAC failures which legitimately deserve `401`.

### NFR-4: Maintainability
- Vertical-slice layout under `Features/Smartsupp/UseCases/` matches existing project conventions.
- DTOs (request/response classes that cross the API boundary, including `ProcessWebhookEventRequest` and `RunManualSync*`) are classes, not records, per the project rule for OpenAPI compatibility.
- New code targets ≥ 80% unit test coverage; HMAC verifier and dispatch handler are 100% covered for branch coverage.

### NFR-5: Operational
- Both staging and production webhook URLs are registered in the same Smartsupp support email to minimize back-and-forth.
- A documented runbook step exists for capturing the returned `app_id` and shared secret into each environment's configuration.

## Data Model
No schema additions. Removed entity and persistence touchpoints:

- **Removed:** `SmartsuppSyncState` (domain entity), corresponding EF configuration if any, `ApplicationDbContext.SmartsuppSyncState` DbSet. Migration `20260512190557_DropSmartsuppSyncState` has already dropped the physical table.
- **Unchanged:** `SmartsuppConversation` (keyed by `Id`, has `UpdatedAt` used for guard), `SmartsuppMessage` (keyed by `Id`, scoped to `ConversationId`).

In-memory transport types:

- `ProcessWebhookEventRequest { string EventName; DateTime Timestamp; string AccountId; string AppId; JsonElement Data; }` — class, MediatR request.
- `ProcessWebhookEventResponse { bool Handled; string? Reason; }` — class.
- `RunManualSyncRequest { DateTime? Since; }` — class, MediatR request.
- `RunManualSyncResponse { int ConversationsProcessed; int MessagesProcessed; DateTime StartedAt; DateTime CompletedAt; }` — class.

## API / Interface Design

### Webhook ingress
```
POST /api/webhooks/smartsupp
Headers:
  Content-Type: application/json
  X-Smartsupp-Hmac: <lowercase hex HMAC-SHA256(raw_body, WebhookSecret)>
Body (envelope):
  {
    "type": "event_callback",
    "event": "conversation.created" | "conversation.updated" | "conversation.closed" | "message.created" | <other>,
    "timestamp": "<ISO 8601 UTC>",
    "account_id": "<string>",
    "app_id": "<string>",
    "data": { ... event-specific payload ... }
  }
Responses:
  200 OK   — empty body. Valid signature; event handled or known-but-unhandled.
  401      — empty body. HMAC mismatch, missing header, or app_id mismatch (when configured).
```

### Manual sync
```
POST /api/smartsupp/sync
Authorization: <bearer or cookie session per project standard>
Body (optional):
  { "since": "2026-05-06T00:00:00Z" }   // defaults to now - 7 days when omitted
Response 200:
  {
    "conversationsProcessed": 42,
    "messagesProcessed": 137,
    "startedAt": "2026-05-13T12:00:00Z",
    "completedAt": "2026-05-13T12:00:18Z"
  }
```

### Event subscriptions (Smartsupp side)
- `conversation.created`
- `conversation.updated`
- `conversation.closed`
- `message.created`

### UI flow
1. Operator opens `SmartsuppChatsPage` and clicks **Sync now** in the header.
2. Button disables, spinner appears.
3. Frontend invokes generated client method for `POST /api/smartsupp/sync`.
4. On 200, toast displays `Synced N conversations / M messages` and chat list refetches.
5. On error, toast surfaces the message; button re-enables.

## Dependencies
- **Smartsupp** webhook delivery — registration is via emailed support request (`support@smartsupp.com`); no self-service UI. Activation gates real-end-to-end verification.
- **`SmartsuppApiClient`** (existing adapter) and its DI registration — retained, used only by `RunManualSyncHandler`.
- **MediatR** auto-scan already wired through `SmartsuppModule.cs` — handlers register automatically when placed under `Features/Smartsupp/UseCases/`.
- **EF Core / `ApplicationDbContext`** for upserts; `SaveChangesAsync` is called by the handlers.
- **`ISmartsuppRepository`** (existing) — interface trimmed (drop sync-state methods), implementation gains the `UpdatedAt` guard.
- **`Microsoft.AspNetCore.Authorization` `[Authorize]`** for the manual sync endpoint (existing app-wide auth).
- **OpenAPI TypeScript client generation** (existing build step per `docs/development/api-client-generation.md`) — must run after backend changes so the FE button has a typed call site.

## Out of Scope
- Adding/removing Smartsupp event subscriptions beyond the four agreed types (`conversation.created`, `conversation.updated`, `conversation.closed`, `message.created`).
- A dedicated webhook delivery-log / event-archive table.
- A UI for replaying or browsing raw webhook payloads.
- Multi-tenant / multi-`app_id` support — single Smartsupp account assumed.
- Rate limiting or queue-based async processing of webhook events (synchronous in-process upsert is sufficient at expected volumes).
- Automatic registration of webhook URLs with Smartsupp — registration remains a manual support email per Smartsupp's API constraints.
- Backporting / rehydrating historical conversations outside the manual sync window (operator may re-run "Sync now" with a deeper `Since` via direct API call if needed).
- Removing the `SmartsuppApiClient` adapter or its DI wiring.

## Open Questions
None.

## Status: COMPLETE