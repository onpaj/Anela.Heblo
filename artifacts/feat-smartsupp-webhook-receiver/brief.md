# Smartsupp Webhook Receiver

## Context

We currently sync Smartsupp conversations and messages via a Hangfire recurring job (`SmartsuppSyncJob`, every 2 min, disabled by default). Today's migration `20260512190557_DropSmartsuppSyncState` dropped the watermark table, which leaves the job broken at runtime — it still references `SmartsuppSyncState` via `SmartsuppRepository`. The intent is to move to a webhook-driven, real-time model.

Goal: stand up an authenticated webhook endpoint that Smartsupp pushes events to, retire the recurring poll, and keep the existing API client wired up behind a manual "sync now" button for backfill / disaster recovery.

Smartsupp spec (verified at https://docs.smartsupp.com/rest-api/webhooks/):
- Envelope: `{ "type": "event_callback", "event": "<name>", "timestamp": "<ISO>", "account_id": "...", "app_id": "...", "data": {...} }`
- Signature: `X-Smartsupp-Hmac` = HMAC-SHA256(raw_body, app_secret), hex
- Response: 2xx ASAP, non-2xx is retried by Smartsupp
- Registration: **no UI** — email `support@smartsupp.com` with public URL + event list; they reply with `app_id` and shared secret

User decisions:
- Subscribe to `conversation.created`, `conversation.updated`, `conversation.closed`, `message.created`
- Delete the recurring poll, but expose the same sync logic via a **manual button** in the UI
- Register staging + production URLs together in the initial Smartsupp support request

## Implementation

### 1. Webhook controller

**New:** `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs`
- `[ApiController] [Route("api/webhooks/smartsupp")] [AllowAnonymous]`
- Single `POST` action. Read raw body via `HttpContext.Request.EnableBuffering()` + `StreamReader` into `byte[]` before any JSON binding (HMAC must hash exact bytes received).
- Order: read body → verify HMAC → parse JSON envelope → dispatch via MediatR → return `200 OK` (empty body).
- Bad signature → log warning, return `401 Unauthorized`, no body.
- Valid signature + malformed JSON → log error, return `200 OK` (retrying wouldn't help Smartsupp).
- Valid signature + unknown `event` name → log info, return `200 OK` (don't reject — future-proof against new events).

### 2. HMAC verification

Inline static helper in the controller (one consumer, filter/middleware is over-engineered).

```csharp
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
var computedHex = Convert.ToHexString(hmac.ComputeHash(rawBody)).ToLowerInvariant();
var headerHex = (headerValue ?? "").Trim().ToLowerInvariant();
return CryptographicOperations.FixedTimeEquals(
    Encoding.ASCII.GetBytes(computedHex),
    Encoding.ASCII.GetBytes(headerHex));
```

### 3. Options & secrets

Extend `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs`:
- Add `string WebhookSecret { get; set; } = ""`
- Add `string? WebhookAppId { get; set; }` — defence in depth: if set, reject payloads whose `app_id` doesn't match
- Remove `PollIntervalMinutes` (unused after job deletion)

`backend/src/Anela.Heblo.API/appsettings.json` Smartsupp section: add `"WebhookSecret": "-- stored in secrets.json --"` and `"WebhookAppId": ""`; drop `PollIntervalMinutes`.

Real secret goes into the API project's `secrets.json` under `Smartsupp:WebhookSecret` (per project rule: edit secrets.json directly).

### 4. MediatR pipeline

**New folder:** `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/`
- `ProcessWebhookEventRequest.cs` — class (not record), `IRequest<ProcessWebhookEventResponse>`. Fields: `EventName`, `Timestamp` (DateTime), `AccountId`, `AppId`, `Data` (`JsonElement`).
- `ProcessWebhookEventResponse.cs` — class, `bool Handled`, `string? Reason`.
- `ProcessWebhookEventHandler.cs` — switch on `EventName`:
  - `conversation.created` / `conversation.updated` / `conversation.closed`: map `Data` → `SmartsuppConversation`, call `ISmartsuppRepository.UpsertConversationAsync` + `SaveChangesAsync`.
  - `message.created`: extract `conversationId` from `Data`, map to `SmartsuppMessage`, call `ISmartsuppRepository.UpsertMessagesAsync(conversationId, [msg], ct)`.
  - default: log info, return `Handled=false`.

MediatR auto-scan already wired in `SmartsuppModule.cs`.

### 5. Idempotency

- `SmartsuppRepository.UpsertConversationAsync` already does upsert-by-Id (lines 43–66) — idempotent on retries.
- Add a timestamp guard inside `UpsertConversationAsync`: if `existing.UpdatedAt > incoming.UpdatedAt`, skip (handles out-of-order delivery).
- Messages: `UpsertMessagesAsync` already keyed on Id.
- No separate dedupe table — upserts + timestamp guard suffice.

### 6. Retire recurring job, keep manual sync

Delete:
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs` (IRecurringJob)
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppSyncState.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs`

Edit:
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` lines 92–109: remove `GetOrCreateSyncStateAsync` / `SetSyncWatermarkAsync`.
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` lines 24–26: drop the two methods.
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` line 124: drop `SmartsuppSyncState` DbSet.
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs` line 19: drop `AddScoped<SmartsuppSyncJob>()` registration.

**New manual-sync use case** under `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/`:
- `RunManualSyncRequest.cs` (class, `IRequest<RunManualSyncResponse>`): optional `DateTime? Since` (default: now - 7 days).
- `RunManualSyncResponse.cs` (class): `int ConversationsProcessed`, `int MessagesProcessed`, `DateTime StartedAt`, `DateTime CompletedAt`.
- `RunManualSyncHandler.cs`: port the existing logic from `SmartsuppSyncJob.ExecuteAsync` — call `ISmartsuppApiClient.SearchConversationsAsync` paged by `updatedAt > since`, upsert each, then `GetConversationMessagesAsync` + upsert messages. No watermark persistence; the response carries counts back to the UI.

**New controller action** on existing `SmartsuppController.cs`:
- `[HttpPost("sync")] [Authorize]` — dispatches `RunManualSyncRequest` via MediatR, returns `RunManualSyncResponse`.

**Frontend** in `frontend/src/components/customer-support/smartsupp/`:
- Add a "Sync now" button to `pages/SmartsuppChatsPage.tsx` header. Calls the regenerated `POST /api/smartsupp/sync` client. Shows toast with counts on success; disables itself during the call.
- After the BE changes, regenerate the OpenAPI client via the existing build step (per `docs/development/api-client-generation.md`).

The `SmartsuppApiClient` adapter and its DI wiring stay — manual sync depends on it.

### 7. Logging

- Controller `LogInformation("smartsupp webhook event={Event} account={AccountId} app={AppId}", ...)` after HMAC pass.
- `LogWarning("smartsupp webhook signature mismatch from {RemoteIp}", ...)` on HMAC fail (never log secret, header, or body).
- `LogInformation("smartsupp webhook unknown event={Event}", ...)` for unhandled types.
- Handler logs at `Debug` per event with entity Id only. No visitor PII at Info level.

### 8. Testing

Unit tests under `backend/test/Anela.Heblo.Tests/Features/Smartsupp/`:
- `SmartsuppHmacVerifierTests.cs` — known-vector match, tampered body, wrong secret, missing header, mixed-case hex.
- `ProcessWebhookEventHandlerTests.cs` — dispatch for each event type using mocked `ISmartsuppRepository`; unknown event returns `Handled=false` without throwing; out-of-order timestamp skipped.
- `RunManualSyncHandlerTests.cs` — paged fetch + upsert with mocked `ISmartsuppApiClient` and `ISmartsuppRepository`.
- `SmartsuppWebhookControllerTests.cs` via `WebApplicationFactory` — 401 on bad sig, 200 on good sig with known event, 200 on good sig with unknown event.

Local end-to-end:
```bash
BODY='{"type":"event_callback","event":"conversation.closed","timestamp":"2026-05-12T10:00:00Z","account_id":"x","app_id":"y","data":{}}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -hex | awk '{print $2}')
curl -X POST http://localhost:5000/api/webhooks/smartsupp \
  -H "X-Smartsupp-Hmac: $SIG" -H "Content-Type: application/json" -d "$BODY"
```

### 9. Operational steps (outside code)

1. Once BE+FE merged and deployed to staging + prod, email `support@smartsupp.com`:
   - Subject: "Webhook registration request"
   - Both URLs: `https://<staging-host>/api/webhooks/smartsupp` and `https://<prod-host>/api/webhooks/smartsupp`
   - Events: `conversation.created`, `conversation.updated`, `conversation.closed`, `message.created`
2. Save the `app_id` and shared secret they reply with into `Smartsupp:WebhookSecret` + `Smartsupp:WebhookAppId` per environment.
3. Validate via the Smartsupp delivery log (if exposed) plus our app logs. Use "Sync now" to backfill anything that happened during the gap.

## Critical files

**New:**
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/{ProcessWebhookEventRequest,Response,Handler}.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RunManualSync/{RunManualSyncRequest,Response,Handler}.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/{SmartsuppHmacVerifierTests,ProcessWebhookEventHandlerTests,RunManualSyncHandlerTests,SmartsuppWebhookControllerTests}.cs`

**Edited:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs` (add `WebhookSecret`, `WebhookAppId`; remove `PollIntervalMinutes`)
- `backend/src/Anela.Heblo.API/appsettings.json` (Smartsupp section)
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs` (add `POST sync` action)
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` (remove sync-state methods, add timestamp guard in `UpsertConversationAsync`)
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` (drop sync-state methods)
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (drop `SmartsuppSyncState` DbSet)
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs` (drop job registration)
- `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx` (add "Sync now" button)
- `frontend/src/api/generated/api-client.ts` (regenerated on build)

**Deleted:**
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs`
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppSyncState.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs`

## Verification

Before declaring done:

- [ ] BE: `dotnet build` + `dotnet format` (per CLAUDE.md gate)
- [ ] FE: `npm run build` + `npm run lint` (per CLAUDE.md gate)
- [ ] All Smartsupp unit + integration tests green (new + existing)
- [ ] Manual curl test against locally-running API: valid HMAC → 200 + row upserted; tampered body → 401; unknown event → 200 + log line.
- [ ] "Sync now" button in `SmartsuppChatsPage` returns counts and refreshes the list.
- [ ] After deployment, Smartsupp support has registered both URLs and we see at least one real `conversation.*` or `message.created` event in app logs with `signature pass`.
- [ ] No runtime exceptions referencing `SmartsuppSyncState` anywhere.