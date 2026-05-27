I have enough context to produce the architecture review. Let me write it now.

# Architecture Review: Smartsupp Webhook Receiver

## Skip Design: true

## Architectural Fit Assessment

The proposal aligns cleanly with the existing Clean Architecture layout. The codebase already organizes Smartsupp work as a vertical slice (`Features/Smartsupp/UseCases/...` for application logic, `Domain/Features/Smartsupp` for entities and ports, `Persistence/Smartsupp` for repository, `Adapters/Anela.Heblo.Adapters.Smartsupp` for the API client). The webhook receiver and manual sync both fit the established MediatR-handler-per-use-case pattern (mirrors `ListConversations`, `GetConversation`).

Two integration points need attention:

1. **Webhook controller is the only `[AllowAnonymous]` POST that ingests untrusted bytes.** It must not extend `BaseApiController` because `BaseApiController.HandleResponse` returns a JSON `BaseResponse` envelope (with `success`, `errorCode`), while Smartsupp expects an empty `200`. The webhook controller should inherit `ControllerBase` directly.
2. **Raw body capture conflicts with `RequestLoggingMiddleware`**, which already calls `EnableBuffering()` and reads the body for "detailed" endpoints. We must (a) ensure `EnableBuffering` is called before any body read, (b) call `Request.Body.Seek(0, SeekOrigin.Begin)` before reading inside the controller, and (c) confirm `IsSensitiveHeader` covers `X-Smartsupp-Hmac` so it never appears in logs.

The "Sync now" use case is a clean port of the existing `SmartsuppSyncJob.ExecuteAsync` body — no architectural risk, but it must not regress the existing contact-fetch caching behaviour that the job already implements.

## Proposed Architecture

### Component Overview

```
┌───────────────────────────────┐        ┌────────────────────────────────────┐
│ Smartsupp (event source)      │  POST  │ Anela.Heblo.API                    │
│                               │ ─────► │ SmartsuppWebhookController        │
│ X-Smartsupp-Hmac header       │        │   [AllowAnonymous] (ControllerBase)│
└───────────────────────────────┘        │ 1. Buffer + read raw body bytes    │
                                         │ 2. HmacVerifier.Verify             │
                                         │ 3. Parse envelope (JSON)           │
                                         │ 4. (optional) app_id match         │
                                         │ 5. _mediator.Send(...)             │
                                         │ 6. Always return 200 (except 401)  │
                                         └─────────────┬──────────────────────┘
                                                       │
                                                       ▼
        ┌──────────────────────────────────────────────────────────────────┐
        │ Application.Features.Smartsupp.UseCases.ProcessWebhookEvent      │
        │   ProcessWebhookEventHandler : IRequestHandler<…>                │
        │   - switch (EventName)                                           │
        │     • conversation.* → MapToConversation → Repo.Upsert + Save    │
        │     • message.created → MapToMessage → Repo.UpsertMessages       │
        │     • default        → log info, Handled = false                 │
        └─────────────────────────────────────┬────────────────────────────┘
                                              │
                                              ▼
   ┌───────────────────────────────┐    ┌─────────────────────────────────┐
   │ Domain.Features.Smartsupp     │    │ Persistence.Smartsupp           │
   │ ISmartsuppRepository (trimmed)│◄───│ SmartsuppRepository             │
   │   ...                         │    │  + UpdatedAt timestamp guard    │
   └───────────────────────────────┘    └─────────────────────────────────┘

  Manual flow:
  ┌──────────────────┐    POST /api/smartsupp/sync   ┌────────────────────┐
  │ SmartsuppChats   │ ───────────────────────────►  │ SmartsuppController│
  │ Page (FE)        │    [Authorize]                │ (existing)         │
  │   "Sync now" btn │                               │  → MediatR         │
  └──────────────────┘                               └─────────┬──────────┘
                                                               ▼
                       ┌─────────────────────────────────────────────┐
                       │ UseCases.RunManualSync.RunManualSyncHandler │
                       │   uses ISmartsuppApiClient + ISmartsuppRepo│
                       │   (port of SmartsuppSyncJob.ExecuteAsync)   │
                       └─────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Webhook controller does not inherit `BaseApiController`

**Options considered:**
1. Extend `BaseApiController` (consistent with other controllers).
2. Inherit `ControllerBase` directly (bespoke for this anonymous webhook).

**Chosen approach:** Inherit `ControllerBase` directly.

**Rationale:** `BaseApiController.HandleResponse` only handles `BaseResponse` derivatives, and unconditionally writes a JSON body. Smartsupp expects an empty 200 (and an empty 401). Forcing the webhook into that envelope pattern would either pollute the response or require special-casing inside the base class. A focused `ControllerBase` with two `return Unauthorized()` / `return Ok()` exits is simpler.

#### Decision 2: HMAC verifier is a static helper class in `Anela.Heblo.API`, not the controller

**Options considered:**
1. Inline static method in the controller file (per brief).
2. Standalone `internal static class SmartsuppHmacVerifier` in the API project.
3. ASP.NET filter / middleware abstraction.

**Chosen approach:** Standalone `internal static class SmartsuppHmacVerifier` in `backend/src/Anela.Heblo.API/Webhooks/Smartsupp/SmartsuppHmacVerifier.cs`.

**Rationale:** The spec calls for `SmartsuppHmacVerifierTests.cs` — testing a static method on a controller requires either reflection or making it `internal` plus `InternalsVisibleTo`. Extracting one file does not introduce an abstraction layer; it just lets the verifier be unit-tested in isolation with clean inputs (bytes + secret + header). A full filter/middleware would be over-engineered.

#### Decision 3: Webhook handler always returns 200 except on auth failure

**Options considered:**
1. Map handler exceptions to 500 (default ASP.NET behaviour).
2. Catch-all in controller maps everything except HMAC mismatch to 200 + structured log.

**Chosen approach:** Catch-all in controller (Option 2).

**Rationale:** Smartsupp retries on any non-2xx. A transient DB issue should not flood us with retries that all fail. Acknowledging the event after HMAC verification (which proves the request is authentic Smartsupp traffic) and logging the downstream failure for replay via "Sync now" is more reliable than retry storms. The trade-off — possible silent message loss — is acceptable because the "Sync now" backfill exists.

#### Decision 4: Timestamp guard lives **inside** `UpsertConversationAsync`, not in the handler

**Options considered:**
1. Handler reads conversation, compares timestamps, decides whether to call upsert.
2. `UpsertConversationAsync` short-circuits internally when `existing.UpdatedAt > incoming.UpdatedAt`.

**Chosen approach:** Option 2 — guard inside repository method.

**Rationale:** Idempotency is a repository invariant: both the webhook handler and `RunManualSyncHandler` use this same upsert. Embedding the guard in the handler would either duplicate the logic or leak the timestamp-comparison concern out of persistence. Existing call sites (manual sync) automatically benefit.

#### Decision 5: `JsonElement` envelope vs. typed event payloads

**Options considered:**
1. Deserialize envelope to `ProcessWebhookEventRequest { ... JsonElement Data; }`, let handler re-deserialize `Data` per event type.
2. Define discriminated payload classes per event up front.

**Chosen approach:** Option 1 — `JsonElement Data` field, handler dispatches.

**Rationale:** Smartsupp's webhook payload shape per event is not fully formalised in our codebase, and the brief explicitly anticipates new event types arriving (we must not 4xx them). `JsonElement` defers parsing until the handler knows it cares about the event, which keeps the controller path resilient. The existing `SmartsuppConversationData` / `SmartsuppMessageData` adapters already accept similar shapes — handler reuses the JSON property casing (`snake_case_lower`) by configuring `JsonSerializerOptions` identically to `SmartsuppApiClient`.

#### Decision 6: Manual sync endpoint goes on existing `SmartsuppController`, not a new controller

**Options considered:**
1. New `SmartsuppSyncController`.
2. Add `[HttpPost("sync")]` to existing `SmartsuppController`.

**Chosen approach:** Option 2.

**Rationale:** Spec says so, and the action belongs to the same authorization scope. Keeping it grouped with conversations queries means the OpenAPI client surfaces it under `smartsupp_*` methods (e.g. `smartsupp_Sync`), consistent with the existing FE call sites.

## Implementation Guidance

### Directory / Module Structure

**New files:**
```
backend/src/Anela.Heblo.API/
  Controllers/
    SmartsuppWebhookController.cs                   (NEW, ControllerBase, AllowAnonymous)
  Webhooks/Smartsupp/
    SmartsuppHmacVerifier.cs                        (NEW, internal static)

backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/
  ProcessWebhookEvent/
    ProcessWebhookEventRequest.cs                   (class, IRequest<…>)
    ProcessWebhookEventResponse.cs                  (class)
    ProcessWebhookEventHandler.cs
  RunManualSync/
    RunManualSyncRequest.cs                         (class, IRequest<…>)
    RunManualSyncResponse.cs                        (class)
    RunManualSyncHandler.cs

backend/test/Anela.Heblo.Tests/Features/Smartsupp/
  SmartsuppHmacVerifierTests.cs
  ProcessWebhookEventHandlerTests.cs
  RunManualSyncHandlerTests.cs
  SmartsuppWebhookControllerTests.cs                (WebApplicationFactory)
```

**Edited files:**
```
backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs
backend/src/Anela.Heblo.API/appsettings.json                        (Smartsupp section)
backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs      (+ POST /sync)
backend/src/Anela.Heblo.API/Middleware/RequestLoggingMiddleware.cs  (ensure X-Smartsupp-Hmac in IsSensitiveHeader)
backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs (remove sync-state methods, add UpdatedAt guard)
backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs
backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs         (drop DbSet)
backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs
frontend/src/api/hooks/useSmartsupp.ts                              (add useTriggerManualSync mutation)
frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx
```

**Deleted files:**
```
backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs
backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppSyncState.cs
backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppSyncStateConfiguration.cs     (← brief missed this)
backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs
```

### Interfaces and Contracts

**`SmartsuppHmacVerifier` (new, internal static)**
```csharp
internal static class SmartsuppHmacVerifier
{
    public static bool Verify(byte[] rawBody, string? headerValue, string secret);
}
```
- Constant-time hex compare via `CryptographicOperations.FixedTimeEquals`
- Returns `false` if `secret` is empty (fail-closed)
- Returns `false` if `headerValue` is null/whitespace

**`ProcessWebhookEventRequest` (class, MediatR)**
```csharp
public class ProcessWebhookEventRequest : IRequest<ProcessWebhookEventResponse>
{
    public string EventName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string AccountId { get; set; } = "";
    public string AppId { get; set; } = "";
    public JsonElement Data { get; set; }
}
```

**`ProcessWebhookEventResponse` (class)** — does **not** inherit `BaseResponse`. The webhook controller never serializes the response; it just inspects `Handled` for logging.

**`RunManualSyncRequest` / `RunManualSyncResponse`** — classes (DTO rule). `RunManualSyncResponse` **must** inherit `BaseResponse` because the `SmartsuppController` action returns through `HandleResponse`. Brief is inconsistent here — see Specification Amendments.

**`ISmartsuppRepository` (trimmed)** — remove `GetOrCreateSyncStateAsync`, `SetSyncWatermarkAsync`. Keep everything else.

**`SmartsuppOptions` (updated)**
```csharp
public class SmartsuppOptions
{
    public const string SectionKey = "Smartsupp";
    public string ApiToken { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.smartsupp.com/v2/";
    public int HttpTimeoutSeconds { get; set; } = 30;
    public string WebhookSecret { get; set; } = "";
    public string? WebhookAppId { get; set; }
    // PollIntervalMinutes removed
}
```

### Data Flow

**Webhook ingest (happy path, `message.created`):**
1. `POST /api/webhooks/smartsupp` → `RequestLoggingMiddleware` runs first; `X-Smartsupp-Hmac` is in the sensitive-header list and not logged.
2. `SmartsuppWebhookController.Receive`:
   1. Call `Request.EnableBuffering()`.
   2. Read raw bytes from `Request.Body` into `byte[]` (limit to e.g. 1 MB — see Risks).
   3. `Request.Body.Position = 0` (so downstream can re-read if needed).
   4. `SmartsuppHmacVerifier.Verify(rawBody, Request.Headers["X-Smartsupp-Hmac"], options.WebhookSecret)` → `false` → log warning with remote IP only → `return Unauthorized()`.
   5. `JsonDocument.Parse(rawBody)` to extract `event`, `timestamp`, `account_id`, `app_id`, `data`.
   6. If `options.WebhookAppId` is set and differs from envelope `app_id` → log warning, `return Unauthorized()`.
   7. Build `ProcessWebhookEventRequest`, `_mediator.Send(...)`.
   8. Log `LogInformation("smartsupp webhook event={Event} account={AccountId} app={AppId}", ...)`.
   9. `return Ok()` (empty body) — whether `Handled` was true or false.
   10. Any unexpected exception in steps 5–8 → log error, `return Ok()` (suppress retry).
3. `ProcessWebhookEventHandler` switches on `EventName`:
   - For `message.created`: read `conversationId` and message fields from `Data`, build `SmartsuppMessage`, `await _repository.UpsertMessagesAsync(conversationId, [msg], ct)`, `await _repository.SaveChangesAsync(ct)`.
   - Repository handles idempotent upsert internally.

**Manual sync flow:**
1. UI button → `useTriggerManualSync` mutation → `POST /api/smartsupp/sync` (no body).
2. `SmartsuppController.RunSync` → `_mediator.Send(new RunManualSyncRequest())`.
3. `RunManualSyncHandler`:
   1. `since = request.Since ?? DateTime.UtcNow.AddDays(-7)`.
   2. Page through `_apiClient.SearchConversationsAsync(cursor, 50, ct)` until cursor is null.
   3. Per item where `item.UpdatedAt > since`: build conversation, `UpsertConversationAsync` (timestamp guard inside is no-op when `since` is fresh), then `GetConversationMessagesAsync` → `UpsertMessagesAsync`.
   4. `SaveChangesAsync` per page (already done in current `SmartsuppSyncJob`).
   5. Build response with counts + `StartedAt`/`CompletedAt`.
4. Controller returns via `HandleResponse(response)` (200 envelope).
5. FE toasts counts and invalidates `SMARTSUPP_QUERY_KEYS.conversations`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Body bytes consumed before HMAC computed (RequestLoggingMiddleware reads body) | HIGH | Explicit `Request.Body.Position = 0` before reading. Smoke test in `SmartsuppWebhookControllerTests` ensures HMAC succeeds when middleware is in the pipeline. |
| `X-Smartsupp-Hmac` accidentally written to request logs | HIGH | Add header name to `RequestLoggingMiddleware.IsSensitiveHeader`. Verify by grep + log inspection during local test. |
| Empty/missing `WebhookSecret` silently passes any signature | CRITICAL | `SmartsuppHmacVerifier.Verify` returns `false` immediately when secret is empty. Startup validation: log warning at boot if `WebhookSecret` is empty (don't fail — UI-only deploy may not have it yet). |
| Huge request body OOMs the server | MEDIUM | Enforce `MaxRequestBodySize` at the action level: `[RequestSizeLimit(1_048_576)]` (1 MB). Smartsupp payloads are tiny; anything larger is malicious. |
| Out-of-order events cause stale state | MEDIUM | `UpsertConversationAsync` timestamp guard. Documented in test `ProcessWebhookEventHandlerTests.OutOfOrderTimestamp_DoesNotRegressState`. |
| Manual sync triggered repeatedly by impatient operator → API throttling | LOW | FE button disabled during in-flight call. Server already uses Polly retry on 429 in `SmartsuppApiClient`. |
| Webhook handler exception loses event silently | MEDIUM | Catch-all logs error + `app_id` + `event` + payload size; "Sync now" backfill always available. |
| `account_id` shape unknown (string vs. int) | LOW | Use `JsonElement` for envelope parsing and `.GetRawText()` / `.ToString()` to coerce. Document actual value once a real event arrives. |
| Migration already dropped table but code still references `SmartsuppSyncState` → app crashes at startup | CRITICAL | This is the trigger for the work. Verify with `dotnet build` after FR-7 file deletions; grep for `SmartsuppSyncState` returns zero hits in `src/`. |
| Antiforgery / CSRF middleware blocks anonymous POST | LOW | ASP.NET MVC API controllers do not enable antiforgery by default; confirm `app.UseAntiforgery()` is not present (it isn't in this codebase). |

## Specification Amendments

1. **`RunManualSyncResponse` must inherit `BaseResponse`.** The spec describes it as a plain class with counts, but `SmartsuppController` extends `BaseApiController` and uses `HandleResponse<T> where T : BaseResponse`. To keep the existing controller pattern consistent, `RunManualSyncResponse` must inherit `BaseResponse`. The four data fields (`ConversationsProcessed`, `MessagesProcessed`, `StartedAt`, `CompletedAt`) remain.

2. **Delete `SmartsuppSyncStateConfiguration.cs` too.** FR-7 lists `SmartsuppSyncState.cs` but omits its EF configuration at `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppSyncStateConfiguration.cs`. The configuration is applied via `ApplyConfigurationsFromAssembly` and will fail to compile once the entity is gone.

3. **Add `X-Smartsupp-Hmac` to `RequestLoggingMiddleware.IsSensitiveHeader`.** FR-11 says "never log the signature header," but the existing global request-logging middleware will log it for "detailed" endpoints unless explicitly filtered.

4. **Cap request body size at 1 MB.** NFR-2 (security) implicitly assumes bounded payloads. Add `[RequestSizeLimit(1_048_576)]` to the webhook action. This is a routine hardening step.

5. **Extract HMAC verification into its own file** (`Webhooks/Smartsupp/SmartsuppHmacVerifier.cs`), not inline in the controller. FR-2's "inline static helper" wording conflicts with the test plan calling for `SmartsuppHmacVerifierTests.cs`; a sibling file is simpler to test and adds zero abstraction cost.

6. **Frontend uses `useMutation` from `@tanstack/react-query`** to invoke the sync and invalidate `SMARTSUPP_QUERY_KEYS.conversations` on success. The existing `useSmartsupp.ts` hook file should export `useTriggerSmartsuppSync`. Toast surface uses `useToast` from `frontend/src/contexts/ToastContext.tsx`.

7. **Cap manual-sync `Since` floor.** Add a sanity bound: if `Since < UtcNow - 30 days`, clamp to `UtcNow - 30 days` (or reject with a validation error). Prevents the operator from accidentally re-syncing the entire account.

8. **Strip `SmartsuppSyncState` references from `SmartsuppRepository.cs` lines 146–163** *and* remove the now-orphan `Unspecified` helper if it's only used by those methods (verify before deletion).

## Prerequisites

1. **Migration `20260512190557_DropSmartsuppSyncState` already applied** in all target environments (it is — that's what triggered this work).
2. **`Smartsupp:WebhookSecret`** placeholder in `appsettings.json` and real value in `secrets.json` locally. In Azure deployment, the secret must be added to the configured Key Vault / app settings store under `Smartsupp:WebhookSecret` before Smartsupp registration completes.
3. **No registration of `SmartsuppSyncJob`** in `SmartsuppModule.cs` before the file is deleted — otherwise DI fails at startup.
4. **OpenAPI TypeScript client regeneration** must run after BE changes are merged so the FE can call the new `smartsupp_Sync` method (per `docs/development/api-client-generation.md`).
5. **Public-facing webhook URL** for staging and production reachable over HTTPS (no IP allowlist on this path — Smartsupp egress IPs are not documented as static).
6. **`gh` CLI / operator process** to send the Smartsupp registration email after deployment, capture the returned `app_id` and shared secret, and inject them per environment (manual operational step, documented in the PR description and in `docs/integrations/` — recommend creating `docs/integrations/smartsupp-webhook.md` capturing the registration recipe alongside the existing `shoptet-api.md`).