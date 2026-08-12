# Design: Route Smartsupp webhook-audit access through a repository contract

No user-facing UI component — the architecture review sets `Skip Design: true` (backend-only refactor,
no new/changed routes, screens, or visual components). UX/UI Design section omitted per instructions.

## Component Design

### `ISmartsuppWebhookAuditRepository` (new — `Anela.Heblo.Domain/Features/Smartsupp/`)

Owns all access to the `SmartsuppWebhookAuditEntry` table. Responsibilities:
- Create a new audit row and return its generated id.
- Update an existing row's processing outcome (status/error/duration/timestamp).
- List rows with the same filter/order/paging semantics `ListWebhookAuditHandler` uses today.
- Fetch a single row, both no-tracking (for read-only display) and tracked (for the replay
  read-modify-write flow).
- Persist pending changes on a tracked entity (`SaveChangesAsync`).
- Purge rows older than a caller-supplied cutoff, returning the deleted count.

Consumers: `ListWebhookAuditHandler`, `GetWebhookAuditEntryHandler`, `ReplayWebhookEventHandler`,
`SmartsuppWebhookAuditCleanupJob`, `SmartsuppWebhookController`.

### `SmartsuppWebhookAuditRepository` (new — `Anela.Heblo.Persistence/Smartsupp/`)

Sole implementation of `ISmartsuppWebhookAuditRepository`, constructor-injected with
`ApplicationDbContext`, following the existing `SmartsuppRepository`/`SmartsuppPresenceRepository`
style (`public sealed class`, one `_db` field). Ports the query/mutation logic currently inline in the
five refactored classes and in `SmartsuppWebhookAuditWriter`, unchanged in semantics.

### `ISmartsuppRepository` (existing — extended by one method)

Gains `FindConversationByIdAsync(string conversationId, CancellationToken)`: a tracked, no-`Include`
lookup of a single `SmartsuppConversation` by primary key, for `RefreshOrphanContactsHandler`'s
existing-conversation check before it re-attaches a recovered `ContactId`.

### Refactored consumers (no interface changes, constructor/import changes only)

- `ListWebhookAuditHandler` — keeps the `MaxTake = 200` clamp and skip/take normalization; delegates
  the query to `ISmartsuppWebhookAuditRepository.ListAsync`; projects the returned entities to
  `WebhookAuditSummaryDto` itself (this projection step moves from inside the EF query to an in-memory
  step after the repository call — see arch-review Specification Amendment #2).
- `GetWebhookAuditEntryHandler` — delegates to `ISmartsuppWebhookAuditRepository.GetByIdAsync`; keeps
  the `ErrorCodes.ResourceNotFound` mapping and `WebhookAuditEntryDto` projection.
- `ReplayWebhookEventHandler` — delegates to `GetForReplayAsync` (tracked) + mutates
  `ReplayCount`/`LastReplayedAt`/`LastReplayedBy` in place + `SaveChangesAsync`; keeps the JSON parsing
  and `IMediator.Send(ProcessWebhookEventRequest)` re-dispatch exactly as today.
- `SmartsuppWebhookAuditCleanupJob` — keeps `RetentionDays = 7` and cutoff computation; delegates the
  purge to `PurgeOlderThanAsync(cutoff)`; keeps its existing presence-purge call to
  `ISmartsuppPresenceRepository` untouched.
- `RefreshOrphanContactsHandler` — replaces `_db.SmartsuppConversations.FirstOrDefaultAsync(...)` with
  `_repository.FindConversationByIdAsync(conversationId, cancellationToken)`; drops the
  `ApplicationDbContext _db` field/constructor param entirely; drops the `_db.ChangeTracker.Clear()`
  call in the catch block (see arch-review Decision 4) rather than relocating it.
- `SmartsuppWebhookController` — swaps its injected `ISmartsuppWebhookAuditWriter` for
  `ISmartsuppWebhookAuditRepository`; calls the same `CreateAsync`/`UpdateOutcomeAsync` methods at the
  same four call sites (signature-missing/mismatch, malformed-JSON, app-id-mismatch, post-dispatch
  outcome); drops its feature-level `using Anela.Heblo.Persistence.Smartsupp;` import.

### `SmartsuppModule.cs` (DI wiring, ADR-004)

Replaces `services.AddScoped<ISmartsuppWebhookAuditWriter, SmartsuppWebhookAuditWriter>();` with
`services.AddScoped<ISmartsuppWebhookAuditRepository, SmartsuppWebhookAuditRepository>();`. No other
Smartsupp bindings change.

### Removed components

`ISmartsuppWebhookAuditWriter` and `SmartsuppWebhookAuditWriter` (both in
`Anela.Heblo.Persistence/Smartsupp/`) are deleted — fully superseded by
`ISmartsuppWebhookAuditRepository`/`SmartsuppWebhookAuditRepository`.

## Data Schemas

No database schema changes — `SmartsuppWebhookAuditEntry` and its EF mapping
(`SmartsuppWebhookAuditEntryConfiguration`) are untouched, and no migration is required.

No HTTP API contract changes — `ListWebhookAuditRequest`/`Response`, `GetWebhookAuditEntryRequest`/
`Response`, `ReplayWebhookEventRequest`/`Response`, and the routes/authorization on
`SmartsuppWebhookAuditController` and `SmartsuppWebhookController` are all unchanged.

### `ISmartsuppWebhookAuditRepository` contract (internal, non-HTTP)

```csharp
public interface ISmartsuppWebhookAuditRepository
{
    Task<Guid> CreateAsync(SmartsuppWebhookAuditEntry entry, CancellationToken cancellationToken);

    Task UpdateOutcomeAsync(
        Guid id,
        SmartsuppWebhookProcessingStatus status,
        string? error,
        int durationMs,
        CancellationToken cancellationToken);

    Task<(List<SmartsuppWebhookAuditEntry> Items, int Total)> ListAsync(
        DateTime? from,
        DateTime? to,
        string? eventName,
        SmartsuppWebhookSignatureStatus? signatureStatus,
        SmartsuppWebhookProcessingStatus? processingStatus,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<SmartsuppWebhookAuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // Tracked — caller mutates ReplayCount/LastReplayedAt/LastReplayedBy, then SaveChangesAsync.
    Task<SmartsuppWebhookAuditEntry?> GetForReplayAsync(Guid id, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken);
}
```

### `ISmartsuppRepository` addition (internal, non-HTTP)

```csharp
Task<SmartsuppConversation?> FindConversationByIdAsync(
    string conversationId,
    CancellationToken cancellationToken);
```
Tracked, no `Include`s.
