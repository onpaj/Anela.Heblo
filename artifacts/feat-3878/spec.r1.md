# Specification: Move Smartsupp contact-enrichment REST call out of SmartsuppRepository

## Summary
`SmartsuppRepository` (Persistence layer) currently injects `ISmartsuppApiClient` and calls the live Smartsupp REST API mid-upsert to fetch a missing contact. This couples a database write to third-party network I/O, violates the module's own layering rule (external calls belong in `Adapters.*`, orchestrated from the Application layer), and makes the repository untestable without stubbing HTTP. This spec moves contact enrichment into the Application layer so `SmartsuppRepository` becomes a pure persistence class, while preserving every observable behavior of `UpsertConversationAsync` today (fail-open on REST error, contact-id wipe when REST returns nothing, no REST call when the contact is already known locally, denormalized name/email hydration).

## Background
`UpsertConversationAsync` (`backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs:86-187`) looks up the conversation's linked `SmartsuppContact` locally; if it's missing, it calls `TryFetchAndStageContactAsync` (`:302-328`), which invokes `_apiClient.GetContactAsync(...)` against the live Smartsupp REST API, maps the response, and stages the contact via `UpsertContactAsync` before the conversation's raw-SQL upsert runs. This happens inside every webhook reaction that calls `UpsertConversationAsync`: `ConversationOpenedReaction`, `ConversationRatedReaction`, `ConversationClosedReaction`, `ConversationClosedByContactReaction`, `ConversationAgentAssignedReaction`, `ConversationAgentUnassignedReaction`, and `ConversationReplyReactionBase` (base for the reply reactions) — all invoked from `ProcessWebhookEventHandler.Handle` on the `POST /api/webhooks/smartsupp` request path.

`SmartsuppApiClient`'s Polly pipeline retries up to 3 times with exponential backoff and `SmartsuppOptions.HttpTimeoutSeconds` defaults to 30s, so a degraded Smartsupp endpoint can stall webhook processing for tens of seconds on a path whose only job, from the caller's perspective, is "save a row." No caller in `Reactions/` can see or bound this I/O.

Precedent: issue #3731 established the same layering rule for `AnalyticsRepository`. `RefreshOrphanContactsHandler` (`Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`) already demonstrates the target shape — an Application-layer handler that owns `ISmartsuppApiClient` directly and calls the repository only to persist.

This issue **only records the finding**; per its "Suggested direction" section the fix was explicitly deferred. This spec turns that suggestion into an actionable, testable implementation plan.

## Functional Requirements

### FR-1: `SmartsuppRepository` no longer depends on `ISmartsuppApiClient`
Remove the `ISmartsuppApiClient` constructor parameter and field from `SmartsuppRepository`. `TryFetchAndStageContactAsync` and `MapContactDataToEntity` (which map REST DTOs to `SmartsuppContact`) are removed from the repository; `MapContactDataToEntity`'s mapping logic moves to the Application layer alongside its caller.

**Acceptance criteria:**
- `SmartsuppRepository`'s constructor takes only `ApplicationDbContext` and `ILogger<SmartsuppRepository>`.
- No `using Anela.Heblo.Domain.Features.Smartsupp.ISmartsuppApiClient` call site remains inside `Anela.Heblo.Persistence`.
- `dotnet build` succeeds with no new warnings introduced by the removal.

### FR-2: `UpsertConversationAsync` persists what it is given — no I/O, no wipe
`ISmartsuppRepository.UpsertConversationAsync` keeps its exact current signature and continues to: look up the locally-known `SmartsuppContact` by `conversation.ContactId` when the incoming DTO doesn't already carry `ContactName`/`ContactEmail`, hydrate `ContactName`/`ContactEmail` from that local contact when found, and write the conversation row via the existing raw-SQL upsert (unchanged SQL). It **no longer** calls `_apiClient`, and it **no longer** clears `conversation.ContactId` when the contact isn't found locally — that decision moves to the caller (FR-3), which now supplies a `SmartsuppContact` it has already resolved (via REST) or an explicit "no contact" signal before calling the repository.

**Acceptance criteria:**
- Given a `SmartsuppConversation` with `ContactId` set to an id present in `SmartsuppContacts`, `UpsertConversationAsync` hydrates `ContactName`/`ContactEmail` from that row exactly as today (existing behavior of `SmartsuppRepositoryDenormFieldTests.UpsertConversationAsync_HydratesDenormFields_FromExistingContact`, unchanged).
- Given a `SmartsuppConversation` with `ContactId` set to an id **not** present in `SmartsuppContacts`, `UpsertConversationAsync` performs **zero** calls to any HTTP client (there is none to call) and persists the conversation row with the `ContactId` exactly as it was passed in — no wipe. It is the caller's responsibility to have already cleared `ContactId` if the enrichment step decided the contact could not be resolved.
- `UpsertConversationAsync` still writes the exact same set of columns via the same `ON CONFLICT ... WHERE EXCLUDED."UpdatedAt" >= ... "UpdatedAt"` guard — no SQL changes.

### FR-3: New Application-layer contact enrichment step
Introduce a single Application-layer service, `ISmartsuppContactEnricher` (implementation `SmartsuppContactEnricher`, placed in `Anela.Heblo.Application/Features/Smartsupp/Infrastructure/`), with one method:

```csharp
Task<SmartsuppConversation> EnrichContactAsync(SmartsuppConversation conversation, CancellationToken cancellationToken);
```

Given a conversation whose `ContactId` is set, it:
1. Returns the conversation unchanged if `ContactId` is null. Otherwise checks whether a `SmartsuppContact` row already exists locally for that id via a new `ISmartsuppRepository.ContactExistsAsync(contactId, ct)` method (a plain existence read, added to the repository interface). **This existence check must query the `SmartsuppContacts` table — it must NOT be based on whether the incoming `SmartsuppConversation` DTO already carries non-null `ContactName`/`ContactEmail`.** Smartsupp webhook payloads for conversation events routinely inline `contact_name`/`contact_email` directly (`SmartsuppPayloadMapper.MapConversation` reads `contact_name`/`contact_email` straight off the JSON), so a DTO-field check would wrongly skip fetching-and-persisting a brand-new contact whenever the webhook happens to inline those fields — silently starving the `SmartsuppContacts` table for contacts that only ever arrive this way, breaking anything that joins on it (e.g. `GetSmartsuppContactShoptetInfoHandler`, `KnowledgeBaseSmartsuppKnowledgeSource`). If the contact already exists locally, hydrate `conversation.ContactName`/`ContactEmail` from it (`??=` semantics) and return — no REST call (this is the exact case covered by `DoesNotCallRest_WhenContactAlreadyInDb`).
2. Otherwise calls `ISmartsuppApiClient.GetContactAsync(conversation.ContactId, ct)`.
3. On success with a non-null result: maps the DTO to a `SmartsuppContact` (moved `MapContactDataToEntity` logic, `DateTimeKind.Utc` handling preserved verbatim — see the existing code comment on why this matters), persists it via `ISmartsuppRepository.UpsertContactAsync`, and sets `conversation.ContactName`/`ContactEmail` from the mapped contact (only where not already set, matching today's `??=` semantics).
4. On success with a null result, or on any exception from the REST call: logs a warning (same message/shape as today's `_logger.LogWarning(ex, "smartsupp: failed to fetch contact {ContactId} while upserting conversation; continuing without link", contactId)`, plus an explicit warning for the "REST returned null" case) and sets `conversation.ContactId = null` — this is where the fail-open wipe now happens, one layer up from before.
5. Returns the (possibly mutated) conversation.

**Acceptance criteria:**
- `SmartsuppContactEnricher` depends only on `ISmartsuppApiClient`, `ISmartsuppRepository`, and `ILogger<SmartsuppContactEnricher>` — no `ApplicationDbContext`.
- Behavior-preserving unit tests (ported from `SmartsuppRepositoryUnknownContactFetchTests`, see FR-5) pass against the new class with `ISmartsuppRepository` mocked.
- The exception type caught is `Exception` (broad, matching today's fail-open contract at `SmartsuppRepository.cs:312`) — not narrowed, to avoid behavior change; this is called out explicitly as a design carry-over, not a new decision.

### FR-4: All `UpsertConversationAsync` call sites route through the enricher first
Every reaction that currently relies on the repository's implicit REST fetch must call `ISmartsuppContactEnricher.EnrichContactAsync` before calling `ISmartsuppRepository.UpsertConversationAsync`:
- `ConversationOpenedReaction`
- `ConversationRatedReaction`
- `ConversationClosedReaction`
- `ConversationClosedByContactReaction`
- `ConversationAgentAssignedReaction`
- `ConversationAgentUnassignedReaction`
- `ConversationReplyReactionBase` (shared base for reply-triggering reactions — enrich before the conversation-upsert branch; the message-only upsert branch is unaffected)

`RefreshOrphanContactsHandler` already calls `ISmartsuppApiClient.GetContactAsync`... actually it calls `GetConversationAsync` for a different purpose (re-discovering `ContactId`) and then calls `UpsertConversationAsync`, which today implicitly re-triggers the REST contact fetch inside the repository. After this change, `RefreshOrphanContactsHandler` must also call the enricher after setting `local.ContactId = remote.ContactId` and before `UpsertConversationAsync`, or its designed re-backfill behavior silently stops working (the whole point of that handler is to re-trigger contact enrichment for orphaned rows).

**Acceptance criteria:**
- Every reaction listed above calls `IServiceProvider`-injected `ISmartsuppContactEnricher.EnrichContactAsync(conversation, ct)` immediately before `Repository.UpsertConversationAsync(conversation, ct)`, using the returned (possibly mutated) conversation.
- `RefreshOrphanContactsHandler` is updated to call the enricher after re-attaching `ContactId`, preserving its `Updated`/`Failed`/`SkippedNoContactId` counting semantics.
- `ProcessWebhookEventHandler`'s single `SaveChangesAsync` call after `reaction.HandleAsync` is unchanged — the enricher's `UpsertContactAsync` call and the reaction's `UpsertConversationAsync` call are both still flushed by that one call (transactional grouping preserved).

### FR-5: Test suite reflects the new boundary
- `SmartsuppRepositoryUnknownContactFetchTests` (`backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUnknownContactFetchTests.cs`) currently constructs `SmartsuppRepository` with a mocked `ISmartsuppApiClient` and asserts REST-fetch behavior. Since the repository no longer takes that dependency, this file is split:
  - Tests asserting **REST-calling behavior** (`FetchesContactViaRest_WhenLocalContactMissing`, `WipesContactId_WhenRestReturnsNull`, `WipesContactIdAndLogsWarning_WhenRestThrows`, `DoesNotCallRest_WhenContactAlreadyInDb`) move to a new `SmartsuppContactEnricherTests` under `backend/test/Anela.Heblo.Tests/Features/Smartsupp/` (Application-layer test project location, mirroring `RefreshOrphanContactsHandler`'s existing test placement), rewritten against `SmartsuppContactEnricher` with `ISmartsuppRepository` mocked instead of a live `ApplicationDbContext`.
  - `ListOrphanContactConversationIdsAsync_ReturnsOnlyConversationsWithNoNameOrEmail` stays in Persistence tests (pure repository behavior, no REST).
- `SmartsuppRepositoryUpsertIntegrationTests` and `SmartsuppRepositoryUpdatedAtGuardTests` (real-Postgres integration tests) are updated only where they construct `SmartsuppRepository` with an `ISmartsuppApiClient` mock argument — that argument is dropped from the constructor call; assertions about SQL upsert behavior (COALESCE, `UpdatedAt` guard) are unchanged since the SQL is unchanged.
- `ConversationReactionsTests` and `SmartsuppWebhookControllerTests` gain coverage (or updated mocks) for the new `ISmartsuppContactEnricher` dependency injected into the affected reactions.

**Acceptance criteria:**
- `dotnet test` passes for the full `Anela.Heblo.Tests` project after the move, with no reduction in the specific behaviors covered (REST-fetch-on-miss, fail-open on error, fail-open on null, no-call-when-known, orphan listing, denorm hydration, COALESCE/UpdatedAt-guard SQL behavior).
- No test constructs `SmartsuppRepository` with an `ISmartsuppApiClient` argument after this change.

## Non-Functional Requirements

### NFR-1: Behavior parity
This is a pure refactor of *where* the contact-enrichment REST call is orchestrated from, not a behavior change. Webhook processing latency, retry/backoff characteristics, fail-open semantics, and the resulting persisted rows must be identical before and after. No new caching, batching, or timeout policy is introduced by this change (those are legitimate follow-ups the issue explicitly does not ask for).

### NFR-2: Testability
Post-change, `SmartsuppRepository` must be fully testable against `ApplicationDbContext` alone (already true for its other methods) with zero HTTP mocking required. `SmartsuppContactEnricher` must be testable with `ISmartsuppApiClient` and `ISmartsuppRepository` both mocked — no `ApplicationDbContext` required for its unit tests.

### NFR-3: No DI wiring regressions
`ISmartsuppContactEnricher` must be registered in the DI container (`SmartsuppModule.cs` or equivalent, wherever `ISmartsuppApiClient` and `ISmartsuppRepository` are currently registered) so all seven reaction classes and `RefreshOrphanContactsHandler` resolve it without runtime `InvalidOperationException`s. `dotnet build` plus the app's existing DI-validation test coverage (if any — check for a "container resolves all registered handlers" test) must pass unchanged.

## Data Model
No schema changes. `SmartsuppContact` and `SmartsuppConversation` entities, their EF configurations, and all existing migrations are untouched. This is purely a code-organization change; no new migration is required.

## API / Interface Design

**New interface** (`Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs`):
```csharp
namespace Anela.Heblo.Application.Features.Smartsupp.Infrastructure;

public interface ISmartsuppContactEnricher
{
    Task<SmartsuppConversation> EnrichContactAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken);
}
```

**Changed interface** — `ISmartsuppRepository` (`Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`): `UpsertConversationAsync` keeps its exact current signature; only its internal implementation loses the REST call and the `ContactId = null` wipe-on-miss branch. One new method is added for `SmartsuppContactEnricher`'s local-existence check:
```csharp
Task<bool> ContactExistsAsync(string contactId, CancellationToken cancellationToken);
```
This is a plain existence read (`_db.SmartsuppContacts.AsNoTracking().AnyAsync(c => c.Id == contactId, ct)`) — no business logic, same category as the existing `ListOrphanContactConversationIdsAsync`.

**Changed constructor** — `SmartsuppRepository(ApplicationDbContext db, ILogger<SmartsuppRepository> logger)` — drops the `ISmartsuppApiClient apiClient` parameter.

**Changed callers** — the seven reaction classes and `RefreshOrphanContactsHandler` each gain an `ISmartsuppContactEnricher` constructor dependency and one additional `await` call before their existing `UpsertConversationAsync` call.

No public/external API (webhook payload shape, REST responses to Smartsupp, MVC controller contracts) changes.

## Dependencies
- `ISmartsuppApiClient` / `SmartsuppApiClient` (`Anela.Heblo.Adapters.Smartsupp`) — unchanged, just re-homed to be injected into the Application layer instead of Persistence.
- `ISmartsuppRepository.UpsertContactAsync` — now called from the Application layer (`SmartsuppContactEnricher`) in addition to its existing caller (`ContactUpsertWithBackfillReactionBase`); no signature change needed since it already lives on the repository interface.
- MediatR pipeline / DI container (`SmartsuppModule.cs`) — needs the new registration described in NFR-3.

## Out of Scope
- Any change to the Smartsupp REST call's timeout, retry policy, or Polly pipeline configuration.
- Adding a time budget / circuit breaker to the webhook controller (mentioned in the issue as a consequence, not requested as a fix here).
- Batching or caching contact lookups across webhook events.
- Any change to `SmartsuppPresenceRepository`, `SmartsuppWebhookAuditWriter`, or other Persistence-layer classes in the same folder — only `SmartsuppRepository`'s contact-enrichment coupling is addressed, per the issue's exact finding.
- Renaming or restructuring the `Anela.Heblo.Persistence.Smartsupp` namespace/folder beyond removing the `ISmartsuppApiClient` dependency.

## Open Questions

## Status: COMPLETE
