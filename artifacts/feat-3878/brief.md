# [arch-review] Smartsupp: SmartsuppRepository performs outbound Smartsupp REST calls from inside the Persistence layer

## Module
Customer Support (Smartsupp) — module-map part #29

## Finding
`backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` — a Persistence-layer repository — injects the external HTTP client and calls it from inside a database write:

```csharp
// SmartsuppRepository.cs:13,18
private readonly ISmartsuppApiClient _apiClient;
```

`UpsertConversationAsync` (`:86`) reaches out to the Smartsupp REST API mid-upsert when the referenced contact is not already stored:

```csharp
// SmartsuppRepository.cs:97-107
if (linkedContact is null)
{
    linkedContact = await TryFetchAndStageContactAsync(
        conversation.ContactId, conversation.SyncedAt, cancellationToken);
    ...
}
```

`TryFetchAndStageContactAsync` (`:302-328`) calls `_apiClient.GetContactAsync(...)`, maps the response (`:336`) and writes the contact — all from a class whose declared job is data access against `ApplicationDbContext`.

Nothing else in the Persistence assembly does this. Every other external-system call in the codebase lives in an `Adapters.*` project or an Application-layer `Infrastructure/` class.

## Rule
`docs/architecture/development_guidelines.md`, *Code Organization* and *File Organization*, places infrastructure/data access and external integrations in different rings, and `docs/architecture/filesystem.md` puts third-party clients in `Adapters/`. `ISmartsuppApiClient` is itself declared in `Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs` and implemented in `Anela.Heblo.Adapters.Smartsupp/` precisely so that *use cases*, not repositories, orchestrate it.

Precedent in this repository: #3731 ("Analytics: `AnalyticsRepository` is misplaced in Persistence"), same layering rule.

## Why it matters
- **Third-party latency is now inside a DB write.** `SmartsuppOptions.HttpTimeoutSeconds` defaults to 30 (`backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs:8`) and `SmartsuppApiClient`'s Polly pipeline retries up to 3 times with exponential backoff (`SmartsuppApiClient.cs:15-30`). A degraded Smartsupp REST endpoint therefore stalls `POST /api/webhooks/smartsupp` for tens of seconds per event, on a path that only meant to write a row. The webhook controller has no time budget of its own.
- **Callers cannot see the I/O.** Every reaction in `Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/` calls `UpsertConversationAsync` believing it is a database operation; none can choose to skip, batch, or bound the REST lookup.
- **The repository is untestable in isolation.** Any test of conversation upsert has to stub an HTTP client alongside the DbContext.

The fail-open behaviour and the `Kind=Utc` comment at `:330-335` show the failure modes were found the hard way; both belong to the enrichment step, not to persistence.

## Suggested direction
Move the contact-enrichment step out of the repository and into the webhook use case: let `UpsertConversationAsync` persist what it is given, and have the reactions (or a small Application-layer enrichment service) resolve a missing contact via `ISmartsuppApiClient` before calling the repository — leaving `SmartsuppRepository` free of `ISmartsuppApiClient`. `RefreshOrphanContactsHandler` already demonstrates that shape at the Application layer. Do not implement the fix here — this issue only records the finding.

---
_Filed by arch-review of module-map part #29._